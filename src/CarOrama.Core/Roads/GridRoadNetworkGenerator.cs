using CarOrama.Core.Geometry;
using CarOrama.Core.Random;

namespace CarOrama.Core.Roads;

public sealed class GridRoadNetworkGenerator : IRoadNetworkGenerator
{
    private const double TrafficControlLongitudinalClearanceMeters = 0.9;
    private const double TrafficControlRoadEdgeClearanceMeters = 0.75;

    private readonly record struct GridPoint(int X, int Y)
    {
        public string NodeId => $"node:{X}:{Y}";
    }

    private readonly record struct CandidateEdge(GridPoint A, GridPoint B)
    {
        public bool IsHorizontal => A.Y == B.Y;

        public string Id => IsHorizontal
            ? $"road:h:{Math.Min(A.X, B.X)}:{A.Y}"
            : $"road:v:{A.X}:{Math.Min(A.Y, B.Y)}";
    }

    public RoadNetwork Generate(RoadNetworkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        var random = new DeterministicRandom(config.Seed);
        var candidates = BuildCandidates(config);
        var shuffled = candidates.ToList();
        random.Shuffle(shuffled);

        var selected = SelectConnectedEdges(config, candidates, shuffled, random)
            .Concat(candidates.Where(edge => IsArterial(config, edge)))
            .Distinct()
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        var positions = BuildNodePositions(config);
        var incident = BuildIncidentMap(config, selected);
        var nodes = BuildNodes(config, positions, incident);

        var junctionHalfWidths = BuildJunctionHalfWidths(config, selected);
        var laneDrafts = new List<LaneDraft>(selected.Length * config.ArterialLanesPerDirection * 2);
        var segments = new List<RoadSegment>(selected.Length);
        foreach (var edge in selected)
        {
            var segment = BuildSegment(config, edge, positions, junctionHalfWidths, laneDrafts);
            segments.Add(segment);
        }

        var laneByStart = laneDrafts.GroupBy(lane => lane.StartNodeId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var laneByEnd = laneDrafts.GroupBy(lane => lane.EndNodeId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var stopLines = new List<StopLine>();
        var controls = new List<TrafficControl>();
        var intersections = new List<Intersection>();
        var lanes = LinkLanesAndBuildIntersections(
            config,
            nodes,
            laneDrafts,
            laneByStart,
            laneByEnd,
            stopLines,
            controls,
            intersections);

        var spawns = BuildSpawnPoints(lanes, config.BlockSizeMeters);
        var network = new RoadNetwork(
            config.Seed,
            nodes,
            segments,
            lanes,
            intersections,
            stopLines,
            controls,
            spawns);

        var validation = RoadNetworkValidator.Validate(network);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Generated road network is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, validation.Errors)}");
        }

        return network;
    }

    private static IReadOnlyList<CandidateEdge> BuildCandidates(RoadNetworkConfig config)
    {
        var edges = new List<CandidateEdge>();
        for (var y = 0; y < config.GridHeight; y++)
        {
            for (var x = 0; x < config.GridWidth; x++)
            {
                var point = new GridPoint(x, y);
                if (x + 1 < config.GridWidth)
                {
                    edges.Add(new CandidateEdge(point, new GridPoint(x + 1, y)));
                }

                if (y + 1 < config.GridHeight)
                {
                    edges.Add(new CandidateEdge(point, new GridPoint(x, y + 1)));
                }
            }
        }

        return edges;
    }

    private static IReadOnlySet<CandidateEdge> SelectConnectedEdges(
        RoadNetworkConfig config,
        IReadOnlyList<CandidateEdge> candidates,
        IReadOnlyList<CandidateEdge> shuffled,
        DeterministicRandom random)
    {
        var disjointSet = new DisjointSet(config.GridWidth * config.GridHeight);
        var selected = new HashSet<CandidateEdge>();
        foreach (var edge in shuffled)
        {
            var a = (edge.A.Y * config.GridWidth) + edge.A.X;
            var b = (edge.B.Y * config.GridWidth) + edge.B.X;
            if (disjointSet.Union(a, b))
            {
                selected.Add(edge);
            }
        }

        foreach (var edge in candidates)
        {
            if (!selected.Contains(edge) && random.NextDouble() < config.OptionalConnectionProbability)
            {
                selected.Add(edge);
            }
        }

        return selected;
    }

    private static bool IsArterial(RoadNetworkConfig config, CandidateEdge edge)
    {
        var centralRow = (config.GridHeight - 1) / 2;
        var centralColumn = (config.GridWidth - 1) / 2;
        return edge.IsHorizontal ? edge.A.Y == centralRow : edge.A.X == centralColumn;
    }

    private static IReadOnlyDictionary<string, double> BuildJunctionHalfWidths(
        RoadNetworkConfig config,
        IReadOnlyList<CandidateEdge> selected)
    {
        var halfWidths = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var edge in selected)
        {
            var lanesPerDirection = IsArterial(config, edge)
                ? config.ArterialLanesPerDirection
                : config.LocalLanesPerDirection;
            var halfWidth = lanesPerDirection * config.LaneWidthMeters;
            halfWidths[edge.A.NodeId] = Math.Max(halfWidths.GetValueOrDefault(edge.A.NodeId), halfWidth);
            halfWidths[edge.B.NodeId] = Math.Max(halfWidths.GetValueOrDefault(edge.B.NodeId), halfWidth);
        }

        return halfWidths;
    }

    private static Dictionary<string, Vector2D> BuildNodePositions(RoadNetworkConfig config)
    {
        var positions = new Dictionary<string, Vector2D>(StringComparer.Ordinal);
        var originX = -((config.GridWidth - 1) * config.BlockSizeMeters * 0.5);
        var originY = -((config.GridHeight - 1) * config.BlockSizeMeters * 0.5);
        for (var y = 0; y < config.GridHeight; y++)
        {
            for (var x = 0; x < config.GridWidth; x++)
            {
                var point = new GridPoint(x, y);
                positions.Add(
                    point.NodeId,
                    new Vector2D(originX + (x * config.BlockSizeMeters), originY + (y * config.BlockSizeMeters)));
            }
        }

        return positions;
    }

    private static Dictionary<string, List<CandidateEdge>> BuildIncidentMap(
        RoadNetworkConfig config,
        IReadOnlyList<CandidateEdge> selected)
    {
        var incident = new Dictionary<string, List<CandidateEdge>>(StringComparer.Ordinal);
        for (var y = 0; y < config.GridHeight; y++)
        {
            for (var x = 0; x < config.GridWidth; x++)
            {
                incident.Add(new GridPoint(x, y).NodeId, []);
            }
        }

        foreach (var edge in selected)
        {
            incident[edge.A.NodeId].Add(edge);
            incident[edge.B.NodeId].Add(edge);
        }

        return incident;
    }

    private static IReadOnlyList<RoadNode> BuildNodes(
        RoadNetworkConfig config,
        IReadOnlyDictionary<string, Vector2D> positions,
        IReadOnlyDictionary<string, List<CandidateEdge>> incident)
    {
        var nodes = new List<RoadNode>(config.GridWidth * config.GridHeight);
        for (var y = 0; y < config.GridHeight; y++)
        {
            for (var x = 0; x < config.GridWidth; x++)
            {
                var id = new GridPoint(x, y).NodeId;
                var edges = incident[id].OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
                nodes.Add(new RoadNode(id, positions[id], ClassifyIntersection(id, edges), edges.Select(edge => edge.Id).ToArray()));
            }
        }

        return nodes;
    }

    private static IntersectionKind ClassifyIntersection(string nodeId, IReadOnlyList<CandidateEdge> edges)
    {
        return edges.Count switch
        {
            <= 1 => IntersectionKind.DeadEnd,
            2 => AreOpposite(nodeId, edges[0], edges[1]) ? IntersectionKind.Straight : IntersectionKind.Corner,
            3 => IntersectionKind.ThreeWay,
            _ => IntersectionKind.FourWay,
        };
    }

    private static bool AreOpposite(string nodeId, CandidateEdge a, CandidateEdge b)
    {
        var aOther = a.A.NodeId == nodeId ? a.B : a.A;
        var bOther = b.A.NodeId == nodeId ? b.B : b.A;
        return aOther.X == bOther.X || aOther.Y == bOther.Y;
    }

    private static RoadSegment BuildSegment(
        RoadNetworkConfig config,
        CandidateEdge edge,
        IReadOnlyDictionary<string, Vector2D> positions,
        IReadOnlyDictionary<string, double> junctionHalfWidths,
        ICollection<LaneDraft> laneDrafts)
    {
        var start = positions[edge.A.NodeId];
        var end = positions[edge.B.NodeId];
        var direction = (end - start).Normalized();
        var left = direction.PerpendicularLeft();
        var classification = IsArterial(config, edge)
            ? RoadClassification.Arterial
            : RoadClassification.Local;
        var lanesPerDirection = classification == RoadClassification.Arterial
            ? config.ArterialLanesPerDirection
            : config.LocalLanesPerDirection;
        var startInset = junctionHalfWidths[edge.A.NodeId] + config.IntersectionApproachSetbackMeters;
        var endInset = junctionHalfWidths[edge.B.NodeId] + config.IntersectionApproachSetbackMeters;
        var laneStart = start + (direction * startInset);
        var laneEnd = end - (direction * endInset);
        var laneIds = new List<string>(lanesPerDirection * 2);

        for (var index = 0; index < lanesPerDirection; index++)
        {
            var offset = config.LaneWidthMeters * (index + 0.5);
            var abId = $"lane:{edge.Id}:ab:{index}";
            var baId = $"lane:{edge.Id}:ba:{index}";
            var abCenter = new[]
            {
                laneStart - (left * offset),
                laneEnd - (left * offset),
            };
            var baCenter = new[]
            {
                laneEnd + (left * offset),
                laneStart + (left * offset),
            };

            laneDrafts.Add(CreateLaneDraft(
                config,
                classification,
                abId,
                edge.Id,
                edge.A.NodeId,
                edge.B.NodeId,
                abCenter));
            laneDrafts.Add(CreateLaneDraft(
                config,
                classification,
                baId,
                edge.Id,
                edge.B.NodeId,
                edge.A.NodeId,
                baCenter));
            laneIds.Add(abId);
            laneIds.Add(baId);
        }

        return new RoadSegment(
            edge.Id,
            edge.A.NodeId,
            edge.B.NodeId,
            new[] { start, end },
            classification,
            lanesPerDirection,
            config.LaneWidthMeters * lanesPerDirection * 2.0,
            config.SidewalkWidthMeters,
            laneIds);
    }

    private static LaneDraft CreateLaneDraft(
        RoadNetworkConfig config,
        RoadClassification classification,
        string id,
        string segmentId,
        string startNodeId,
        string endNodeId,
        IReadOnlyList<Vector2D> center)
    {
        var direction = (center[^1] - center[0]).Normalized();
        var left = direction.PerpendicularLeft() * (config.LaneWidthMeters * 0.5);
        var boundaryLeft = center.Select(point => point + left).ToArray();
        var boundaryRight = center.Select(point => point - left).ToArray();
        return new LaneDraft(
            id,
            segmentId,
            startNodeId,
            endNodeId,
            center,
            boundaryLeft,
            boundaryRight,
            direction,
            config.LaneWidthMeters,
            classification == RoadClassification.Arterial
                ? config.ArterialSpeedLimitMetersPerSecond
                : config.LocalSpeedLimitMetersPerSecond);
    }

    private static IReadOnlyList<Lane> LinkLanesAndBuildIntersections(
        RoadNetworkConfig config,
        IReadOnlyList<RoadNode> nodes,
        IReadOnlyList<LaneDraft> drafts,
        IReadOnlyDictionary<string, LaneDraft[]> laneByStart,
        IReadOnlyDictionary<string, LaneDraft[]> laneByEnd,
        ICollection<StopLine> stopLines,
        ICollection<TrafficControl> controls,
        ICollection<Intersection> intersections)
    {
        var successorMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var stopLineByLane = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var incoming = laneByEnd.GetValueOrDefault(node.Id, []);
            var outgoing = laneByStart.GetValueOrDefault(node.Id, []);
            var controlled = node.Kind is IntersectionKind.ThreeWay or IntersectionKind.FourWay;
            var trafficLight = controlled && StableControlChoice(config.Seed, node.Id);
            var controlIds = new List<string>();

            foreach (var approach in incoming.OrderBy(lane => lane.Id, StringComparer.Ordinal))
            {
                var successors = outgoing
                    .Where(candidate => candidate.SegmentId != approach.SegmentId)
                    .Select(candidate => candidate.Id)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (successors.Length == 0)
                {
                    successors = outgoing.Select(candidate => candidate.Id).Order(StringComparer.Ordinal).ToArray();
                }

                successorMap.Add(approach.Id, successors);

                if (!controlled)
                {
                    continue;
                }

                var stopLineId = $"stop-line:{approach.Id}";
                var endpoint = approach.CenterLine[^1];
                var halfWidth = approach.Direction.PerpendicularLeft() * (approach.WidthMeters * 0.5);
                stopLines.Add(new StopLine(stopLineId, approach.Id, node.Id, endpoint + halfWidth, endpoint - halfWidth));
                stopLineByLane.Add(approach.Id, stopLineId);
            }

            if (controlled)
            {
                foreach (var approachGroup in incoming
                    .GroupBy(lane => lane.SegmentId, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    var approachLanes = approachGroup.OrderBy(lane => lane.Id, StringComparer.Ordinal).ToArray();
                    var representative = approachLanes[0];
                    var direction = representative.Direction;
                    var endpoint = representative.CenterLine[^1];
                    var longitudinalInset = Vector2D.Dot(node.Position - endpoint, direction);
                    var roadCenterAtStopLine = node.Position - (direction * longitudinalInset);
                    var roadHalfWidth = approachLanes.Length * representative.WidthMeters;
                    var right = direction.PerpendicularLeft() * -1.0;
                    var controlPosition =
                        roadCenterAtStopLine - (direction * TrafficControlLongitudinalClearanceMeters) +
                        (right * (roadHalfWidth + TrafficControlRoadEdgeClearanceMeters));
                    var controlId = $"control:{node.Id}:{approachGroup.Key}";
                    var incomingLaneIds = approachLanes.Select(lane => lane.Id).ToArray();
                    controls.Add(new TrafficControl(
                        controlId,
                        node.Id,
                        approachGroup.Key,
                        incomingLaneIds,
                        trafficLight ? TrafficControlKind.TrafficLight : TrafficControlKind.StopSign,
                        controlPosition,
                        direction,
                        trafficLight ? "Green" : "Stop"));
                    controlIds.Add(controlId);
                }
            }

            intersections.Add(new Intersection(
                node.Id,
                node.Position,
                node.Kind,
                incoming.Select(lane => lane.Id).Order(StringComparer.Ordinal).ToArray(),
                outgoing.Select(lane => lane.Id).Order(StringComparer.Ordinal).ToArray(),
                controlIds.Order(StringComparer.Ordinal).ToArray()));
        }

        return drafts.Select(draft => new Lane(
                draft.Id,
                draft.SegmentId,
                draft.StartNodeId,
                draft.EndNodeId,
                draft.CenterLine,
                draft.LeftBoundary,
                draft.RightBoundary,
                draft.Direction,
                draft.WidthMeters,
                draft.SpeedLimitMetersPerSecond,
                successorMap[draft.Id],
                stopLineByLane.GetValueOrDefault(draft.Id)))
            .OrderBy(lane => lane.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool StableControlChoice(long seed, string nodeId)
    {
        var hash = unchecked((ulong)seed);
        foreach (var character in nodeId)
        {
            hash = ((hash << 5) + hash) ^ character;
        }

        return (hash & 1UL) == 0;
    }

    private static IReadOnlyList<SpawnPoint> BuildSpawnPoints(IReadOnlyList<Lane> lanes, double blockSize)
    {
        var spawns = new List<SpawnPoint>();
        foreach (var lane in lanes)
        {
            var length = Vector2D.Distance(lane.CenterLine[0], lane.CenterLine[^1]);
            if (length < blockSize * 0.45)
            {
                continue;
            }

            var distance = Math.Min(12.0, length * 0.25);
            var amount = distance / length;
            spawns.Add(new SpawnPoint(
                $"spawn:{lane.Id}",
                lane.Id,
                Vector2D.Lerp(lane.CenterLine[0], lane.CenterLine[^1], amount),
                lane.Direction,
                distance));
        }

        return spawns.OrderBy(spawn => spawn.Id, StringComparer.Ordinal).ToArray();
    }

    private sealed record LaneDraft(
        string Id,
        string SegmentId,
        string StartNodeId,
        string EndNodeId,
        IReadOnlyList<Vector2D> CenterLine,
        IReadOnlyList<Vector2D> LeftBoundary,
        IReadOnlyList<Vector2D> RightBoundary,
        Vector2D Direction,
        double WidthMeters,
        double SpeedLimitMetersPerSecond);

    private sealed class DisjointSet(int count)
    {
        private readonly int[] _parent = Enumerable.Range(0, count).ToArray();
        private readonly byte[] _rank = new byte[count];

        public int Find(int value)
        {
            if (_parent[value] != value)
            {
                _parent[value] = Find(_parent[value]);
            }

            return _parent[value];
        }

        public bool Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB)
            {
                return false;
            }

            if (_rank[rootA] < _rank[rootB])
            {
                (rootA, rootB) = (rootB, rootA);
            }

            _parent[rootB] = rootA;
            if (_rank[rootA] == _rank[rootB])
            {
                _rank[rootA]++;
            }

            return true;
        }
    }
}

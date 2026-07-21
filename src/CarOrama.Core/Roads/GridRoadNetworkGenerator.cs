using CarOrama.Core.Geometry;
using CarOrama.Core.Random;

namespace CarOrama.Core.Roads;

public sealed class GridRoadNetworkGenerator : IRoadNetworkGenerator
{
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
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        var positions = BuildNodePositions(config);
        var incident = BuildIncidentMap(config, selected);
        var nodes = BuildNodes(config, positions, incident);

        var laneDrafts = new List<LaneDraft>(selected.Length * 2);
        var segments = new List<RoadSegment>(selected.Length);
        foreach (var edge in selected)
        {
            var segment = BuildSegment(config, edge, positions, laneDrafts);
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
        ICollection<LaneDraft> laneDrafts)
    {
        var start = positions[edge.A.NodeId];
        var end = positions[edge.B.NodeId];
        var direction = (end - start).Normalized();
        var left = direction.PerpendicularLeft();
        var halfRoadWidth = config.LaneWidthMeters;
        var junctionInset = halfRoadWidth + 1.2;
        var laneStart = start + (direction * junctionInset);
        var laneEnd = end - (direction * junctionInset);

        var abId = $"lane:{edge.Id}:ab";
        var baId = $"lane:{edge.Id}:ba";
        var abCenter = new[]
        {
            laneStart - (left * (config.LaneWidthMeters * 0.5)),
            laneEnd - (left * (config.LaneWidthMeters * 0.5)),
        };
        var baCenter = new[]
        {
            laneEnd + (left * (config.LaneWidthMeters * 0.5)),
            laneStart + (left * (config.LaneWidthMeters * 0.5)),
        };

        laneDrafts.Add(CreateLaneDraft(config, abId, edge.Id, edge.A.NodeId, edge.B.NodeId, abCenter));
        laneDrafts.Add(CreateLaneDraft(config, baId, edge.Id, edge.B.NodeId, edge.A.NodeId, baCenter));

        return new RoadSegment(
            edge.Id,
            edge.A.NodeId,
            edge.B.NodeId,
            new[] { start, end },
            config.LaneWidthMeters * 2.0,
            config.SidewalkWidthMeters,
            new[] { abId, baId });
    }

    private static LaneDraft CreateLaneDraft(
        RoadNetworkConfig config,
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
        var arterial = segmentId.Contains(":h:", StringComparison.Ordinal);
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
            arterial ? config.ArterialSpeedLimitMetersPerSecond : config.LocalSpeedLimitMetersPerSecond);
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

                var controlId = $"control:{approach.Id}";
                var controlPosition = endpoint - (approach.Direction * 1.5) - (halfWidth * 1.45);
                controls.Add(new TrafficControl(
                    controlId,
                    node.Id,
                    approach.Id,
                    trafficLight ? TrafficControlKind.TrafficLight : TrafficControlKind.StopSign,
                    controlPosition,
                    approach.Direction,
                    trafficLight ? "Green" : "Stop"));
                controlIds.Add(controlId);
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

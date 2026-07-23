using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public sealed class RoadSceneBuilder
{
    private const double IntersectionCurbReturnSetbackMeters = 2.5;
    private const double CornerSweptPathClearanceMeters = 1.2;
    private const double RoadEdgeShoulderMeters = 0.65;
    private const float MarkingElevation = 0.081f;
    private const float StopLineElevation = 0.083f;

    private readonly StandardMaterial3D _asphalt = PrimitiveFactory.Material(new Color("262a2c"));
    private readonly StandardMaterial3D _sidewalk = PrimitiveFactory.Material(new Color("92958f"), 0.96f);
    private readonly StandardMaterial3D _curb = PrimitiveFactory.Material(new Color("747976"), 0.98f);
    private readonly StandardMaterial3D _white = PrimitiveFactory.Material(new Color("f2f0d8"));
    private readonly StandardMaterial3D _yellow = PrimitiveFactory.Material(new Color("f2c94c"));
    private readonly StandardMaterial3D _grass = PrimitiveFactory.Material(new Color("567d46"));
    private readonly StandardMaterial3D _pole = PrimitiveFactory.Material(new Color("454b50"), 0.55f);
    private readonly StandardMaterial3D _signRed = PrimitiveFactory.Material(new Color("c62828"), 0.7f);
    private readonly StandardMaterial3D _signalHousing = PrimitiveFactory.Material(new Color("171b1d"), 0.5f);
    private readonly StandardMaterial3D _treeTrunk = PrimitiveFactory.Material(new Color("76553a"));
    private readonly StandardMaterial3D _treeCrown = PrimitiveFactory.Material(new Color("2f6b3c"));

    public RoadWorld Build(RoadNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var world = new RoadWorld(network);
        AddGround(world, network);

        var roads = new Node3D { Name = "RoadSurfaces" };
        var markings = new Node3D { Name = "RoadMarkings" };
        var sidewalks = new Node3D { Name = "SidewalksAndCurbs" };
        var controls = new Node3D { Name = "TrafficControls" };
        var scenery = new Node3D { Name = "RoadsideScenery" };
        world.AddChild(roads);
        world.AddChild(markings);
        world.AddChild(sidewalks);
        world.AddChild(controls);
        world.AddChild(scenery);

        foreach (var segment in network.Segments)
        {
            AddRoadSegment(network, segment, roads, markings, sidewalks, scenery);
        }

        foreach (var intersection in network.Intersections)
        {
            AddIntersection(network, intersection, roads, sidewalks);
            AddIntersectionMarkings(network, intersection, markings);
        }

        foreach (var stopLine in network.StopLines)
        {
            AddLine(markings, stopLine.Id, stopLine.LeftPoint, stopLine.RightPoint, 0.18f, StopLineElevation, _white);
        }

        foreach (var control in network.TrafficControls)
        {
            var signalHead = AddTrafficControl(network, controls, control);
            if (signalHead is not null)
            {
                world.RegisterTrafficSignalHead(signalHead);
            }
        }

        world.ActivateTrafficSignals();

        return world;
    }

    private void AddGround(Node3D world, RoadNetwork network)
    {
        var xs = network.Nodes.Select(node => node.Position.X).ToArray();
        var ys = network.Nodes.Select(node => node.Position.Y).ToArray();
        var width = (float)(xs.Max() - xs.Min() + 120.0);
        var depth = (float)(ys.Max() - ys.Min() + 120.0);
        world.AddChild(PrimitiveFactory.Box(
            "Ground",
            new Vector3(width, 0.5f, depth),
            new Transform3D(Basis.Identity, new Vector3(0.0f, -0.31f, 0.0f)),
            _grass,
            collision: true,
            collisionGroup: "drivable_surface"));
    }

    private void AddRoadSegment(
        RoadNetwork network,
        RoadSegment segment,
        Node3D roads,
        Node3D markings,
        Node3D sidewalks,
        Node3D scenery)
    {
        var direction = (segment.CenterLine[^1] - segment.CenterLine[0]).Normalized();
        var left = direction.PerpendicularLeft();
        var roadEdge = segment.WidthMeters * 0.5;
        var roadSurfaceEdge = roadEdge + RoadEdgeShoulderMeters;
        var startTrim = GetIntersectionCutoutHalfWidth(network, network.GetNode(segment.StartNodeId));
        var endTrim = GetIntersectionCutoutHalfWidth(network, network.GetNode(segment.EndNodeId));
        var trimmedStart = segment.CenterLine[0] + (direction * startTrim);
        var trimmedEnd = segment.CenterLine[^1] - (direction * endTrim);
        var trimmedLength = Vector2D.Distance(trimmedStart, trimmedEnd);
        var roadStart = ToWorld(trimmedStart);
        var roadEnd = ToWorld(trimmedEnd);
        roads.AddChild(PrimitiveFactory.Box(
            segment.Id,
            new Vector3((float)(roadSurfaceEdge * 2.0), 0.12f, (float)trimmedLength),
            PrimitiveFactory.OrientedBoxTransform(roadStart, roadEnd, 0.0f),
            _asphalt));

        var markingStartTrim = GetLongitudinalMarkingTrim(
            network,
            segment,
            network.GetNode(segment.StartNodeId),
            direction,
            startTrim);
        var markingEndTrim = GetLongitudinalMarkingTrim(
            network,
            segment,
            network.GetNode(segment.EndNodeId),
            direction * -1.0,
            endTrim);
        var markingStart = segment.CenterLine[0] + (direction * markingStartTrim);
        var markingEnd = segment.CenterLine[^1] - (direction * markingEndTrim);

        AddCenterTreatment(segment, markings, markingStart, markingEnd, left);
        AddLaneDividers(segment, markings, markingStart, markingEnd, left);
        AddLine(
            markings,
            $"edge-left:{segment.Id}",
            trimmedStart + (left * roadEdge),
            trimmedEnd + (left * roadEdge),
            0.12f,
            MarkingElevation,
            _white);
        AddLine(
            markings,
            $"edge-right:{segment.Id}",
            trimmedStart - (left * roadEdge),
            trimmedEnd - (left * roadEdge),
            0.12f,
            MarkingElevation,
            _white);

        var sidewalkOffset = roadSurfaceEdge + (segment.SidewalkWidthMeters * 0.5) + 0.22;
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var offset = left * (sidewalkOffset * side);
            var sidewalkStart = ToWorld(trimmedStart + offset);
            var sidewalkEnd = ToWorld(trimmedEnd + offset);
            sidewalks.AddChild(PrimitiveFactory.Box(
                $"sidewalk:{segment.Id}:{side}",
                new Vector3((float)segment.SidewalkWidthMeters, 0.16f, (float)trimmedLength),
                PrimitiveFactory.OrientedBoxTransform(sidewalkStart, sidewalkEnd, 0.1f),
                _sidewalk,
                collision: true));

            var curbOffset = left * ((roadSurfaceEdge + 0.12) * side);
            var curbStart = ToWorld(trimmedStart + curbOffset);
            var curbEnd = ToWorld(trimmedEnd + curbOffset);
            sidewalks.AddChild(PrimitiveFactory.Box(
                $"curb:{segment.Id}:{side}",
                new Vector3(0.22f, 0.24f, (float)trimmedLength),
                PrimitiveFactory.OrientedBoxTransform(curbStart, curbEnd, 0.12f),
                _curb,
                collision: true));
        }

        AddRoadsideTrees(segment, scenery, direction, left, roadSurfaceEdge + segment.SidewalkWidthMeters + 2.2);
    }

    private void AddCenterTreatment(
        RoadSegment segment,
        Node3D markings,
        Vector2D from,
        Vector2D to,
        Vector2D left)
    {
        if (segment.Classification == RoadClassification.Local)
        {
            AddDashedLine(markings, $"centre:{segment.Id}", from, to, 0.13f, _yellow);
            return;
        }

        const double doubleLineOffset = 0.15;
        AddLine(
            markings,
            $"centre-left:{segment.Id}",
            from + (left * doubleLineOffset),
            to + (left * doubleLineOffset),
            0.11f,
            MarkingElevation,
            _yellow);
        AddLine(
            markings,
            $"centre-right:{segment.Id}",
            from - (left * doubleLineOffset),
            to - (left * doubleLineOffset),
            0.11f,
            MarkingElevation,
            _yellow);
    }

    private void AddLaneDividers(
        RoadSegment segment,
        Node3D markings,
        Vector2D from,
        Vector2D to,
        Vector2D left)
    {
        var lanesPerDirection = segment.LanesPerDirection;
        if (lanesPerDirection < 2)
        {
            return;
        }

        var laneWidth = segment.WidthMeters / (lanesPerDirection * 2.0);
        for (var divider = 1; divider < lanesPerDirection; divider++)
        {
            var offset = laneWidth * divider;
            AddDashedLine(
                markings,
                $"lane-divider-left:{segment.Id}:{divider}",
                from + (left * offset),
                to + (left * offset),
                0.11f,
                _white);
            AddDashedLine(
                markings,
                $"lane-divider-right:{segment.Id}:{divider}",
                from - (left * offset),
                to - (left * offset),
                0.11f,
                _white);
        }
    }

    private void AddIntersection(
        RoadNetwork network,
        Intersection intersection,
        Node3D roads,
        Node3D sidewalks)
    {
        var node = network.GetNode(intersection.NodeId);
        var widestRoad = intersection.IncomingLaneIds
            .Select(network.GetLane)
            .Select(lane => network.GetSegment(lane.SegmentId).WidthMeters)
            .DefaultIfEmpty(7.2)
            .Max();

        if (intersection.Kind == IntersectionKind.Corner)
        {
            AddCornerRoadSurface(network, intersection, roads);
            AddIntersectionSidewalks(network, intersection, sidewalks, widestRoad);
            return;
        }

        var size = (float)(GetIntersectionCutoutHalfWidth(network, node) * 2.0);
        roads.AddChild(PrimitiveFactory.Box(
            $"intersection:{intersection.NodeId}",
            new Vector3(size, 0.13f, size),
            new Transform3D(Basis.Identity, ToWorld(intersection.Position)),
            _asphalt));

        if (intersection.Kind is IntersectionKind.ThreeWay or IntersectionKind.FourWay)
        {
            AddIntersectionSidewalks(network, intersection, sidewalks, widestRoad);
        }
    }

    private void AddCornerRoadSurface(
        RoadNetwork network,
        Intersection intersection,
        Node3D roads)
    {
        var node = network.GetNode(intersection.NodeId);
        var segments = node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .OrderBy(segment => segment.Id, StringComparer.Ordinal)
            .ToArray();
        if (segments.Length != 2)
        {
            return;
        }

        var cutoutHalfWidth = GetIntersectionCutoutHalfWidth(network, node);
        var centerPath = CreateCornerCurve(
            intersection.Position,
            GetOutwardDirection(network, node, segments[0]),
            GetOutwardDirection(network, node, segments[1]),
            cutoutHalfWidth);
        var firstEdge = CreateOffsetPolyline(
            centerPath,
            -((segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters),
            -((segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters),
            CornerSweptPathClearanceMeters);
        var secondEdge = CreateOffsetPolyline(
            centerPath,
            (segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters,
            (segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters,
            CornerSweptPathClearanceMeters);
        roads.AddChild(PrimitiveFactory.Ribbon(
            $"intersection-corner:{intersection.NodeId}",
            firstEdge.Select(ToWorld).ToArray(),
            secondEdge.Select(ToWorld).ToArray(),
            0.065f,
            _asphalt));
    }

    private void AddIntersectionSidewalks(
        RoadNetwork network,
        Intersection intersection,
        Node3D sidewalks,
        double roadWidth)
    {
        var node = network.GetNode(intersection.NodeId);
        var connectedDirections = GetConnectedDirections(network, node);
        var sidewalkWidth = node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .Select(segment => segment.SidewalkWidthMeters)
            .DefaultIfEmpty(2.4)
            .Max();
        var cutoutHalfWidth = GetIntersectionCutoutHalfWidth(network, node);

        if (node.Kind == IntersectionKind.Corner)
        {
            AddCornerSidewalks(network, sidewalks, intersection, node, cutoutHalfWidth);
            return;
        }

        foreach (var xSign in new[] { -1, 1 })
        {
            foreach (var ySign in new[] { -1, 1 })
            {
                var horizontalSegment = GetDirectionalSegment(network, node, horizontal: true, xSign);
                var verticalSegment = GetDirectionalSegment(network, node, horizontal: false, ySign);
                var horizontalBranch = horizontalSegment is not null;
                var verticalBranch = verticalSegment is not null;
                if (!horizontalBranch && !verticalBranch)
                {
                    continue;
                }

                if (horizontalSegment is not null && verticalSegment is not null)
                {
                    AddConnectedCorner(
                        sidewalks,
                        intersection,
                        horizontalSegment,
                        verticalSegment,
                        xSign,
                        ySign,
                        cutoutHalfWidth);
                    continue;
                }

                var segment = horizontalSegment ?? verticalSegment!;
                var sidewalkOffset = (segment.WidthMeters * 0.5) + (segment.SidewalkWidthMeters * 0.5) + 0.22;
                var endpoint = horizontalBranch
                    ? intersection.Position + new Vector2D(xSign * cutoutHalfWidth, ySign * sidewalkOffset)
                    : intersection.Position + new Vector2D(xSign * sidewalkOffset, ySign * cutoutHalfWidth);
                AddSidewalkPad(
                    sidewalks,
                    $"sidewalk-end:{intersection.NodeId}:{xSign}:{ySign}",
                    endpoint,
                    segment.SidewalkWidthMeters);
            }
        }

        AddClosedSideBridges(
            network,
            sidewalks,
            intersection,
            node,
            connectedDirections,
            roadWidth,
            sidewalkWidth,
            cutoutHalfWidth);
    }

    private void AddCornerSidewalks(
        RoadNetwork network,
        Node3D sidewalks,
        Intersection intersection,
        RoadNode node,
        double cutoutHalfWidth)
    {
        var segments = node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .OrderBy(segment => segment.Id, StringComparer.Ordinal)
            .ToArray();
        if (segments.Length != 2)
        {
            return;
        }

        var firstDirection = GetOutwardDirection(network, node, segments[0]);
        var secondDirection = GetOutwardDirection(network, node, segments[1]);
        var centerPath = CreateCornerCurve(
            intersection.Position,
            firstDirection,
            secondDirection,
            cutoutHalfWidth);

        foreach (var side in new[] { -1.0, 1.0 })
        {
            var curbPath = CreateOffsetPolyline(
                centerPath,
                side * ((segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters + 0.12),
                side * ((segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters + 0.12),
                CornerSweptPathClearanceMeters);
            var sidewalkInnerPath = CreateOffsetPolyline(
                centerPath,
                side * ((segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters + 0.22),
                side * ((segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters + 0.22),
                CornerSweptPathClearanceMeters);
            var sidewalkOuterPath = CreateOffsetPolyline(
                centerPath,
                side * ((segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters + segments[0].SidewalkWidthMeters + 0.22),
                side * ((segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters + segments[1].SidewalkWidthMeters + 0.22),
                CornerSweptPathClearanceMeters);

            sidewalks.AddChild(PrimitiveFactory.Ribbon(
                $"sidewalk-corner-curve:{intersection.NodeId}:{side}",
                sidewalkInnerPath.Select(ToWorld).ToArray(),
                sidewalkOuterPath.Select(ToWorld).ToArray(),
                0.18f,
                _sidewalk));

            for (var index = 0; index + 1 < centerPath.Count; index++)
            {
                AddCurbRun(
                    sidewalks,
                    $"curb-corner-curve:{intersection.NodeId}:{side}:{index}",
                    curbPath[index],
                    curbPath[index + 1]);
            }
        }
    }

    private void AddConnectedCorner(
        Node3D sidewalks,
        Intersection intersection,
        RoadSegment horizontalSegment,
        RoadSegment verticalSegment,
        int xSign,
        int ySign,
        double cutoutHalfWidth)
    {
        // Set the physical corner back from the conflict area. A square pad at
        // the ordinary road edge leaves enough lane width for a point mass but
        // clips a real vehicle's swept body during a legal right turn.
        var horizontalSidewalkOffset = cutoutHalfWidth +
            (horizontalSegment.SidewalkWidthMeters * 0.5) + 0.22;
        var verticalSidewalkOffset = cutoutHalfWidth +
            (verticalSegment.SidewalkWidthMeters * 0.5) + 0.22;
        var horizontalEndpoint = intersection.Position + new Vector2D(
            xSign * cutoutHalfWidth,
            ySign * horizontalSidewalkOffset);
        var outerCorner = intersection.Position + new Vector2D(
            xSign * verticalSidewalkOffset,
            ySign * horizontalSidewalkOffset);
        var verticalEndpoint = intersection.Position + new Vector2D(
            xSign * verticalSidewalkOffset,
            ySign * cutoutHalfWidth);

        AddSidewalkRun(
            sidewalks,
            $"sidewalk-corner-x:{intersection.NodeId}:{xSign}:{ySign}",
            horizontalEndpoint,
            outerCorner,
            horizontalSegment.SidewalkWidthMeters);
        AddSidewalkRun(
            sidewalks,
            $"sidewalk-corner-z:{intersection.NodeId}:{xSign}:{ySign}",
            outerCorner,
            verticalEndpoint,
            verticalSegment.SidewalkWidthMeters);
        AddSidewalkPad(
            sidewalks,
            $"sidewalk-corner-pad:{intersection.NodeId}:{xSign}:{ySign}",
            outerCorner,
            Math.Max(horizontalSegment.SidewalkWidthMeters, verticalSegment.SidewalkWidthMeters));

        var horizontalCurbEndpoint = intersection.Position + new Vector2D(
            xSign * cutoutHalfWidth,
            ySign * ((horizontalSegment.WidthMeters * 0.5) + 0.12));
        var verticalCurbEndpoint = intersection.Position + new Vector2D(
            xSign * ((verticalSegment.WidthMeters * 0.5) + 0.12),
            ySign * cutoutHalfWidth);
        var horizontalCurbCorner = intersection.Position + new Vector2D(
            xSign * cutoutHalfWidth,
            ySign * cutoutHalfWidth);
        AddCurbRun(
            sidewalks,
            $"curb-corner-x:{intersection.NodeId}:{xSign}:{ySign}",
            horizontalCurbEndpoint,
            horizontalCurbCorner);
        AddCurbRun(
            sidewalks,
            $"curb-corner-z:{intersection.NodeId}:{xSign}:{ySign}",
            verticalCurbEndpoint,
            horizontalCurbCorner);
    }

    private void AddSidewalkRun(
        Node3D sidewalks,
        string name,
        Vector2D from,
        Vector2D to,
        double width)
    {
        var start = ToWorld(from);
        var end = ToWorld(to);
        var length = start.DistanceTo(end);
        if (length < 0.02f)
        {
            return;
        }

        sidewalks.AddChild(PrimitiveFactory.Box(
            name,
            new Vector3((float)width, 0.16f, length),
            PrimitiveFactory.OrientedBoxTransform(start, end, 0.1f),
            _sidewalk,
            collision: true));
    }

    private void AddSidewalkPad(Node3D sidewalks, string name, Vector2D position, double width)
    {
        sidewalks.AddChild(PrimitiveFactory.Box(
            name,
            new Vector3((float)width, 0.16f, (float)width),
            new Transform3D(Basis.Identity, ToWorld(position) + (Vector3.Up * 0.1f)),
            _sidewalk,
            collision: true));
    }

    private void AddCurbRun(Node3D sidewalks, string name, Vector2D from, Vector2D to)
    {
        var start = ToWorld(from);
        var end = ToWorld(to);
        var length = start.DistanceTo(end);
        if (length < 0.02f)
        {
            return;
        }

        sidewalks.AddChild(PrimitiveFactory.Box(
            name,
            new Vector3(0.22f, 0.24f, length),
            PrimitiveFactory.OrientedBoxTransform(start, end, 0.12f),
            _curb,
            collision: true));
    }

    private void AddClosedSideBridges(
        RoadNetwork network,
        Node3D sidewalks,
        Intersection intersection,
        RoadNode node,
        ConnectedDirections directions,
        double fallbackRoadWidth,
        double fallbackSidewalkWidth,
        double cutoutHalfWidth)
    {
        var horizontalSegment = GetAxisSegment(network, node, horizontal: true);
        var verticalSegment = GetAxisSegment(network, node, horizontal: false);

        if (directions.North && directions.South)
        {
            var roadEdge = (verticalSegment?.WidthMeters ?? fallbackRoadWidth) * 0.5;
            var sidewalkWidth = verticalSegment?.SidewalkWidthMeters ?? fallbackSidewalkWidth;
            var sidewalkOffset = roadEdge + (sidewalkWidth * 0.5) + 0.22;
            if (!directions.East)
            {
                AddVerticalSidewalkBridge(sidewalks, intersection, 1, roadEdge, sidewalkWidth, sidewalkOffset, cutoutHalfWidth);
            }

            if (!directions.West)
            {
                AddVerticalSidewalkBridge(sidewalks, intersection, -1, roadEdge, sidewalkWidth, sidewalkOffset, cutoutHalfWidth);
            }
        }

        if (directions.East && directions.West)
        {
            var roadEdge = (horizontalSegment?.WidthMeters ?? fallbackRoadWidth) * 0.5;
            var sidewalkWidth = horizontalSegment?.SidewalkWidthMeters ?? fallbackSidewalkWidth;
            var sidewalkOffset = roadEdge + (sidewalkWidth * 0.5) + 0.22;
            if (!directions.South)
            {
                AddHorizontalSidewalkBridge(sidewalks, intersection, 1, roadEdge, sidewalkWidth, sidewalkOffset, cutoutHalfWidth);
            }

            if (!directions.North)
            {
                AddHorizontalSidewalkBridge(sidewalks, intersection, -1, roadEdge, sidewalkWidth, sidewalkOffset, cutoutHalfWidth);
            }
        }
    }

    private void AddVerticalSidewalkBridge(
        Node3D sidewalks,
        Intersection intersection,
        int xSign,
        double roadEdge,
        double sidewalkWidth,
        double sidewalkOffset,
        double cutoutHalfWidth)
    {
        var sidewalkPosition = intersection.Position + new Vector2D(xSign * sidewalkOffset, 0.0);
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"sidewalk-bridge-z:{intersection.NodeId}:{xSign}",
            new Vector3((float)sidewalkWidth, 0.16f, (float)(cutoutHalfWidth * 2.0)),
            new Transform3D(Basis.Identity, ToWorld(sidewalkPosition) + (Vector3.Up * 0.1f)),
            _sidewalk,
            collision: true));

        var curbPosition = intersection.Position + new Vector2D(xSign * (roadEdge + 0.12), 0.0);
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"curb-bridge-z:{intersection.NodeId}:{xSign}",
            new Vector3(0.22f, 0.24f, (float)(cutoutHalfWidth * 2.0)),
            new Transform3D(Basis.Identity, ToWorld(curbPosition) + (Vector3.Up * 0.12f)),
            _curb,
            collision: true));
    }

    private void AddHorizontalSidewalkBridge(
        Node3D sidewalks,
        Intersection intersection,
        int ySign,
        double roadEdge,
        double sidewalkWidth,
        double sidewalkOffset,
        double cutoutHalfWidth)
    {
        var sidewalkPosition = intersection.Position + new Vector2D(0.0, ySign * sidewalkOffset);
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"sidewalk-bridge-x:{intersection.NodeId}:{ySign}",
            new Vector3((float)(cutoutHalfWidth * 2.0), 0.16f, (float)sidewalkWidth),
            new Transform3D(Basis.Identity, ToWorld(sidewalkPosition) + (Vector3.Up * 0.1f)),
            _sidewalk,
            collision: true));

        var curbPosition = intersection.Position + new Vector2D(0.0, ySign * (roadEdge + 0.12));
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"curb-bridge-x:{intersection.NodeId}:{ySign}",
            new Vector3((float)(cutoutHalfWidth * 2.0), 0.24f, 0.22f),
            new Transform3D(Basis.Identity, ToWorld(curbPosition) + (Vector3.Up * 0.12f)),
            _curb,
            collision: true));
    }

    private static ConnectedDirections GetConnectedDirections(RoadNetwork network, RoadNode node)
    {
        var directions = new ConnectedDirections();
        foreach (var segmentId in node.ConnectedSegmentIds)
        {
            var segment = network.GetSegment(segmentId);
            var otherNodeId = segment.StartNodeId == node.Id ? segment.EndNodeId : segment.StartNodeId;
            var offset = network.GetNode(otherNodeId).Position - node.Position;
            directions.East |= offset.X > 0.0;
            directions.West |= offset.X < 0.0;
            directions.South |= offset.Y > 0.0;
            directions.North |= offset.Y < 0.0;
        }

        return directions;
    }

    private static RoadSegment? GetAxisSegment(RoadNetwork network, RoadNode node, bool horizontal)
    {
        return node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .Where(segment => IsSegmentOnAxis(network, node, segment, horizontal))
            .OrderByDescending(segment => segment.WidthMeters)
            .FirstOrDefault();
    }

    private static RoadSegment? GetDirectionalSegment(
        RoadNetwork network,
        RoadNode node,
        bool horizontal,
        int sign)
    {
        return node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .Where(segment => IsSegmentOnAxis(network, node, segment, horizontal))
            .FirstOrDefault(segment =>
            {
                var otherNodeId = segment.StartNodeId == node.Id ? segment.EndNodeId : segment.StartNodeId;
                var offset = network.GetNode(otherNodeId).Position - node.Position;
                var coordinate = horizontal ? offset.X : offset.Y;
                return Math.Sign(coordinate) == sign;
            });
    }

    private static bool IsSegmentOnAxis(
        RoadNetwork network,
        RoadNode node,
        RoadSegment segment,
        bool horizontal)
    {
        var otherNodeId = segment.StartNodeId == node.Id ? segment.EndNodeId : segment.StartNodeId;
        var offset = network.GetNode(otherNodeId).Position - node.Position;
        return horizontal
            ? Math.Abs(offset.X) >= Math.Abs(offset.Y)
            : Math.Abs(offset.Y) > Math.Abs(offset.X);
    }

    private void AddIntersectionMarkings(
        RoadNetwork network,
        Intersection intersection,
        Node3D markings)
    {
        if (intersection.Kind == IntersectionKind.Corner)
        {
            AddCornerMarkings(network, intersection, markings);
            return;
        }

        if (intersection.TrafficControlIds.Count == 0)
        {
            return;
        }

        var node = network.GetNode(intersection.NodeId);
        var cutoutHalfWidth = GetIntersectionCutoutHalfWidth(network, node);
        var approachGroups = intersection.IncomingLaneIds
            .Select(network.GetLane)
            .GroupBy(lane => lane.SegmentId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var approach in approachGroups)
        {
            var lanes = approach.ToArray();
            if (lanes.Length == 0)
            {
                continue;
            }

            var segment = network.GetSegment(approach.Key);
            AddCrosswalk(markings, intersection, segment, lanes[0].Direction, cutoutHalfWidth);
            AddDirectionalLaneArrows(markings, intersection, lanes, cutoutHalfWidth);
        }
    }

    private void AddCornerMarkings(
        RoadNetwork network,
        Intersection intersection,
        Node3D markings)
    {
        var node = network.GetNode(intersection.NodeId);
        var segments = node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .OrderBy(segment => segment.Id, StringComparer.Ordinal)
            .ToArray();
        if (segments.Length != 2)
        {
            return;
        }

        var cutoutHalfWidth = GetIntersectionCutoutHalfWidth(network, node);
        var directions = segments
            .Select(segment => GetOutwardDirection(network, node, segment))
            .ToArray();
        var centerPath = CreateCornerCurve(
            node.Position,
            directions[0],
            directions[1],
            cutoutHalfWidth);
        var arterialTreatment = segments.Any(segment => segment.Classification == RoadClassification.Arterial);
        if (!arterialTreatment)
        {
            AddDashedPolylineLine(
                markings,
                $"corner-centre:{intersection.NodeId}",
                centerPath,
                0.13f,
                _yellow);
        }
        else
        {
            AddOffsetPolylineLine(
                markings,
                $"corner-centre-left:{intersection.NodeId}",
                centerPath,
                0.15,
                0.15,
                0.11f,
                _yellow);
            AddOffsetPolylineLine(
                markings,
                $"corner-centre-right:{intersection.NodeId}",
                centerPath,
                -0.15,
                -0.15,
                0.11f,
                _yellow);
        }

        AddOffsetPolylineLine(
            markings,
            $"corner-edge-left:{intersection.NodeId}",
            centerPath,
            (segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters,
            (segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters,
            0.12f,
            _white);
        AddOffsetPolylineLine(
            markings,
            $"corner-edge-right:{intersection.NodeId}",
            centerPath,
            -((segments[0].WidthMeters * 0.5) + RoadEdgeShoulderMeters),
            -((segments[1].WidthMeters * 0.5) + RoadEdgeShoulderMeters),
            0.12f,
            _white);

        var sameLaneTreatment = segments[0].LanesPerDirection == segments[1].LanesPerDirection &&
            Math.Abs(segments[0].WidthMeters - segments[1].WidthMeters) < 0.001;
        if (!sameLaneTreatment || segments[0].LanesPerDirection < 2)
        {
            return;
        }

        var laneWidth = segments[0].WidthMeters / (segments[0].LanesPerDirection * 2.0);
        for (var divider = 1; divider < segments[0].LanesPerDirection; divider++)
        {
            var offset = laneWidth * divider;
            AddOffsetPolylineLine(
                markings,
                $"corner-lane-divider-left:{intersection.NodeId}:{divider}",
                centerPath,
                offset,
                offset,
                0.11f,
                _white,
                dashed: true);
            AddOffsetPolylineLine(
                markings,
                $"corner-lane-divider-right:{intersection.NodeId}:{divider}",
                centerPath,
                -offset,
                -offset,
                0.11f,
                _white,
                dashed: true);
        }
    }

    private static Vector2D GetOutwardDirection(
        RoadNetwork network,
        RoadNode node,
        RoadSegment segment)
    {
        var otherNodeId = segment.StartNodeId == node.Id ? segment.EndNodeId : segment.StartNodeId;
        return (network.GetNode(otherNodeId).Position - node.Position).Normalized();
    }

    private static IReadOnlyList<Vector2D> CreateCornerCurve(
        Vector2D corner,
        Vector2D firstDirection,
        Vector2D secondDirection,
        double cutoutHalfWidth)
    {
        const int samples = 24;
        var arcCenter = corner + ((firstDirection + secondDirection) * cutoutHalfWidth);
        var points = new Vector2D[samples + 1];
        for (var index = 0; index <= samples; index++)
        {
            var amount = index / (double)samples;
            var angle = amount * Math.PI * 0.5;
            points[index] = arcCenter -
                (secondDirection * (Math.Cos(angle) * cutoutHalfWidth)) -
                (firstDirection * (Math.Sin(angle) * cutoutHalfWidth));
        }

        return points;
    }

    private void AddOffsetPolylineLine(
        Node3D markings,
        string name,
        IReadOnlyList<Vector2D> centerPath,
        double startOffset,
        double endOffset,
        float width,
        Material material,
        bool dashed = false)
    {
        var points = CreateOffsetPolyline(centerPath, startOffset, endOffset);

        if (dashed)
        {
            AddDashedPolylineLine(markings, name, points, width, material);
        }
        else
        {
            AddPolylineLine(markings, name, points, width, material);
        }
    }

    private static IReadOnlyList<Vector2D> CreateOffsetPolyline(
        IReadOnlyList<Vector2D> centerPath,
        double startOffset,
        double endOffset,
        double midpointExpansionMeters = 0.0)
    {
        var points = new Vector2D[centerPath.Count];
        for (var index = 0; index < centerPath.Count; index++)
        {
            Vector2D tangent;
            if (index == 0)
            {
                tangent =
                    (centerPath[1] * 4.0) -
                    (centerPath[0] * 3.0) -
                    centerPath[2];
            }
            else if (index == centerPath.Count - 1)
            {
                tangent =
                    (centerPath[index] * 3.0) -
                    (centerPath[index - 1] * 4.0) +
                    centerPath[index - 2];
            }
            else
            {
                tangent = centerPath[index + 1] - centerPath[index - 1];
            }

            var left = tangent.Normalized().PerpendicularLeft();
            var amount = index / (double)(centerPath.Count - 1);
            var offset = startOffset + ((endOffset - startOffset) * amount);
            var offsetDirection = Math.Sign(offset);
            offset += offsetDirection * midpointExpansionMeters * Math.Sin(Math.PI * amount);
            points[index] = centerPath[index] + (left * offset);
        }

        return points;
    }

    private void AddCrosswalk(
        Node3D markings,
        Intersection intersection,
        RoadSegment segment,
        Vector2D approachDirection,
        double cutoutHalfWidth)
    {
        const double stripeWidth = 0.52;
        const double stripeGap = 0.45;
        const double crossingDepth = 3.25;
        const double boundaryClearance = 0.14;

        var direction = approachDirection.Normalized();
        var left = direction.PerpendicularLeft();
        var distanceFromNode = cutoutHalfWidth + (crossingDepth * 0.5) + boundaryClearance;
        var crossingCenter = intersection.Position - (direction * distanceFromNode);
        var halfSpan = Math.Max(0.5, (segment.WidthMeters * 0.5) - 0.22);
        var usableWidth = halfSpan * 2.0;
        var stripePitch = stripeWidth + stripeGap;
        var stripeCount = Math.Max(3, (int)Math.Floor((usableWidth + stripeGap) / stripePitch));
        var patternWidth = ((stripeCount - 1) * stripePitch) + stripeWidth;
        var firstOffset = (-patternWidth * 0.5) + (stripeWidth * 0.5);

        for (var stripe = 0; stripe < stripeCount; stripe++)
        {
            var lateralOffset = firstOffset + (stripe * stripePitch);
            var stripeCenter = crossingCenter + (left * lateralOffset);
            AddLine(
                markings,
                $"crosswalk:{intersection.NodeId}:{segment.Id}:{stripe}",
                stripeCenter - (direction * (crossingDepth * 0.5)),
                stripeCenter + (direction * (crossingDepth * 0.5)),
                (float)stripeWidth,
                MarkingElevation,
                _white);
        }
    }

    private void AddDirectionalLaneArrows(
        Node3D markings,
        Intersection intersection,
        IReadOnlyList<Lane> approachLanes,
        double cutoutHalfWidth)
    {
        var ordered = approachLanes
            .OrderBy(lane => GetLateralDistanceFromRoadCenter(lane, intersection.Position))
            .ThenBy(lane => lane.Id, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var lane = ordered[index];
            var direction = lane.Direction.Normalized();
            var endpointOffset = lane.CenterLine[^1] - intersection.Position;
            var longitudinal = direction * Vector2D.Dot(endpointOffset, direction);
            var lateral = endpointOffset - longitudinal;
            var center = intersection.Position - (direction * (cutoutHalfWidth + 7.5)) + lateral;
            var name = $"lane-arrow:{intersection.NodeId}:{lane.Id}";

            if (ordered.Length == 1)
            {
                // An unassigned single lane permits every movement, so no
                // pavement arrow should reserve it for a particular turn.
                continue;
            }
            else if (ordered.Length == 2)
            {
                AddStraightArrow(markings, $"{name}:straight", center, direction);
                AddTurnBranch(markings, $"{name}:turn", center, direction, index == 0 ? 1.0 : -1.0);
            }
            else if (index == 0)
            {
                AddTurnArrow(markings, name, center, direction, 1.0);
            }
            else if (index == ordered.Length - 1)
            {
                AddTurnArrow(markings, name, center, direction, -1.0);
            }
            else
            {
                AddStraightArrow(markings, name, center, direction);
            }
        }
    }

    private static double GetLateralDistanceFromRoadCenter(Lane lane, Vector2D nodePosition)
    {
        var offset = lane.CenterLine[^1] - nodePosition;
        return Math.Abs(Vector2D.Dot(offset, lane.Direction.PerpendicularLeft()));
    }

    private void AddStraightArrow(
        Node3D markings,
        string name,
        Vector2D center,
        Vector2D direction)
    {
        var left = direction.PerpendicularLeft();
        var tip = center + (direction * 1.55);
        var shoulder = center + (direction * 0.78);
        AddLine(markings, $"{name}:shaft", center - (direction * 1.35), tip, 0.21f, MarkingElevation, _white);
        AddLine(markings, $"{name}:head-left", tip, shoulder + (left * 0.62), 0.21f, MarkingElevation, _white);
        AddLine(markings, $"{name}:head-right", tip, shoulder - (left * 0.62), 0.21f, MarkingElevation, _white);
    }

    private void AddTurnArrow(
        Node3D markings,
        string name,
        Vector2D center,
        Vector2D direction,
        double turnSign)
    {
        var elbow = center + (direction * 0.38);
        AddLine(markings, $"{name}:shaft", center - (direction * 1.35), elbow, 0.21f, MarkingElevation, _white);
        AddTurnArrowHead(markings, name, elbow, direction, turnSign);
    }

    private void AddTurnBranch(
        Node3D markings,
        string name,
        Vector2D center,
        Vector2D direction,
        double turnSign)
    {
        var elbow = center + (direction * 0.18);
        AddTurnArrowHead(markings, name, elbow, direction, turnSign);
    }

    private void AddTurnArrowHead(
        Node3D markings,
        string name,
        Vector2D elbow,
        Vector2D direction,
        double turnSign)
    {
        var turnDirection = direction.PerpendicularLeft() * turnSign;
        var tip = elbow + (turnDirection * 1.18);
        var shoulder = tip - (turnDirection * 0.62);
        AddLine(markings, $"{name}:arm", elbow, tip, 0.21f, MarkingElevation, _white);
        AddLine(markings, $"{name}:head-forward", tip, shoulder + (direction * 0.46), 0.21f, MarkingElevation, _white);
        AddLine(markings, $"{name}:head-back", tip, shoulder - (direction * 0.46), 0.21f, MarkingElevation, _white);
    }

    private static bool RequiresIntersectionCutout(RoadNode node) =>
        node.Kind is IntersectionKind.Corner or IntersectionKind.ThreeWay or IntersectionKind.FourWay;

    private static double GetIntersectionCutoutHalfWidth(RoadNetwork network, RoadNode node)
    {
        if (!RequiresIntersectionCutout(node))
        {
            return 0.0;
        }

        if (node.Kind == IntersectionKind.Corner)
        {
            const double innerVergeRadius = 1.0;
            return node.ConnectedSegmentIds
                .Select(network.GetSegment)
                .Select(segment =>
                    (segment.WidthMeters * 0.5) +
                    segment.SidewalkWidthMeters +
                    0.22 +
                    innerVergeRadius)
                .DefaultIfEmpty(0.0)
                .Max();
        }

        return node.ConnectedSegmentIds
            .Select(network.GetSegment)
            .Select(segment => segment.WidthMeters * 0.5)
            .DefaultIfEmpty(0.0)
            .Max() + IntersectionCurbReturnSetbackMeters;
    }

    private static double GetLongitudinalMarkingTrim(
        RoadNetwork network,
        RoadSegment segment,
        RoadNode node,
        Vector2D awayFromNode,
        double intersectionTrim)
    {
        return segment.LaneIds
            .Select(network.GetLane)
            .Where(lane => lane.EndNodeId == node.Id && lane.StopLineId is not null)
            .Select(lane => Vector2D.Dot(lane.CenterLine[^1] - node.Position, awayFromNode))
            .DefaultIfEmpty(intersectionTrim)
            .Max();
    }

    private sealed class ConnectedDirections
    {
        public bool North { get; set; }

        public bool South { get; set; }

        public bool East { get; set; }

        public bool West { get; set; }
    }

    private static void AddPolylineLine(
        Node3D parent,
        string name,
        IReadOnlyList<Vector2D> points,
        float width,
        Material material)
    {
        for (var index = 0; index + 1 < points.Count; index++)
        {
            AddLine(
                parent,
                $"{name}:{index}",
                points[index],
                points[index + 1],
                width,
                MarkingElevation,
                material);
        }
    }

    private static void AddDashedPolylineLine(
        Node3D parent,
        string name,
        IReadOnlyList<Vector2D> points,
        float width,
        Material material)
    {
        var totalLength = 0.0;
        for (var index = 0; index + 1 < points.Count; index++)
        {
            totalLength += Vector2D.Distance(points[index], points[index + 1]);
        }

        if (totalLength < 1.5)
        {
            return;
        }

        const double dash = 3.2;
        const double gap = 4.2;
        const double endMargin = 0.6;
        var dashIndex = 0;
        for (var offset = endMargin; offset < totalLength - endMargin; offset += dash + gap)
        {
            var dashEnd = Math.Min(offset + dash, totalLength - endMargin);
            if (dashEnd - offset < 0.8)
            {
                break;
            }

            AddPolylineSubLine(parent, $"{name}:{dashIndex++}", points, offset, dashEnd, width, material);
        }
    }

    private static void AddPolylineSubLine(
        Node3D parent,
        string name,
        IReadOnlyList<Vector2D> points,
        double fromDistance,
        double toDistance,
        float width,
        Material material)
    {
        var traversed = 0.0;
        var segmentIndex = 0;
        for (var index = 0; index + 1 < points.Count; index++)
        {
            var segmentLength = Vector2D.Distance(points[index], points[index + 1]);
            var segmentStart = Math.Max(fromDistance, traversed);
            var segmentEnd = Math.Min(toDistance, traversed + segmentLength);
            if (segmentEnd - segmentStart >= 0.02)
            {
                var localStart = (segmentStart - traversed) / segmentLength;
                var localEnd = (segmentEnd - traversed) / segmentLength;
                AddLine(
                    parent,
                    $"{name}:{segmentIndex++}",
                    Vector2D.Lerp(points[index], points[index + 1], localStart),
                    Vector2D.Lerp(points[index], points[index + 1], localEnd),
                    width,
                    MarkingElevation,
                    material);
            }

            traversed += segmentLength;
            if (traversed >= toDistance)
            {
                break;
            }
        }
    }

    private void AddDashedLine(
        Node3D parent,
        string name,
        Vector2D from,
        Vector2D to,
        float width,
        Material material)
    {
        var delta = to - from;
        var length = delta.Length;
        if (length < 1.5)
        {
            return;
        }

        var direction = delta.Normalized();
        const double dash = 3.2;
        const double gap = 4.2;
        const double endMargin = 0.6;
        var index = 0;
        for (var offset = endMargin; offset < length - endMargin; offset += dash + gap)
        {
            var dashEnd = Math.Min(offset + dash, length - endMargin);
            if (dashEnd - offset < 0.8)
            {
                break;
            }

            AddLine(
                parent,
                $"{name}:{index++}",
                from + (direction * offset),
                from + (direction * dashEnd),
                width,
                MarkingElevation,
                material);
        }
    }

    private static void AddLine(
        Node3D parent,
        string name,
        Vector2D from,
        Vector2D to,
        float width,
        float elevation,
        Material material)
    {
        var start = ToWorld(from);
        var end = ToWorld(to);
        parent.AddChild(PrimitiveFactory.Box(
            name,
            new Vector3(width, 0.025f, start.DistanceTo(end)),
            PrimitiveFactory.OrientedBoxTransform(start, end, elevation),
            material,
            castShadow: false));
    }

    private TrafficSignalHead? AddTrafficControl(
        RoadNetwork network,
        Node3D parent,
        TrafficControl control)
    {
        if (control.Kind == TrafficControlKind.StopSign)
        {
            var root = new Node3D
            {
                Name = control.Id,
                Position = ToWorld(control.Position),
                Rotation = new Vector3(
                    0.0f,
                    Mathf.Atan2((float)control.FacingDirection.X, (float)control.FacingDirection.Y),
                    0.0f),
            };
            parent.AddChild(root);
            AddStopSign(root);
            return null;
        }

        return AddOverheadTrafficSignal(network, parent, control);
    }

    private TrafficSignalHead AddOverheadTrafficSignal(
        RoadNetwork network,
        Node3D parent,
        TrafficControl control)
    {
        const double poleRoadClearance = 0.9;
        const double poleIntersectionClearance = 0.8;
        const float mastHeight = 5.35f;

        var node = network.GetNode(control.IntersectionNodeId);
        var approachSegment = network.GetSegment(control.ApproachSegmentId);
        var approachDirection = control.FacingDirection.Normalized();
        var left = approachDirection.PerpendicularLeft();
        var right = left * -1.0;
        var roadHalfWidth = approachSegment.WidthMeters * 0.5;
        var poleLateralOffset = roadHalfWidth + poleRoadClearance;
        var farSideOffset = GetIntersectionCutoutHalfWidth(network, node) + poleIntersectionClearance;
        var polePosition = node.Position +
            (approachDirection * farSideOffset) +
            (right * poleLateralOffset);
        var signalRoot = new TrafficSignalHead(control.Id)
        {
            Position = ToWorld(polePosition),
            Rotation = new Vector3(
                0.0f,
                Mathf.Atan2((float)approachDirection.X, (float)approachDirection.Y),
                0.0f),
        };
        parent.AddChild(signalRoot);

        var pole = PrimitiveFactory.Cylinder("MastPole", 0.11f, mastHeight, _pole, 16);
        pole.Position = new Vector3(0.0f, mastHeight * 0.5f, 0.0f);
        signalRoot.AddChild(pole);

        var mastLength = (float)(approachSegment.WidthMeters + (poleRoadClearance * 2.0));
        signalRoot.AddChild(PrimitiveFactory.Box(
            "MastArm",
            new Vector3(mastLength, 0.18f, 0.18f),
            new Transform3D(
                Basis.Identity,
                new Vector3(mastLength * 0.5f, mastHeight, 0.0f)),
            _pole));

        var incomingLanes = control.IncomingLaneIds
            .Select(network.GetLane)
            .OrderBy(lane => Vector2D.Dot(lane.CenterLine[^1] - node.Position, left))
            .ToArray();
        for (var index = 0; index < incomingLanes.Length; index++)
        {
            var lane = incomingLanes[index];
            var laneLateralOffset = Vector2D.Dot(lane.CenterLine[^1] - node.Position, left);
            var localX = (float)(poleLateralOffset + laneLateralOffset);
            AddOverheadSignalHead(signalRoot, index, localX, mastHeight);
        }

        return signalRoot;
    }

    private void AddOverheadSignalHead(
        TrafficSignalHead signalRoot,
        int index,
        float localX,
        float mastHeight)
    {
        const float housingCenterHeight = 4.73f;

        signalRoot.AddChild(PrimitiveFactory.Box(
            $"Hanger:{index}",
            new Vector3(0.08f, mastHeight - housingCenterHeight, 0.08f),
            new Transform3D(
                Basis.Identity,
                new Vector3(localX, (mastHeight + housingCenterHeight) * 0.5f, 0.0f)),
            _pole));

        var housing = PrimitiveFactory.Box(
            $"SignalHousing:{index}",
            new Vector3(0.46f, 1.08f, 0.32f),
            new Transform3D(Basis.Identity, new Vector3(localX, housingCenterHeight, 0.0f)),
            _signalHousing);
        signalRoot.AddChild(housing);
        var redLamp = AddSignalLamp(housing, "Red", 0.32f);
        var yellowLamp = AddSignalLamp(housing, "Yellow", 0.0f);
        var greenLamp = AddSignalLamp(housing, "Green", -0.32f);
        signalRoot.AddLamps(redLamp, yellowLamp, greenLamp);
    }

    private void AddStopSign(Node3D root)
    {
        const float signCenterHeight = 2.08f;
        const float poleHeight = 1.68f;

        var pole = PrimitiveFactory.Cylinder("Pole", 0.075f, poleHeight, _pole, 12);
        pole.Position = new Vector3(0.0f, poleHeight * 0.5f, 0.035f);
        root.AddChild(pole);

        var border = PrimitiveFactory.Cylinder("WhiteBorder", 0.47f, 0.065f, _white, 8);
        border.Position = new Vector3(0.0f, signCenterHeight, 0.0f);
        border.RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f);
        root.AddChild(border);

        var face = PrimitiveFactory.Cylinder("RedFace", 0.415f, 0.04f, _signRed, 8);
        face.Position = new Vector3(0.0f, signCenterHeight, -0.045f);
        face.RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f);
        root.AddChild(face);

        var label = new Label3D
        {
            Name = "StopText",
            Text = "STOP",
            FontSize = 64,
            PixelSize = 0.0045f,
            Modulate = Colors.White,
            OutlineSize = 0,
            DoubleSided = true,
            Position = new Vector3(0.0f, signCenterHeight, -0.075f),
            RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f),
        };
        root.AddChild(label);
    }

    private static MeshInstance3D AddSignalLamp(Node3D housing, string name, float y)
    {
        var lamp = new MeshInstance3D
        {
            Name = name,
            Mesh = new SphereMesh { Radius = 0.105f, Height = 0.21f },
            Position = new Vector3(0.0f, y, -0.17f),
        };
        housing.AddChild(lamp);
        return lamp;
    }

    private void AddRoadsideTrees(
        RoadSegment segment,
        Node3D scenery,
        Vector2D direction,
        Vector2D left,
        double offset)
    {
        var length = Vector2D.Distance(segment.CenterLine[0], segment.CenterLine[^1]);
        var treeIndex = 0;
        for (var distance = 20.0; distance < length - 16.0; distance += 28.0)
        {
            var side = ((StableHash(segment.Id) + treeIndex) & 1) == 0 ? 1.0 : -1.0;
            var point = segment.CenterLine[0] + (direction * distance) + (left * offset * side);
            AddTree(scenery, $"tree:{segment.Id}:{treeIndex}", ToWorld(point));
            treeIndex++;
        }
    }

    private void AddTree(Node3D parent, string name, Vector3 position)
    {
        var root = new Node3D { Name = name, Position = position };
        var trunk = PrimitiveFactory.Cylinder("Trunk", 0.22f, 2.0f, _treeTrunk, 10);
        trunk.Position = new Vector3(0.0f, 1.0f, 0.0f);
        root.AddChild(trunk);
        var crown = new MeshInstance3D
        {
            Name = "Crown",
            Mesh = new SphereMesh { Radius = 1.25f, Height = 2.5f },
            MaterialOverride = _treeCrown,
            Position = new Vector3(0.0f, 2.7f, 0.0f),
        };
        root.AddChild(crown);
        parent.AddChild(root);
    }

    private static int StableHash(string value)
    {
        var hash = 17;
        foreach (var character in value)
        {
            hash = unchecked((hash * 31) + character);
        }

        return hash;
    }

    private static Vector3 ToWorld(Vector2D point) => new((float)point.X, 0.0f, (float)point.Y);
}

using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public sealed class RoadSceneBuilder
{
    private readonly StandardMaterial3D _asphalt = PrimitiveFactory.Material(new Color("262a2c"));
    private readonly StandardMaterial3D _sidewalk = PrimitiveFactory.Material(new Color("a9aa9f"));
    private readonly StandardMaterial3D _curb = PrimitiveFactory.Material(new Color("d1d0c4"));
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
        }

        foreach (var stopLine in network.StopLines)
        {
            AddLine(markings, stopLine.Id, stopLine.LeftPoint, stopLine.RightPoint, 0.18f, 0.025f, _white);
        }

        foreach (var control in network.TrafficControls)
        {
            AddTrafficControl(controls, control);
        }

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
            collision: true));
    }

    private void AddRoadSegment(
        RoadNetwork network,
        RoadSegment segment,
        Node3D roads,
        Node3D markings,
        Node3D sidewalks,
        Node3D scenery)
    {
        var start = ToWorld(segment.CenterLine[0]);
        var end = ToWorld(segment.CenterLine[^1]);
        var length = start.DistanceTo(end);
        var transform = PrimitiveFactory.OrientedBoxTransform(start, end, 0.0f);
        roads.AddChild(PrimitiveFactory.Box(
            segment.Id,
            new Vector3((float)segment.WidthMeters, 0.12f, length),
            transform,
            _asphalt));

        AddDashedLine(markings, $"centre:{segment.Id}", segment.CenterLine[0], segment.CenterLine[^1], _yellow);

        var direction = (segment.CenterLine[^1] - segment.CenterLine[0]).Normalized();
        var left = direction.PerpendicularLeft();
        var roadEdge = segment.WidthMeters * 0.5;
        var startTrim = RequiresIntersectionCutout(network.GetNode(segment.StartNodeId)) ? roadEdge : 0.0;
        var endTrim = RequiresIntersectionCutout(network.GetNode(segment.EndNodeId)) ? roadEdge : 0.0;
        var trimmedStart = segment.CenterLine[0] + (direction * startTrim);
        var trimmedEnd = segment.CenterLine[^1] - (direction * endTrim);
        var trimmedLength = Vector2D.Distance(trimmedStart, trimmedEnd);
        AddLine(markings, $"edge-left:{segment.Id}", trimmedStart + (left * roadEdge), trimmedEnd + (left * roadEdge), 0.12f, 0.018f, _white);
        AddLine(markings, $"edge-right:{segment.Id}", trimmedStart - (left * roadEdge), trimmedEnd - (left * roadEdge), 0.12f, 0.018f, _white);

        var sidewalkOffset = roadEdge + (segment.SidewalkWidthMeters * 0.5) + 0.22;
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

            var curbOffset = left * ((roadEdge + 0.12) * side);
            var curbStart = ToWorld(trimmedStart + curbOffset);
            var curbEnd = ToWorld(trimmedEnd + curbOffset);
            sidewalks.AddChild(PrimitiveFactory.Box(
                $"curb:{segment.Id}:{side}",
                new Vector3(0.22f, 0.24f, (float)trimmedLength),
                PrimitiveFactory.OrientedBoxTransform(curbStart, curbEnd, 0.12f),
                _curb,
                collision: true));
        }

        AddRoadsideTrees(segment, scenery, direction, left, roadEdge + segment.SidewalkWidthMeters + 2.2);
    }

    private void AddIntersection(
        RoadNetwork network,
        Intersection intersection,
        Node3D roads,
        Node3D sidewalks)
    {
        var widestRoad = intersection.IncomingLaneIds
            .Select(network.GetLane)
            .Select(lane => network.GetSegment(lane.SegmentId).WidthMeters)
            .DefaultIfEmpty(7.2)
            .Max();
        var size = (float)(widestRoad + 0.08);
        roads.AddChild(PrimitiveFactory.Box(
            $"intersection:{intersection.NodeId}",
            new Vector3(size, 0.13f, size),
            new Transform3D(Basis.Identity, ToWorld(intersection.Position)),
            _asphalt));

        if (intersection.Kind is IntersectionKind.Corner or IntersectionKind.ThreeWay or IntersectionKind.FourWay)
        {
            AddIntersectionSidewalks(network, intersection, sidewalks, widestRoad);
        }
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
        var roadEdge = roadWidth * 0.5;
        var sidewalkOffset = roadEdge + (sidewalkWidth * 0.5) + 0.22;

        foreach (var xSign in new[] { -1, 1 })
        {
            foreach (var ySign in new[] { -1, 1 })
            {
                var horizontalBranch = xSign > 0 ? connectedDirections.East : connectedDirections.West;
                var verticalBranch = ySign > 0 ? connectedDirections.South : connectedDirections.North;
                if (!horizontalBranch && !verticalBranch)
                {
                    continue;
                }

                var padPosition = intersection.Position + new Vector2D(
                    xSign * sidewalkOffset,
                    ySign * sidewalkOffset);
                sidewalks.AddChild(PrimitiveFactory.Box(
                    $"sidewalk-corner:{intersection.NodeId}:{xSign}:{ySign}",
                    new Vector3((float)sidewalkWidth, 0.16f, (float)sidewalkWidth),
                    new Transform3D(Basis.Identity, ToWorld(padPosition) + (Vector3.Up * 0.1f)),
                    _sidewalk,
                    collision: true));

                if (horizontalBranch)
                {
                    var curbPosition = intersection.Position + new Vector2D(
                        xSign * sidewalkOffset,
                        ySign * (roadEdge + 0.12));
                    sidewalks.AddChild(PrimitiveFactory.Box(
                        $"curb-corner-x:{intersection.NodeId}:{xSign}:{ySign}",
                        new Vector3((float)sidewalkWidth, 0.24f, 0.22f),
                        new Transform3D(Basis.Identity, ToWorld(curbPosition) + (Vector3.Up * 0.12f)),
                        _curb,
                        collision: true));
                }

                if (verticalBranch)
                {
                    var curbPosition = intersection.Position + new Vector2D(
                        xSign * (roadEdge + 0.12),
                        ySign * sidewalkOffset);
                    sidewalks.AddChild(PrimitiveFactory.Box(
                        $"curb-corner-z:{intersection.NodeId}:{xSign}:{ySign}",
                        new Vector3(0.22f, 0.24f, (float)sidewalkWidth),
                        new Transform3D(Basis.Identity, ToWorld(curbPosition) + (Vector3.Up * 0.12f)),
                        _curb,
                        collision: true));
                }
            }
        }

        AddClosedSideBridges(sidewalks, intersection, connectedDirections, roadEdge, sidewalkWidth, sidewalkOffset);
    }

    private void AddClosedSideBridges(
        Node3D sidewalks,
        Intersection intersection,
        ConnectedDirections directions,
        double roadEdge,
        double sidewalkWidth,
        double sidewalkOffset)
    {
        if (directions.North && directions.South)
        {
            if (!directions.East)
            {
                AddVerticalSidewalkBridge(sidewalks, intersection, 1, roadEdge, sidewalkWidth, sidewalkOffset);
            }

            if (!directions.West)
            {
                AddVerticalSidewalkBridge(sidewalks, intersection, -1, roadEdge, sidewalkWidth, sidewalkOffset);
            }
        }

        if (directions.East && directions.West)
        {
            if (!directions.South)
            {
                AddHorizontalSidewalkBridge(sidewalks, intersection, 1, roadEdge, sidewalkWidth, sidewalkOffset);
            }

            if (!directions.North)
            {
                AddHorizontalSidewalkBridge(sidewalks, intersection, -1, roadEdge, sidewalkWidth, sidewalkOffset);
            }
        }
    }

    private void AddVerticalSidewalkBridge(
        Node3D sidewalks,
        Intersection intersection,
        int xSign,
        double roadEdge,
        double sidewalkWidth,
        double sidewalkOffset)
    {
        var sidewalkPosition = intersection.Position + new Vector2D(xSign * sidewalkOffset, 0.0);
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"sidewalk-bridge-z:{intersection.NodeId}:{xSign}",
            new Vector3((float)sidewalkWidth, 0.16f, (float)(roadEdge * 2.0)),
            new Transform3D(Basis.Identity, ToWorld(sidewalkPosition) + (Vector3.Up * 0.1f)),
            _sidewalk,
            collision: true));

        var curbPosition = intersection.Position + new Vector2D(xSign * (roadEdge + 0.12), 0.0);
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"curb-bridge-z:{intersection.NodeId}:{xSign}",
            new Vector3(0.22f, 0.24f, (float)(roadEdge * 2.0)),
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
        double sidewalkOffset)
    {
        var sidewalkPosition = intersection.Position + new Vector2D(0.0, ySign * sidewalkOffset);
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"sidewalk-bridge-x:{intersection.NodeId}:{ySign}",
            new Vector3((float)(roadEdge * 2.0), 0.16f, (float)sidewalkWidth),
            new Transform3D(Basis.Identity, ToWorld(sidewalkPosition) + (Vector3.Up * 0.1f)),
            _sidewalk,
            collision: true));

        var curbPosition = intersection.Position + new Vector2D(0.0, ySign * (roadEdge + 0.12));
        sidewalks.AddChild(PrimitiveFactory.Box(
            $"curb-bridge-x:{intersection.NodeId}:{ySign}",
            new Vector3((float)(roadEdge * 2.0), 0.24f, 0.22f),
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

    private static bool RequiresIntersectionCutout(RoadNode node) =>
        node.Kind is IntersectionKind.Corner or IntersectionKind.ThreeWay or IntersectionKind.FourWay;

    private sealed class ConnectedDirections
    {
        public bool North { get; set; }

        public bool South { get; set; }

        public bool East { get; set; }

        public bool West { get; set; }
    }

    private void AddDashedLine(Node3D parent, string name, Vector2D from, Vector2D to, Material material)
    {
        var delta = to - from;
        var length = delta.Length;
        var direction = delta.Normalized();
        const double dash = 3.2;
        const double gap = 4.2;
        var index = 0;
        for (var offset = 7.0; offset + dash < length - 7.0; offset += dash + gap)
        {
            AddLine(
                parent,
                $"{name}:{index++}",
                from + (direction * offset),
                from + (direction * (offset + dash)),
                0.13f,
                0.022f,
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
            material));
    }

    private void AddTrafficControl(Node3D parent, TrafficControl control)
    {
        var root = new Node3D
        {
            Name = control.Id,
            Position = ToWorld(control.Position),
            Rotation = new Vector3(0.0f, Mathf.Atan2((float)control.FacingDirection.X, (float)control.FacingDirection.Y), 0.0f),
        };
        parent.AddChild(root);

        var pole = PrimitiveFactory.Cylinder("Pole", 0.075f, 2.2f, _pole, 12);
        pole.Position = new Vector3(0.0f, 1.1f, 0.0f);
        root.AddChild(pole);

        if (control.Kind == TrafficControlKind.StopSign)
        {
            var sign = PrimitiveFactory.Cylinder("StopSign", 0.43f, 0.08f, _signRed, 8);
            sign.Position = new Vector3(0.0f, 2.05f, 0.0f);
            sign.RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f);
            root.AddChild(sign);
            return;
        }

        var housing = PrimitiveFactory.Box(
            "SignalHousing",
            new Vector3(0.4f, 1.05f, 0.3f),
            new Transform3D(Basis.Identity, new Vector3(0.0f, 2.25f, 0.0f)),
            _signalHousing);
        root.AddChild(housing);
        AddSignalLamp(housing, "Red", 0.32f, new Color("9b1c1c"), control.State == "Red");
        AddSignalLamp(housing, "Amber", 0.0f, new Color("d69e16"), control.State == "Amber");
        AddSignalLamp(housing, "Green", -0.32f, new Color("20b45a"), control.State == "Green");
    }

    private static void AddSignalLamp(Node3D housing, string name, float y, Color color, bool active)
    {
        var material = PrimitiveFactory.Material(active ? color : color.Darkened(0.72f), 0.5f, active);
        var lamp = new MeshInstance3D
        {
            Name = name,
            Mesh = new SphereMesh { Radius = 0.105f, Height = 0.21f },
            MaterialOverride = material,
            Position = new Vector3(0.0f, y, -0.17f),
        };
        housing.AddChild(lamp);
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

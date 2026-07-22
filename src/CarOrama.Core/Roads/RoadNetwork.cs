using CarOrama.Core.Geometry;

namespace CarOrama.Core.Roads;

public enum IntersectionKind
{
    DeadEnd,
    Corner,
    Straight,
    ThreeWay,
    FourWay,
}

public enum TrafficControlKind
{
    None,
    StopSign,
    TrafficLight,
}

public enum RoadClassification
{
    Local,
    Arterial,
}

public sealed record RoadNode(
    string Id,
    Vector2D Position,
    IntersectionKind Kind,
    IReadOnlyList<string> ConnectedSegmentIds);

public sealed record RoadSegment(
    string Id,
    string StartNodeId,
    string EndNodeId,
    IReadOnlyList<Vector2D> CenterLine,
    RoadClassification Classification,
    int LanesPerDirection,
    double WidthMeters,
    double SidewalkWidthMeters,
    IReadOnlyList<string> LaneIds);

public sealed record Lane(
    string Id,
    string SegmentId,
    string StartNodeId,
    string EndNodeId,
    IReadOnlyList<Vector2D> CenterLine,
    IReadOnlyList<Vector2D> LeftBoundary,
    IReadOnlyList<Vector2D> RightBoundary,
    Vector2D Direction,
    double WidthMeters,
    double SpeedLimitMetersPerSecond,
    IReadOnlyList<string> SuccessorLaneIds,
    string? StopLineId);

public sealed record StopLine(
    string Id,
    string LaneId,
    string IntersectionNodeId,
    Vector2D LeftPoint,
    Vector2D RightPoint);

public sealed record TrafficControl(
    string Id,
    string IntersectionNodeId,
    string IncomingLaneId,
    TrafficControlKind Kind,
    Vector2D Position,
    Vector2D FacingDirection,
    string State);

public sealed record Intersection(
    string NodeId,
    Vector2D Position,
    IntersectionKind Kind,
    IReadOnlyList<string> IncomingLaneIds,
    IReadOnlyList<string> OutgoingLaneIds,
    IReadOnlyList<string> TrafficControlIds);

public sealed record SpawnPoint(
    string Id,
    string LaneId,
    Vector2D Position,
    Vector2D Forward,
    double DistanceAlongLaneMeters);

public sealed class RoadNetwork
{
    private readonly Dictionary<string, RoadNode> _nodeById;
    private readonly Dictionary<string, RoadSegment> _segmentById;
    private readonly Dictionary<string, Lane> _laneById;

    public RoadNetwork(
        long seed,
        IReadOnlyList<RoadNode> nodes,
        IReadOnlyList<RoadSegment> segments,
        IReadOnlyList<Lane> lanes,
        IReadOnlyList<Intersection> intersections,
        IReadOnlyList<StopLine> stopLines,
        IReadOnlyList<TrafficControl> trafficControls,
        IReadOnlyList<SpawnPoint> spawnPoints)
    {
        Seed = seed;
        Nodes = nodes;
        Segments = segments;
        Lanes = lanes;
        Intersections = intersections;
        StopLines = stopLines;
        TrafficControls = trafficControls;
        SpawnPoints = spawnPoints;
        _nodeById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _segmentById = segments.ToDictionary(segment => segment.Id, StringComparer.Ordinal);
        _laneById = lanes.ToDictionary(lane => lane.Id, StringComparer.Ordinal);
    }

    public long Seed { get; }

    public IReadOnlyList<RoadNode> Nodes { get; }

    public IReadOnlyList<RoadSegment> Segments { get; }

    public IReadOnlyList<Lane> Lanes { get; }

    public IReadOnlyList<Intersection> Intersections { get; }

    public IReadOnlyList<StopLine> StopLines { get; }

    public IReadOnlyList<TrafficControl> TrafficControls { get; }

    public IReadOnlyList<SpawnPoint> SpawnPoints { get; }

    public RoadNode GetNode(string id) => _nodeById[id];

    public RoadSegment GetSegment(string id) => _segmentById[id];

    public Lane GetLane(string id) => _laneById[id];

    public bool TryGetLane(string id, out Lane lane) => _laneById.TryGetValue(id, out lane!);
}

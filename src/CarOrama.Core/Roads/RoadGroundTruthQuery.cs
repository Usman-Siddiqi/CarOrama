using CarOrama.Core.Geometry;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Roads;

public sealed record RoadGroundTruthSnapshot(
    LaneReferenceObservation Lane,
    RouteProgressObservation Route,
    UpcomingTrafficControlObservation UpcomingTrafficControl,
    double ProgressMeters,
    double DistanceFromRouteMeters,
    bool IsLaneDeparture,
    bool IsOutsideDrivableArea,
    bool IsWrongWay);

/// <summary>
/// Projects world-space state onto structured route data without depending on
/// rendered road geometry or Godot scene nodes.
/// </summary>
public sealed class RoadGroundTruthQuery
{
    private const double DefaultLookaheadMeters = 15.0;
    private const double DrivableAreaMarginMeters = 1.0;
    private readonly RoadNetwork _network;
    private readonly DrivingRoute _route;

    public RoadGroundTruthQuery(RoadNetwork network, DrivingRoute route)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _route = route ?? throw new ArgumentNullException(nameof(route));
    }

    public RoadGroundTruthSnapshot Observe(
        Vector2D worldPosition,
        double headingRadians,
        double minimumProgressMeters = 0.0,
        double lookaheadDistanceMeters = DefaultLookaheadMeters,
        Func<TrafficControl, ObservedTrafficControlState>? signalStateResolver = null)
    {
        if (!double.IsFinite(headingRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(headingRadians));
        }

        if (!double.IsFinite(lookaheadDistanceMeters) || lookaheadDistanceMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lookaheadDistanceMeters));
        }

        var projection = _route.Project(worldPosition, minimumProgressMeters);
        var lane = _network.GetLane(projection.Segment.LaneId);
        var lateralOffset = Vector2D.Dot(
            worldPosition - projection.Point,
            projection.Segment.Direction.PerpendicularLeft());
        var laneHeading = Math.Atan2(projection.Segment.Direction.Y, projection.Segment.Direction.X);
        var headingError = NormalizeAngle(headingRadians - laneHeading);
        var remainingDistance = Math.Max(0.0, _route.TotalLengthMeters - projection.ProgressMeters);
        var actualLookahead = Math.Min(lookaheadDistanceMeters, remainingDistance);
        var lookaheadPoint = _route.GetPointAtDistance(projection.ProgressMeters + actualLookahead);
        var halfLaneWidth = lane.WidthMeters * 0.5;

        return new RoadGroundTruthSnapshot(
            LaneReferenceObservation.Create(
                lane.Id,
                projection.Point,
                projection.Segment.Direction,
                lateralOffset,
                headingError,
                curvaturePerMeter: 0.0,
                lane.SpeedLimitMetersPerSecond),
            RouteProgressObservation.Create(
                _route.Id,
                lookaheadPoint,
                actualLookahead,
                projection.ProgressMeters,
                remainingDistance),
            FindUpcomingTrafficControl(projection.ProgressMeters, signalStateResolver),
            projection.ProgressMeters,
            projection.DistanceMeters,
            !projection.Segment.IsIntersectionConnector && Math.Abs(lateralOffset) > halfLaneWidth,
            Math.Abs(lateralOffset) > halfLaneWidth + DrivableAreaMarginMeters,
            Math.Abs(headingError) > Math.PI * 0.5);
    }

    private UpcomingTrafficControlObservation FindUpcomingTrafficControl(
        double progressMeters,
        Func<TrafficControl, ObservedTrafficControlState>? signalStateResolver)
    {
        (TrafficControl Control, StopLine StopLine, double Distance)? nearest = null;
        foreach (var laneId in _route.LaneIds)
        {
            var lane = _network.GetLane(laneId);
            if (lane.StopLineId is null)
            {
                continue;
            }

            var stopLine = _network.StopLines.Single(line => line.Id == lane.StopLineId);
            var stopLineCenter = (stopLine.LeftPoint + stopLine.RightPoint) * 0.5;
            if (!_route.TryGetDistanceForLanePoint(lane.Id, stopLineCenter, out var stopLineProgress))
            {
                continue;
            }

            var distance = stopLineProgress - progressMeters;
            if (distance < -0.25 || nearest is not null && distance >= nearest.Value.Distance)
            {
                continue;
            }

            var control = _network.TrafficControls.Single(candidate =>
                candidate.IncomingLaneIds.Contains(lane.Id, StringComparer.Ordinal));
            nearest = (control, stopLine, Math.Max(0.0, distance));
        }

        if (nearest is null)
        {
            return UpcomingTrafficControlObservation.None;
        }

        var state = nearest.Value.Control.Kind == TrafficControlKind.StopSign
            ? ObservedTrafficControlState.StopRequired
            : signalStateResolver?.Invoke(nearest.Value.Control) ?? ParseStaticSignalState(nearest.Value.Control.State);
        return UpcomingTrafficControlObservation.Create(
            nearest.Value.Control.Kind == TrafficControlKind.StopSign
                ? ObservedTrafficControlKind.StopSign
                : ObservedTrafficControlKind.TrafficSignal,
            state,
            nearest.Value.Control.Id,
            nearest.Value.StopLine.Id,
            nearest.Value.Distance);
    }

    private static ObservedTrafficControlState ParseStaticSignalState(string state)
    {
        return state.ToUpperInvariant() switch
        {
            "RED" => ObservedTrafficControlState.Red,
            "YELLOW" => ObservedTrafficControlState.Yellow,
            "GREEN" => ObservedTrafficControlState.Green,
            _ => ObservedTrafficControlState.Unknown,
        };
    }

    private static double NormalizeAngle(double angleRadians)
    {
        return Math.Atan2(Math.Sin(angleRadians), Math.Cos(angleRadians));
    }
}

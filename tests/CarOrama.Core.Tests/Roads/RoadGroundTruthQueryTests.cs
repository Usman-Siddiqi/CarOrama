using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Roads;

public sealed class RoadGroundTruthQueryTests
{
    private readonly RoadNetwork _network = new GridRoadNetworkGenerator().Generate(
        new RoadNetworkConfig { Seed = 42 });

    [Fact]
    public void RouteRejectsDisconnectedOrRepeatedLanes()
    {
        var first = _network.Lanes[0];
        var disconnected = _network.Lanes.First(lane =>
            lane.Id != first.Id && !first.SuccessorLaneIds.Contains(lane.Id, StringComparer.Ordinal));

        Assert.Throws<ArgumentException>(() => DrivingRoute.Create(
            "disconnected",
            _network,
            [first.Id, disconnected.Id]));
        Assert.Throws<ArgumentException>(() => DrivingRoute.Create(
            "repeated",
            _network,
            [first.Id, first.Id]));
    }

    [Fact]
    public void RouteProvidesContinuousDistanceAcrossIntersectionConnector()
    {
        var first = _network.Lanes.First(lane => lane.SuccessorLaneIds.Count > 0);
        var second = _network.GetLane(first.SuccessorLaneIds[0]);
        var route = DrivingRoute.Create("two-lane-route", _network, [first.Id, second.Id]);
        var laneLength = PolylineLength(first.CenterLine) + PolylineLength(second.CenterLine);
        var connectorLength = Vector2D.Distance(first.CenterLine[^1], second.CenterLine[0]);

        Assert.Equal(laneLength + connectorLength, route.TotalLengthMeters, precision: 9);
        Assert.True(Vector2D.Distance(first.CenterLine[0], route.GetPointAtDistance(0.0)) < 1e-9);
        Assert.True(Vector2D.Distance(
            second.CenterLine[^1],
            route.GetPointAtDistance(route.TotalLengthMeters)) < 1e-9);
    }

    [Fact]
    public void ObservationReportsSignedLaneOffsetHeadingAndRouteLookahead()
    {
        var lane = _network.Lanes[0];
        var route = DrivingRoute.Create("lane-reference", _network, [lane.Id]);
        var query = new RoadGroundTruthQuery(_network, route);
        var center = Vector2D.Lerp(lane.CenterLine[0], lane.CenterLine[^1], 0.5);
        var position = center + lane.Direction.PerpendicularLeft();
        var heading = Math.Atan2(lane.Direction.Y, lane.Direction.X);

        var observation = query.Observe(position, heading, lookaheadDistanceMeters: 12.0);

        Assert.Equal(lane.Id, observation.Lane.LaneId);
        Assert.Equal(1.0, observation.Lane.LateralOffsetMeters, precision: 9);
        Assert.Equal(0.0, observation.Lane.HeadingErrorRadians, precision: 9);
        Assert.Equal(12.0, observation.Route.LookaheadDistanceMeters, precision: 9);
        Assert.False(observation.IsLaneDeparture);
        Assert.False(observation.IsWrongWay);
    }

    [Fact]
    public void ObservationExposesUpcomingStopLineAndControlState()
    {
        var control = _network.TrafficControls[0];
        var lane = _network.GetLane(control.IncomingLaneIds[0]);
        var route = DrivingRoute.Create("controlled-approach", _network, [lane.Id]);
        var stopLine = _network.StopLines.Single(line => line.LaneId == lane.Id);
        var stopLineCenter = (stopLine.LeftPoint + stopLine.RightPoint) * 0.5;
        var position = stopLineCenter - (lane.Direction * 10.0);
        var heading = Math.Atan2(lane.Direction.Y, lane.Direction.X);

        var observation = new RoadGroundTruthQuery(_network, route).Observe(position, heading);

        Assert.Equal(control.Id, observation.UpcomingTrafficControl.TrafficControlId);
        Assert.Equal(stopLine.Id, observation.UpcomingTrafficControl.StopLineId);
        Assert.Equal(10.0, observation.UpcomingTrafficControl.DistanceToStopLineMeters!.Value, precision: 6);
        Assert.Equal(
            control.Kind == TrafficControlKind.StopSign
                ? ObservedTrafficControlState.StopRequired
                : ObservedTrafficControlState.Green,
            observation.UpcomingTrafficControl.State);
    }

    private static double PolylineLength(IReadOnlyList<Vector2D> points)
    {
        var length = 0.0;
        for (var index = 1; index < points.Count; index++)
        {
            length += Vector2D.Distance(points[index - 1], points[index]);
        }

        return length;
    }
}

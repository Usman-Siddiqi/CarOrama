using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Simulation;

public sealed class RoadRuleMetricsMonitorTests
{
    [Fact]
    public void RedSignalIsCountedOnlyWhenItsStopLineIsCrossedOnRed()
    {
        var (network, route) = CreateRoute(TrafficControlKind.TrafficLight, "RED");
        var monitor = new RoadRuleMetricsMonitor(network, route);
        monitor.Reset(0.0);

        monitor.Update(4.9, 4.0, 10.0, false, 0.1);
        Assert.Equal(0, monitor.RedLightViolationCount);

        monitor.Update(5.1, 4.0, 10.0, false, 0.1);
        monitor.Update(6.0, 4.0, 10.0, false, 0.1);

        Assert.Equal(1, monitor.RedLightViolationCount);
    }

    [Fact]
    public void GreenSignalCrossingIsNotAReportedViolation()
    {
        var (network, route) = CreateRoute(TrafficControlKind.TrafficLight, "GREEN");
        var monitor = new RoadRuleMetricsMonitor(network, route);
        monitor.Reset(0.0);

        monitor.Update(5.1, 4.0, 10.0, false, 0.1);

        Assert.Equal(0, monitor.RedLightViolationCount);
    }

    [Fact]
    public void StopSignRequiresAStationaryHoldNearTheLine()
    {
        var (network, route) = CreateRoute(TrafficControlKind.StopSign, "STOP");
        var failedStop = new RoadRuleMetricsMonitor(network, route);
        failedStop.Reset(0.0);
        failedStop.Update(5.1, 3.0, 10.0, false, 0.1);

        var validStop = new RoadRuleMetricsMonitor(network, route);
        validStop.Reset(0.0);
        for (var sample = 0; sample < 6; sample++)
        {
            validStop.Update(4.5, 0.0, 10.0, false, 0.1);
        }

        validStop.Update(5.1, 2.0, 10.0, false, 0.1);

        Assert.Equal(1, failedStop.StopSignViolationCount);
        Assert.Equal(0, validStop.StopSignViolationCount);
    }

    [Fact]
    public void ExposureDurationsAndLaneDepartureEventsAccumulateDeterministically()
    {
        var (network, route) = CreateRoute(TrafficControlKind.TrafficLight, "GREEN");
        var monitor = new RoadRuleMetricsMonitor(network, route);
        monitor.Reset(0.0);

        monitor.Update(1.0, 11.0, 10.0, true, 0.05);
        monitor.Update(2.0, 11.0, 10.0, true, 0.05);
        monitor.Update(3.0, 9.0, 10.0, false, 0.05);
        monitor.Update(4.0, 9.0, 10.0, true, 0.05);

        Assert.Equal(2, monitor.LaneDepartureCount);
        Assert.Equal(0.10, monitor.SpeedingDurationSeconds, 6);
        Assert.Equal(0.15, monitor.LaneDepartureDurationSeconds, 6);
    }

    private static (RoadNetwork Network, DrivingRoute Route) CreateRoute(
        TrafficControlKind kind,
        string state)
    {
        var start = new RoadNode("start", new Vector2D(0.0, 0.0), IntersectionKind.DeadEnd, ["road"]);
        var end = new RoadNode("end", new Vector2D(10.0, 0.0), IntersectionKind.DeadEnd, ["road"]);
        var lane = new Lane(
            "lane",
            "road",
            start.Id,
            end.Id,
            [start.Position, end.Position],
            [new Vector2D(0.0, -1.5), new Vector2D(10.0, -1.5)],
            [new Vector2D(0.0, 1.5), new Vector2D(10.0, 1.5)],
            new Vector2D(1.0, 0.0),
            3.0,
            10.0,
            [],
            "stop-line");
        var segment = new RoadSegment(
            "road",
            start.Id,
            end.Id,
            [start.Position, end.Position],
            RoadClassification.Local,
            1,
            6.0,
            1.5,
            [lane.Id]);
        var stopLine = new StopLine(
            "stop-line",
            lane.Id,
            end.Id,
            new Vector2D(5.0, -1.5),
            new Vector2D(5.0, 1.5));
        var control = new TrafficControl(
            "control",
            end.Id,
            segment.Id,
            [lane.Id],
            kind,
            new Vector2D(5.0, 2.5),
            new Vector2D(-1.0, 0.0),
            state);
        var network = new RoadNetwork(
            1,
            [start, end],
            [segment],
            [lane],
            [],
            [stopLine],
            [control],
            [new SpawnPoint("spawn", lane.Id, start.Position, lane.Direction, 0.0)]);
        return (network, DrivingRoute.Create("route", network, [lane.Id]));
    }
}

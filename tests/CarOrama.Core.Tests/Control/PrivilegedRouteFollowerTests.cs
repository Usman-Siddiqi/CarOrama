using CarOrama.Core.Control;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Control;

public sealed class PrivilegedRouteFollowerTests
{
    [Fact]
    public void BaselineCompletesASeededStraightRouteWithoutLaneDeparture()
    {
        var network = new GridRoadNetworkGenerator().Generate(new RoadNetworkConfig { Seed = 2026 });
        var startLane = network.Lanes.First(lane => lane.SuccessorLaneIds.Any(successorId =>
            CarOrama.Core.Geometry.Vector2D.Dot(lane.Direction, network.GetLane(successorId).Direction) > 0.99));
        var successorLane = network.GetLane(startLane.SuccessorLaneIds.First(successorId =>
            CarOrama.Core.Geometry.Vector2D.Dot(startLane.Direction, network.GetLane(successorId).Direction) > 0.99));
        var spawn = network.SpawnPoints.Single(candidate => candidate.LaneId == startLane.Id);
        var route = DrivingRoute.Create("baseline-straight", network, [startLane.Id, successorLane.Id]);
        var scenario = SimulationScenario.Create(
            "baseline-evaluation",
            physicsTicksPerSecond: 120,
            controlTicksPerSecond: 20,
            maximumControlTicks: 4_000);
        var environment = new DeterministicDrivingEnvironment(network, route);
        var controller = new PrivilegedRouteFollower();
        controller.Reset();
        var observation = environment.Reset(EpisodeResetRequest.Create(
            scenario,
            network.Seed,
            route.Id,
            spawn.Id));

        EpisodeStepResult? result = null;
        while (result is null || !result.IsTerminal)
        {
            result = environment.Step(controller.GetAction(observation));
            observation = result.Observation;
        }

        Assert.False(result.IsTruncated);
        Assert.Equal(EpisodeTerminationReason.RouteCompleted, result.Termination.Reason);
        Assert.Equal(0, result.Metrics.CollisionCount);
        Assert.Equal(0, result.Metrics.LaneDepartureCount);
        Assert.True(result.Metrics.RouteCompletionFraction > 0.99);
    }

    [Fact]
    public void BaselineCompletesASeededIntersectionTurnWithoutLaneDeparture()
    {
        var network = new GridRoadNetworkGenerator().Generate(new RoadNetworkConfig { Seed = 42 });
        var startLane = network.Lanes.First(lane => lane.SuccessorLaneIds.Any(successorId =>
            Math.Abs(CarOrama.Core.Geometry.Vector2D.Dot(
                lane.Direction,
                network.GetLane(successorId).Direction)) < 0.01));
        var successorLane = network.GetLane(startLane.SuccessorLaneIds.First(successorId =>
            Math.Abs(CarOrama.Core.Geometry.Vector2D.Dot(
                startLane.Direction,
                network.GetLane(successorId).Direction)) < 0.01));
        var spawn = network.SpawnPoints.Single(candidate => candidate.LaneId == startLane.Id);
        var route = DrivingRoute.Create("baseline-turn", network, [startLane.Id, successorLane.Id]);
        var scenario = SimulationScenario.Create(
            "baseline-turn-evaluation",
            physicsTicksPerSecond: 120,
            controlTicksPerSecond: 20,
            maximumControlTicks: 4_000);
        var environment = new DeterministicDrivingEnvironment(network, route);
        var controller = new PrivilegedRouteFollower();
        var observation = environment.Reset(EpisodeResetRequest.Create(
            scenario,
            network.Seed,
            route.Id,
            spawn.Id));

        EpisodeStepResult? result = null;
        while (result is null || !result.IsTerminal)
        {
            result = environment.Step(controller.GetAction(observation));
            observation = result.Observation;
        }

        Assert.False(result.IsTruncated);
        Assert.Equal(EpisodeTerminationReason.RouteCompleted, result.Termination.Reason);
        Assert.Equal(0, result.Metrics.LaneDepartureCount);
    }

    [Fact]
    public void BaselineRequestsBrakingForARedSignal()
    {
        var observation = EpisodeObservationFactory.Create(
            speedMetersPerSecond: 12.0,
            remainingRouteMeters: 100.0,
            trafficControl: UpcomingTrafficControlObservation.Create(
                ObservedTrafficControlKind.TrafficSignal,
                ObservedTrafficControlState.Red,
                "signal-1",
                "stop-line-1",
                8.0));

        var action = new PrivilegedRouteFollower().GetAction(observation);

        Assert.True(action.LongitudinalAccelerationMetersPerSecondSquared < 0.0);
    }

    [Fact]
    public void BaselineHoldsThenServesAStopSign()
    {
        var controller = new PrivilegedRouteFollower(config: new PrivilegedRouteFollowerConfig
        {
            StopHoldControlTicks = 3,
        });
        var stopSign = UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.StopSign,
            ObservedTrafficControlState.StopRequired,
            "stop-1",
            "stop-line-1",
            1.0);

        var first = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 0.0,
            remainingRouteMeters: 100.0,
            stopSign,
            controlTick: 0));
        var second = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 0.0,
            remainingRouteMeters: 100.0,
            stopSign,
            controlTick: 1));
        var served = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 0.0,
            remainingRouteMeters: 100.0,
            stopSign,
            controlTick: 2));

        Assert.Equal(0.0, first.LongitudinalAccelerationMetersPerSecondSquared);
        Assert.Equal(0.0, second.LongitudinalAccelerationMetersPerSecondSquared);
        Assert.True(served.LongitudinalAccelerationMetersPerSecondSquared > 0.0);
    }

    private static class EpisodeObservationFactory
    {
        public static PrivilegedObservation Create(
            double speedMetersPerSecond,
            double remainingRouteMeters,
            UpcomingTrafficControlObservation trafficControl,
            long controlTick = 0)
        {
            return PrivilegedObservation.Create(
                controlTick,
                physicsTick: controlTick,
                EgoVehicleObservation.Create(
                    CarOrama.Core.Geometry.Vector2D.Zero,
                    headingRadians: 0.0,
                    speedMetersPerSecond,
                    longitudinalAccelerationMetersPerSecondSquared: 0.0,
                    yawRateRadiansPerSecond: 0.0,
                    steering: 0.0,
                    batteryStateOfCharge: 0.8),
                LaneReferenceObservation.Create(
                    "lane-1",
                    CarOrama.Core.Geometry.Vector2D.Zero,
                    new CarOrama.Core.Geometry.Vector2D(1.0, 0.0),
                    lateralOffsetMeters: 0.0,
                    headingErrorRadians: 0.0,
                    curvaturePerMeter: 0.0,
                    speedLimitMetersPerSecond: 13.9),
                RouteProgressObservation.Create(
                    "route-1",
                    new CarOrama.Core.Geometry.Vector2D(15.0, 0.0),
                    lookaheadDistanceMeters: 15.0,
                    distanceTravelledMeters: 10.0,
                    remainingRouteMeters),
                trafficControl);
        }
    }
}

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

        Assert.False(result.IsTruncated, $"Termination {result.Termination.Reason}; completion {result.Metrics.RouteCompletionFraction:F4}; distance {result.Metrics.DistanceTravelledMeters:F2}; speed {observation.EgoVehicle.SpeedMetersPerSecond:F3}; control {observation.UpcomingTrafficControl.Kind}/{observation.UpcomingTrafficControl.State} at {observation.UpcomingTrafficControl.DistanceToStopLineMeters:F3}");
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

    [Fact]
    public void BaselineLimitsLongitudinalCommandJerkWhileApproachingAStop()
    {
        var controller = new PrivilegedRouteFollower(config: new PrivilegedRouteFollowerConfig
        {
            MaximumLongitudinalJerkMetersPerSecondCubed = 2.0,
            ControlTicksPerSecond = 20,
        });
        var redSignal = UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.TrafficSignal,
            ObservedTrafficControlState.Red,
            "signal-1",
            "stop-line-1",
            8.0);

        var first = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 12.0,
            remainingRouteMeters: 100.0,
            redSignal,
            controlTick: 0));
        var second = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 12.0,
            remainingRouteMeters: 100.0,
            redSignal,
            controlTick: 1));

        Assert.InRange(first.LongitudinalAccelerationMetersPerSecondSquared, -0.100001, -0.099999);
        Assert.InRange(
            second.LongitudinalAccelerationMetersPerSecondSquared - first.LongitudinalAccelerationMetersPerSecondSquared,
            -0.100001,
            0.100001);
    }

    [Fact]
    public void BaselineActivatesAStopUsingSpeedDependentBrakingDistance()
    {
        var controller = new PrivilegedRouteFollower(config: new PrivilegedRouteFollowerConfig
        {
            SpeedControlGain = 0.01,
            MaximumLongitudinalJerkMetersPerSecondCubed = 1_000.0,
        });
        var stopSign = UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.StopSign,
            ObservedTrafficControlState.StopRequired,
            "stop-envelope",
            "stop-line-envelope",
            18.0);

        var action = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 10.0,
            remainingRouteMeters: 100.0,
            stopSign,
            controlTick: 0));

        Assert.True(action.LongitudinalAccelerationMetersPerSecondSquared < -2.5);
    }

    [Fact]
    public void GreenSignalReleasesAnActiveStopBrakeImmediately()
    {
        var controller = new PrivilegedRouteFollower();
        var red = UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.TrafficSignal,
            ObservedTrafficControlState.Red,
            "signal-release",
            "stop-line-release",
            2.8);
        var green = UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.TrafficSignal,
            ObservedTrafficControlState.Green,
            "signal-release",
            "stop-line-release",
            2.8);

        var braking = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 8.0,
            remainingRouteMeters: 100.0,
            red,
            controlTick: 0));
        var released = controller.GetAction(EpisodeObservationFactory.Create(
            speedMetersPerSecond: 7.8,
            remainingRouteMeters: 100.0,
            green,
            controlTick: 1));

        Assert.True(braking.LongitudinalAccelerationMetersPerSecondSquared < 0.0);
        Assert.True(released.LongitudinalAccelerationMetersPerSecondSquared >= 0.0);
    }
    [Fact]
    public void BaselineRespectsCombinedGForceLimit()
    {
        var controller = new PrivilegedRouteFollower(config: new PrivilegedRouteFollowerConfig
        {
            MaximumAccelerationMetersPerSecondSquared = 10.0,
            MaximumCombinedAccelerationG = 0.20,
            MaximumLongitudinalJerkMetersPerSecondCubed = 100.0,
        });
        var observation = EpisodeObservationFactory.Create(
            speedMetersPerSecond: 2.0,
            remainingRouteMeters: 500.0,
            UpcomingTrafficControlObservation.None,
            speedLimitMetersPerSecond: 13.9,
            steering: 0.5);

        var action = controller.GetAction(observation);

        Assert.InRange(
            Math.Abs(action.LongitudinalAccelerationMetersPerSecondSquared),
            0.0,
            (0.20 * 9.80665) + 1e-6);
    }
    [Fact]
    public void CruiseVariationIsSmoothlySeededAndReproducible()
    {
        static PrivilegedRouteFollower CreateController(long seed) => new(config: new PrivilegedRouteFollowerConfig
        {
            MaximumCruiseSpeedMetersPerSecond = 16.3,
            SpeedLimitMarginMetersPerSecond = 0.4,
            CruiseSpeedVariationMetersPerSecond = 0.75,
            DriverProfileSeed = seed,
            MaximumLongitudinalJerkMetersPerSecondCubed = 1_000.0,
        });

        static double[] SampleProfile(long seed) => new long[] { 0, 100, 200, 300 }
            .Select(tick => CreateController(seed).GetAction(EpisodeObservationFactory.Create(
                speedMetersPerSecond: 15.5,
                remainingRouteMeters: 500.0,
                UpcomingTrafficControlObservation.None,
                controlTick: tick,
                speedLimitMetersPerSecond: 16.67))
                .LongitudinalAccelerationMetersPerSecondSquared)
            .ToArray();

        var first = SampleProfile(101);
        var repeated = SampleProfile(101);
        var otherProfile = SampleProfile(203);

        Assert.Equal(first, repeated);
        Assert.Contains(first.Zip(otherProfile), pair => Math.Abs(pair.First - pair.Second) > 1e-9);
    }
    [Fact]
    public void BaselineSignalsBeforeTurnAndCancelsAfterwards()
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
        var route = DrivingRoute.Create("signal-turn", network, [startLane.Id, successorLane.Id]);
        var signalProgress = Enumerable.Range(0, (int)Math.Ceiling(route.TotalLengthMeters) + 1)
            .Select(distance => (double)distance)
            .First(distance =>
            {
                var current = route.GetDirectionAtDistance(distance);
                var future = route.GetDirectionAtDistance(distance + 30.0);
                var cross = (current.X * future.Y) - (current.Y * future.X);
                return Math.Atan2(Math.Abs(cross), CarOrama.Core.Geometry.Vector2D.Dot(current, future)) >= 0.30;
            });
        var currentDirection = route.GetDirectionAtDistance(signalProgress);
        var futureDirection = route.GetDirectionAtDistance(signalProgress + 30.0);
        var crossProduct = (currentDirection.X * futureDirection.Y) -
            (currentDirection.Y * futureDirection.X);
        var expectedSignal = crossProduct > 0.0 ? TurnSignalState.Right : TurnSignalState.Left;
        var controller = new PrivilegedRouteFollower();
        var approachingTurn = EpisodeObservationFactory.Create(
            speedMetersPerSecond: 8.0,
            remainingRouteMeters: route.TotalLengthMeters - signalProgress,
            UpcomingTrafficControlObservation.None,
            distanceTravelledMeters: signalProgress);

        var signal = controller.GetLightingCommand(approachingTurn, route);

        Assert.Equal(expectedSignal, signal.TurnSignal);
        Assert.True(signal.HeadlightsEnabled);

        VehicleLightingCommand cancelled = signal;
        for (var tick = 1; tick <= 10; tick++)
        {
            var afterTurn = EpisodeObservationFactory.Create(
                speedMetersPerSecond: 8.0,
                remainingRouteMeters: 0.0,
                UpcomingTrafficControlObservation.None,
                controlTick: tick,
                distanceTravelledMeters: route.TotalLengthMeters);
            cancelled = controller.GetLightingCommand(afterTurn, route);
        }

        Assert.Equal(TurnSignalState.Off, cancelled.TurnSignal);
    }

    private static class EpisodeObservationFactory
    {
        public static PrivilegedObservation Create(
            double speedMetersPerSecond,
            double remainingRouteMeters,
            UpcomingTrafficControlObservation trafficControl,
            long controlTick = 0,
            double distanceTravelledMeters = 10.0,
            double speedLimitMetersPerSecond = 13.9,
            double steering = 0.0)
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
                    steering,
                    batteryStateOfCharge: 0.8),
                LaneReferenceObservation.Create(
                    "lane-1",
                    CarOrama.Core.Geometry.Vector2D.Zero,
                    new CarOrama.Core.Geometry.Vector2D(1.0, 0.0),
                    lateralOffsetMeters: 0.0,
                    headingErrorRadians: 0.0,
                    curvaturePerMeter: 0.0,
                    speedLimitMetersPerSecond),
                RouteProgressObservation.Create(
                    "route-1",
                    new CarOrama.Core.Geometry.Vector2D(15.0, 0.0),
                    lookaheadDistanceMeters: 15.0,
                    distanceTravelledMeters,
                    remainingRouteMeters),
                trafficControl);
        }
    }
}

using CarOrama.Core.Geometry;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Simulation;

public sealed class EpisodeContractsTests
{
    [Fact]
    public void ScenarioDefinesAnIntegerFixedStepSchedule()
    {
        var scenario = SimulationScenario.Create("lane-following-v1", 120, 20, 2_400);

        Assert.Equal(SimulationContract.CurrentVersion, scenario.ContractVersion);
        Assert.Equal(6, scenario.PhysicsTicksPerControlTick);
        Assert.Equal(1.0 / 120.0, scenario.FixedPhysicsDeltaSeconds, precision: 12);
        Assert.Equal(1.0 / 20.0, scenario.FixedControlDeltaSeconds, precision: 12);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(120, 0)]
    [InlineData(20, 120)]
    [InlineData(120, 22)]
    public void ScenarioRejectsInvalidFixedStepSchedules(int physicsRate, int controlRate)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => SimulationScenario.Create("invalid", physicsRate, controlRate, 100));
    }

    [Fact]
    public void ResetRequestsHaveDeterministicValueEquality()
    {
        var first = CreateReset(seed: 7_741);
        var second = CreateReset(seed: 7_741);
        var differentSeed = CreateReset(seed: 7_742);

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentSeed);
        Assert.Equal(0, first.InitialControlTick);
        Assert.Equal(0, first.InitialPhysicsTick);
    }

    [Fact]
    public void ResetRejectsMissingRouteAndSpawnIdentifiers()
    {
        var scenario = CreateScenario();

        Assert.Throws<ArgumentException>(() => EpisodeResetRequest.Create(scenario, 1, "", "spawn-1"));
        Assert.Throws<ArgumentException>(() => EpisodeResetRequest.Create(scenario, 1, "route-1", " "));
    }

    [Fact]
    public void DrivingActionClampsFiniteOutputsAndRejectsNonFiniteValues()
    {
        var maximum = DrivingAction.Create(8, 4.0, 20.0);
        var minimum = DrivingAction.Create(8, -4.0, -20.0);

        Assert.Equal(1.0, maximum.Steering);
        Assert.Equal(DrivingAction.MaximumLongitudinalAccelerationMetersPerSecondSquared,
            maximum.LongitudinalAccelerationMetersPerSecondSquared);
        Assert.Equal(-1.0, minimum.Steering);
        Assert.Equal(DrivingAction.MinimumLongitudinalAccelerationMetersPerSecondSquared,
            minimum.LongitudinalAccelerationMetersPerSecondSquared);
        Assert.Throws<ArgumentOutOfRangeException>(() => DrivingAction.Create(8, double.NaN, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrivingAction.Create(
            8,
            0.0,
            double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrivingAction.Neutral(-1));
    }

    [Fact]
    public void PrivilegedObservationsHaveDeterministicValueEquality()
    {
        var first = CreateObservation();
        var second = CreateObservation();

        Assert.Equal(first, second);
        Assert.Equal(new Vector2D(1.0, 0.0), first.Lane.Direction);
        Assert.Equal(18, first.ControlTick);
        Assert.Equal(108, first.PhysicsTick);
    }

    [Fact]
    public void ObservationComponentsRejectInvalidSimulatorState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EgoVehicleObservation.Create(
            Vector2D.Zero,
            0.0,
            -0.1,
            0.0,
            0.0,
            0.0,
            0.8));

        Assert.Throws<ArgumentException>(() => LaneReferenceObservation.Create(
            "lane-1",
            Vector2D.Zero,
            Vector2D.Zero,
            0.0,
            0.0,
            0.0,
            13.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => PrivilegedObservation.Create(
            10,
            9,
            CreateEgo(),
            CreateLane(),
            CreateRoute(),
            UpcomingTrafficControlObservation.None));
    }

    [Fact]
    public void UpcomingTrafficControlStateMustMatchItsKind()
    {
        Assert.Throws<ArgumentException>(() => UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.StopSign,
            ObservedTrafficControlState.Green,
            "control-1",
            "stop-line-1",
            20.0));

        Assert.Throws<ArgumentException>(() => UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.TrafficSignal,
            ObservedTrafficControlState.StopRequired,
            "control-1",
            "stop-line-1",
            20.0));
    }

    [Fact]
    public void MetricsRejectImpossibleCumulativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMetrics(routeCompletion: 1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => EpisodeMetrics.Create(
            1.0,
            2.0,
            0.1,
            -1,
            0,
            0,
            0,
            0.2,
            0.1));
    }

    [Fact]
    public void StepResultsDeriveConsistentTerminalSemantics()
    {
        var observation = CreateObservation();
        var metrics = CreateMetrics(routeCompletion: 0.4);
        var ongoing = EpisodeStepResult.Create(observation, 0.25, metrics, EpisodeTermination.Ongoing);
        var completed = EpisodeStepResult.Create(
            observation,
            10.0,
            metrics,
            EpisodeTermination.Terminated(EpisodeTerminationReason.RouteCompleted));
        var timedOut = EpisodeStepResult.Create(
            observation,
            -1.0,
            metrics,
            EpisodeTermination.Truncated(EpisodeTerminationReason.ControlTickLimitReached));

        Assert.False(ongoing.IsTerminal);
        Assert.False(ongoing.IsTruncated);
        Assert.True(completed.IsTerminal);
        Assert.False(completed.IsTruncated);
        Assert.True(timedOut.IsTerminal);
        Assert.True(timedOut.IsTruncated);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpisodeTermination.Terminated(EpisodeTerminationReason.ControlTickLimitReached));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpisodeTermination.Truncated(EpisodeTerminationReason.Collision));
    }

    [Fact]
    public void StepResultsHaveDeterministicValueEqualityAndFiniteRewards()
    {
        var first = EpisodeStepResult.Create(
            CreateObservation(),
            0.5,
            CreateMetrics(routeCompletion: 0.4),
            EpisodeTermination.Ongoing);
        var second = EpisodeStepResult.Create(
            CreateObservation(),
            0.5,
            CreateMetrics(routeCompletion: 0.4),
            EpisodeTermination.Ongoing);

        Assert.Equal(first, second);
        Assert.Equal(18, first.ControlTick);
        Assert.Throws<ArgumentOutOfRangeException>(() => EpisodeStepResult.Create(
            CreateObservation(),
            double.NaN,
            CreateMetrics(routeCompletion: 0.4),
            EpisodeTermination.Ongoing));
    }

    private static SimulationScenario CreateScenario()
    {
        return SimulationScenario.Create("lane-following-v1", 120, 20, 2_400);
    }

    private static EpisodeResetRequest CreateReset(long seed)
    {
        return EpisodeResetRequest.Create(CreateScenario(), seed, "route-12", "spawn-4");
    }

    private static EgoVehicleObservation CreateEgo()
    {
        return EgoVehicleObservation.Create(
            new Vector2D(10.0, 20.0),
            0.1,
            11.5,
            -0.4,
            0.02,
            -0.1,
            0.82);
    }

    private static LaneReferenceObservation CreateLane()
    {
        return LaneReferenceObservation.Create(
            "lane-8",
            new Vector2D(10.0, 19.7),
            new Vector2D(2.0, 0.0),
            0.3,
            0.05,
            0.001,
            13.9);
    }

    private static RouteProgressObservation CreateRoute()
    {
        return RouteProgressObservation.Create(
            "route-12",
            new Vector2D(25.0, 19.7),
            15.0,
            80.0,
            120.0);
    }

    private static PrivilegedObservation CreateObservation()
    {
        var trafficControl = UpcomingTrafficControlObservation.Create(
            ObservedTrafficControlKind.TrafficSignal,
            ObservedTrafficControlState.Red,
            "signal-3",
            "stop-line-3",
            28.0);

        return PrivilegedObservation.Create(
            18,
            108,
            CreateEgo(),
            CreateLane(),
            CreateRoute(),
            trafficControl);
    }

    private static EpisodeMetrics CreateMetrics(double routeCompletion)
    {
        return EpisodeMetrics.Create(
            0.9,
            80.0,
            routeCompletion,
            0,
            0,
            0,
            0,
            18.0,
            0.6);
    }
}

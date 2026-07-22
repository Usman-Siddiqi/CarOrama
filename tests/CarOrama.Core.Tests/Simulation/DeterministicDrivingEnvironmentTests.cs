using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Simulation;

public sealed class DeterministicDrivingEnvironmentTests
{
    [Fact]
    public void ResetProducesStructuredTickZeroObservationAtRequestedSpawn()
    {
        var fixture = CreateFixture(maximumControlTicks: 100);

        var observation = fixture.Environment.Reset(fixture.ResetRequest);

        Assert.Equal(0, observation.ControlTick);
        Assert.Equal(0, observation.PhysicsTick);
        Assert.Equal(fixture.Spawn.LaneId, observation.Lane.LaneId);
        Assert.Equal(fixture.Route.Id, observation.Route.RouteId);
        Assert.Equal(0.0, observation.Lane.LateralOffsetMeters, precision: 9);
        Assert.False(observation.IsOutsideDrivableArea);
        Assert.Equal(observation, fixture.Environment.Observe());
    }

    [Fact]
    public void SameResetAndActionSequenceProduceIdenticalResults()
    {
        var first = CreateFixture(maximumControlTicks: 100);
        var second = CreateFixture(maximumControlTicks: 100);
        first.Environment.Reset(first.ResetRequest);
        second.Environment.Reset(second.ResetRequest);

        for (var tick = 0L; tick < 12; tick++)
        {
            var action = DrivingAction.Create(tick, steering: 0.0, longitudinalAccelerationMetersPerSecondSquared: 2.0);
            var firstResult = first.Environment.Step(action);
            var secondResult = second.Environment.Step(action);

            Assert.Equal(firstResult, secondResult);
        }
    }

    [Fact]
    public void StepAdvancesExactPhysicsCadenceAndAccumulatesMetrics()
    {
        var fixture = CreateFixture(maximumControlTicks: 100);
        var initial = fixture.Environment.Reset(fixture.ResetRequest);

        var result = fixture.Environment.Step(DrivingAction.Create(0, 0.0, 2.0));

        Assert.Equal(1, result.Observation.ControlTick);
        Assert.Equal(6, result.Observation.PhysicsTick);
        Assert.True(result.Observation.EgoVehicle.SpeedMetersPerSecond > initial.EgoVehicle.SpeedMetersPerSecond);
        Assert.True(result.Metrics.DistanceTravelledMeters > 0.0);
        Assert.True(result.Metrics.EnergyConsumedWattHours > 0.0);
        Assert.True(result.Observation.Route.DistanceTravelledMeters >= initial.Route.DistanceTravelledMeters);
        Assert.False(result.IsTerminal);
    }

    [Fact]
    public void EnvironmentRejectsOutOfOrderActionsAndStepsAfterTermination()
    {
        var fixture = CreateFixture(maximumControlTicks: 1);
        fixture.Environment.Reset(fixture.ResetRequest);

        Assert.Throws<ArgumentException>(() => fixture.Environment.Step(DrivingAction.Neutral(1)));
        var terminal = fixture.Environment.Step(DrivingAction.Neutral(0));

        Assert.True(terminal.IsTerminal);
        Assert.True(terminal.IsTruncated);
        Assert.Equal(EpisodeTerminationReason.ControlTickLimitReached, terminal.Termination.Reason);
        Assert.Throws<InvalidOperationException>(() => fixture.Environment.Step(DrivingAction.Neutral(1)));
    }

    [Fact]
    public void ResetValidatesSeedRouteAndSpawnMembership()
    {
        var fixture = CreateFixture(maximumControlTicks: 100);
        var scenario = fixture.ResetRequest.Scenario;

        Assert.Throws<ArgumentException>(() => fixture.Environment.Reset(EpisodeResetRequest.Create(
            scenario,
            fixture.Network.Seed + 1,
            fixture.Route.Id,
            fixture.Spawn.Id)));
        Assert.Throws<ArgumentException>(() => fixture.Environment.Reset(EpisodeResetRequest.Create(
            scenario,
            fixture.Network.Seed,
            "unknown-route",
            fixture.Spawn.Id)));
    }

    private static EnvironmentFixture CreateFixture(long maximumControlTicks)
    {
        var network = new GridRoadNetworkGenerator().Generate(new RoadNetworkConfig { Seed = 42 });
        var spawn = network.SpawnPoints[0];
        var route = DrivingRoute.Create("reference-route", network, [spawn.LaneId]);
        var scenario = SimulationScenario.Create(
            "reference-lane-following",
            physicsTicksPerSecond: 120,
            controlTicksPerSecond: 20,
            maximumControlTicks);
        var reset = EpisodeResetRequest.Create(scenario, network.Seed, route.Id, spawn.Id);
        return new EnvironmentFixture(
            network,
            spawn,
            route,
            reset,
            new DeterministicDrivingEnvironment(network, route));
    }

    private sealed record EnvironmentFixture(
        RoadNetwork Network,
        SpawnPoint Spawn,
        DrivingRoute Route,
        EpisodeResetRequest ResetRequest,
        DeterministicDrivingEnvironment Environment);
}

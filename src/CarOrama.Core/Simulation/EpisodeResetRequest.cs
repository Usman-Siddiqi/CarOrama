namespace CarOrama.Core.Simulation;

public sealed record EpisodeResetRequest
{
    private EpisodeResetRequest(
        SimulationScenario scenario,
        long seed,
        string routeId,
        string spawnPointId)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        Scenario = scenario;
        Seed = seed;
        RouteId = routeId;
        SpawnPointId = spawnPointId;
    }

    public int ContractVersion { get; }

    public SimulationScenario Scenario { get; }

    public long Seed { get; }

    public string RouteId { get; }

    public string SpawnPointId { get; }

    public long InitialControlTick => 0;

    public long InitialPhysicsTick => 0;

    public static EpisodeResetRequest Create(
        SimulationScenario? scenario,
        long seed,
        string? routeId,
        string? spawnPointId)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario.ContractVersion != SimulationContract.CurrentVersion)
        {
            throw new ArgumentException("The scenario contract version is not supported.", nameof(scenario));
        }

        return new EpisodeResetRequest(
            scenario,
            seed,
            SimulationContractValidation.RequireIdentifier(routeId, nameof(routeId)),
            SimulationContractValidation.RequireIdentifier(spawnPointId, nameof(spawnPointId)));
    }
}

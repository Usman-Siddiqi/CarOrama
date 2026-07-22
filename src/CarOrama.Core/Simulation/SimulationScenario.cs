namespace CarOrama.Core.Simulation;

public sealed record SimulationScenario
{
    private SimulationScenario(
        string scenarioId,
        int physicsTicksPerSecond,
        int controlTicksPerSecond,
        long maximumControlTicks)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        ScenarioId = scenarioId;
        PhysicsTicksPerSecond = physicsTicksPerSecond;
        ControlTicksPerSecond = controlTicksPerSecond;
        MaximumControlTicks = maximumControlTicks;
    }

    public int ContractVersion { get; }

    public string ScenarioId { get; }

    public int PhysicsTicksPerSecond { get; }

    public int ControlTicksPerSecond { get; }

    public long MaximumControlTicks { get; }

    public int PhysicsTicksPerControlTick => PhysicsTicksPerSecond / ControlTicksPerSecond;

    public double FixedPhysicsDeltaSeconds => 1.0 / PhysicsTicksPerSecond;

    public double FixedControlDeltaSeconds => 1.0 / ControlTicksPerSecond;

    public static SimulationScenario Create(
        string? scenarioId,
        int physicsTicksPerSecond,
        int controlTicksPerSecond,
        long maximumControlTicks)
    {
        var validatedScenarioId = SimulationContractValidation.RequireIdentifier(
            scenarioId,
            nameof(scenarioId));

        if (physicsTicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicsTicksPerSecond),
                "The physics tick rate must be positive.");
        }

        if (controlTicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(controlTicksPerSecond),
                "The control tick rate must be positive.");
        }

        if (physicsTicksPerSecond < controlTicksPerSecond ||
            physicsTicksPerSecond % controlTicksPerSecond != 0)
        {
            throw new ArgumentException(
                "The physics tick rate must be an integer multiple of the control tick rate.",
                nameof(physicsTicksPerSecond));
        }

        if (maximumControlTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumControlTicks),
                "An episode must allow at least one control tick.");
        }

        return new SimulationScenario(
            validatedScenarioId,
            physicsTicksPerSecond,
            controlTicksPerSecond,
            maximumControlTicks);
    }
}

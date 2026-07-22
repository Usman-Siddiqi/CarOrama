namespace CarOrama.Core.Simulation;

/// <summary>
/// Transport-independent episode boundary shared by in-process validation and
/// future remote training adapters. Each step advances exactly one control tick;
/// the active scenario defines how many fixed physics ticks that contains.
/// </summary>
public interface ISimulationEnvironment
{
    SimulationScenario? ActiveScenario { get; }

    PrivilegedObservation Reset(EpisodeResetRequest request);

    PrivilegedObservation Observe();

    EpisodeStepResult Step(DrivingAction action);
}

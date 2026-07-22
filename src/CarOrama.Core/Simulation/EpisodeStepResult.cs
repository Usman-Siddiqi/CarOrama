namespace CarOrama.Core.Simulation;

public sealed record EpisodeStepResult
{
    private EpisodeStepResult(
        PrivilegedObservation observation,
        double reward,
        EpisodeMetrics metrics,
        EpisodeTermination termination)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        Observation = observation;
        Reward = reward;
        Metrics = metrics;
        Termination = termination;
    }

    public int ContractVersion { get; }

    public PrivilegedObservation Observation { get; }

    public double Reward { get; }

    public EpisodeMetrics Metrics { get; }

    public EpisodeTermination Termination { get; }

    public long ControlTick => Observation.ControlTick;

    public bool IsTerminal => Termination.IsTerminal;

    public bool IsTruncated => Termination.IsTruncated;

    public static EpisodeStepResult Create(
        PrivilegedObservation? observation,
        double reward,
        EpisodeMetrics? metrics,
        EpisodeTermination? termination)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(termination);
        SimulationContractValidation.RequireFinite(reward, nameof(reward));

        if (observation.ContractVersion != SimulationContract.CurrentVersion ||
            metrics.ContractVersion != SimulationContract.CurrentVersion ||
            termination.ContractVersion != SimulationContract.CurrentVersion)
        {
            throw new ArgumentException("All step-result values must use the current contract version.");
        }

        return new EpisodeStepResult(observation, reward, metrics, termination);
    }
}

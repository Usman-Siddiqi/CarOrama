namespace CarOrama.Core.Simulation;

public enum EpisodeTerminationKind
{
    Ongoing,
    Terminated,
    Truncated,
}

public enum EpisodeTerminationReason
{
    None,
    RouteCompleted,
    Collision,
    LaneDeparture,
    LeftDrivableArea,
    WrongWay,
    Stuck,
    ControlTickLimitReached,
    SimulationFault,
}

public sealed record EpisodeTermination
{
    private EpisodeTermination(
        EpisodeTerminationKind kind,
        EpisodeTerminationReason reason,
        string? detail)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        Kind = kind;
        Reason = reason;
        Detail = detail;
    }

    public static EpisodeTermination Ongoing { get; } = new(
        EpisodeTerminationKind.Ongoing,
        EpisodeTerminationReason.None,
        null);

    public int ContractVersion { get; }

    public EpisodeTerminationKind Kind { get; }

    public EpisodeTerminationReason Reason { get; }

    public string? Detail { get; }

    public bool IsTerminal => Kind != EpisodeTerminationKind.Ongoing;

    public bool IsTruncated => Kind == EpisodeTerminationKind.Truncated;

    public static EpisodeTermination Terminated(
        EpisodeTerminationReason reason,
        string? detail = null)
    {
        if (reason is not (EpisodeTerminationReason.RouteCompleted or
            EpisodeTerminationReason.Collision or
            EpisodeTerminationReason.LaneDeparture or
            EpisodeTerminationReason.LeftDrivableArea or
            EpisodeTerminationReason.WrongWay or
            EpisodeTerminationReason.Stuck))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                "This reason does not describe a terminal task outcome.");
        }

        return new EpisodeTermination(
            EpisodeTerminationKind.Terminated,
            reason,
            NormalizeDetail(detail));
    }

    public static EpisodeTermination Truncated(
        EpisodeTerminationReason reason,
        string? detail = null)
    {
        if (reason is not (EpisodeTerminationReason.ControlTickLimitReached or
            EpisodeTerminationReason.SimulationFault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                "This reason does not describe an external episode truncation.");
        }

        return new EpisodeTermination(
            EpisodeTerminationKind.Truncated,
            reason,
            NormalizeDetail(detail));
    }

    private static string? NormalizeDetail(string? detail)
    {
        return string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }
}

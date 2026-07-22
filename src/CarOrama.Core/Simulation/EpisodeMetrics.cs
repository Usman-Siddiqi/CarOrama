namespace CarOrama.Core.Simulation;

public sealed record EpisodeMetrics
{
    private EpisodeMetrics(
        double elapsedSimulationSeconds,
        double distanceTravelledMeters,
        double routeCompletionFraction,
        int collisionCount,
        int laneDepartureCount,
        int redLightViolationCount,
        int stopSignViolationCount,
        double energyConsumedWattHours,
        double meanAbsoluteJerkMetersPerSecondCubed,
        double speedingDurationSeconds,
        double laneDepartureDurationSeconds)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        ElapsedSimulationSeconds = elapsedSimulationSeconds;
        DistanceTravelledMeters = distanceTravelledMeters;
        RouteCompletionFraction = routeCompletionFraction;
        CollisionCount = collisionCount;
        LaneDepartureCount = laneDepartureCount;
        RedLightViolationCount = redLightViolationCount;
        StopSignViolationCount = stopSignViolationCount;
        EnergyConsumedWattHours = energyConsumedWattHours;
        MeanAbsoluteJerkMetersPerSecondCubed = meanAbsoluteJerkMetersPerSecondCubed;
        SpeedingDurationSeconds = speedingDurationSeconds;
        LaneDepartureDurationSeconds = laneDepartureDurationSeconds;
    }

    public int ContractVersion { get; }

    public double ElapsedSimulationSeconds { get; }

    public double DistanceTravelledMeters { get; }

    public double RouteCompletionFraction { get; }

    public int CollisionCount { get; }

    public int LaneDepartureCount { get; }

    public int RedLightViolationCount { get; }

    public int StopSignViolationCount { get; }

    public double EnergyConsumedWattHours { get; }

    public double MeanAbsoluteJerkMetersPerSecondCubed { get; }

    public double SpeedingDurationSeconds { get; }

    public double LaneDepartureDurationSeconds { get; }

    public static EpisodeMetrics Create(
        double elapsedSimulationSeconds,
        double distanceTravelledMeters,
        double routeCompletionFraction,
        int collisionCount,
        int laneDepartureCount,
        int redLightViolationCount,
        int stopSignViolationCount,
        double energyConsumedWattHours,
        double meanAbsoluteJerkMetersPerSecondCubed,
        double speedingDurationSeconds = 0.0,
        double laneDepartureDurationSeconds = 0.0)
    {
        SimulationContractValidation.RequireNonNegative(
            elapsedSimulationSeconds,
            nameof(elapsedSimulationSeconds));
        SimulationContractValidation.RequireNonNegative(
            distanceTravelledMeters,
            nameof(distanceTravelledMeters));

        if (!double.IsFinite(routeCompletionFraction) || routeCompletionFraction is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(routeCompletionFraction),
                "Route completion must be within [0, 1].");
        }

        RequireNonNegativeCount(collisionCount, nameof(collisionCount));
        RequireNonNegativeCount(laneDepartureCount, nameof(laneDepartureCount));
        RequireNonNegativeCount(redLightViolationCount, nameof(redLightViolationCount));
        RequireNonNegativeCount(stopSignViolationCount, nameof(stopSignViolationCount));

        return new EpisodeMetrics(
            elapsedSimulationSeconds,
            distanceTravelledMeters,
            routeCompletionFraction,
            collisionCount,
            laneDepartureCount,
            redLightViolationCount,
            stopSignViolationCount,
            SimulationContractValidation.RequireNonNegative(
                energyConsumedWattHours,
                nameof(energyConsumedWattHours)),
            SimulationContractValidation.RequireNonNegative(
                meanAbsoluteJerkMetersPerSecondCubed,
                nameof(meanAbsoluteJerkMetersPerSecondCubed)),
            SimulationContractValidation.RequireNonNegative(
                speedingDurationSeconds,
                nameof(speedingDurationSeconds)),
            SimulationContractValidation.RequireNonNegative(
                laneDepartureDurationSeconds,
                nameof(laneDepartureDurationSeconds)));
    }

    private static void RequireNonNegativeCount(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A cumulative count cannot be negative.");
        }
    }
}

namespace CarOrama.Core.Vehicles.Dynamics;

public sealed record SuspensionSpecification
{
    public double RestLengthMeters { get; init; } = 0.42;

    public double MaximumCompressionMeters { get; init; } = 0.22;

    public double SpringRateNewtonsPerMeter { get; init; } = 45_000.0;

    public double DampingNewtonSecondsPerMeter { get; init; } = 5_200.0;
}

public readonly record struct SuspensionOutput(
    double CompressionMeters,
    double CompressionVelocityMetersPerSecond,
    double NormalForceNewtons);

public sealed class SuspensionModel(SuspensionSpecification specification)
{
    private double _previousCompressionMeters;

    public SuspensionSpecification Specification { get; } = specification ?? throw new ArgumentNullException(nameof(specification));

    public SuspensionOutput Evaluate(double measuredSpringLengthMeters, double deltaSeconds)
    {
        if (!double.IsFinite(measuredSpringLengthMeters) || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return default;
        }

        var compression = Math.Clamp(
            Specification.RestLengthMeters - measuredSpringLengthMeters,
            0.0,
            Specification.MaximumCompressionMeters);
        var compressionVelocity = (compression - _previousCompressionMeters) / deltaSeconds;
        _previousCompressionMeters = compression;
        var force = Math.Max(
            0.0,
            (compression * Specification.SpringRateNewtonsPerMeter) +
            (compressionVelocity * Specification.DampingNewtonSecondsPerMeter));
        return new SuspensionOutput(compression, compressionVelocity, force);
    }

    public void Reset() => _previousCompressionMeters = 0.0;
}


namespace CarOrama.Core.Vehicles.Dynamics;

public sealed record TireSpecification
{
    public double FrictionCoefficient { get; init; } = 1.05;

    public double LateralStiffnessNewtonSecondsPerMeter { get; init; } = 11_500.0;
}

public readonly record struct TireForceOutput(
    double LongitudinalForceNewtons,
    double LateralForceNewtons,
    bool Saturated);

public sealed class TireForceModel(TireSpecification specification)
{
    public TireSpecification Specification { get; } = specification ?? throw new ArgumentNullException(nameof(specification));

    public TireForceOutput Evaluate(
        double normalForceNewtons,
        double requestedLongitudinalForceNewtons,
        double lateralVelocityMetersPerSecond)
    {
        if (!double.IsFinite(normalForceNewtons) ||
            !double.IsFinite(requestedLongitudinalForceNewtons) ||
            !double.IsFinite(lateralVelocityMetersPerSecond) ||
            normalForceNewtons <= 0.0)
        {
            return default;
        }

        var lateralForce = -lateralVelocityMetersPerSecond * Specification.LateralStiffnessNewtonSecondsPerMeter;
        var magnitude = Math.Sqrt(
            (requestedLongitudinalForceNewtons * requestedLongitudinalForceNewtons) +
            (lateralForce * lateralForce));
        var maximum = normalForceNewtons * Specification.FrictionCoefficient;
        if (magnitude <= maximum || magnitude <= 1e-9)
        {
            return new TireForceOutput(requestedLongitudinalForceNewtons, lateralForce, false);
        }

        var scale = maximum / magnitude;
        return new TireForceOutput(
            requestedLongitudinalForceNewtons * scale,
            lateralForce * scale,
            true);
    }
}


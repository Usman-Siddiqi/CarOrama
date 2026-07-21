namespace CarOrama.Core.Vehicles.Dynamics;

public sealed class MechanicalBrakeModel(double maximumForceNewtons)
{
    public double MaximumForceNewtons { get; } = maximumForceNewtons > 0.0
        ? maximumForceNewtons
        : throw new ArgumentOutOfRangeException(nameof(maximumForceNewtons));

    public double GetForceNewtons(double command) =>
        Math.Clamp(double.IsFinite(command) ? command : 0.0, 0.0, 1.0) * MaximumForceNewtons;
}


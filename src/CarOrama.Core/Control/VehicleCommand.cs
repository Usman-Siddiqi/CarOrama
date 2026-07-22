namespace CarOrama.Core.Control;

public readonly record struct VehicleCommand
{
    private VehicleCommand(double steering, double throttle, double regenerativeBrake, double frictionBrake)
    {
        Steering = steering;
        Throttle = throttle;
        RegenerativeBrake = regenerativeBrake;
        FrictionBrake = frictionBrake;
    }

    public double Steering { get; }

    /// <summary>
    /// Signed accelerator demand: positive drives forward and negative selects reverse.
    /// </summary>
    public double Throttle { get; }

    public double RegenerativeBrake { get; }

    public double FrictionBrake { get; }

    public static VehicleCommand Neutral => new(0.0, 0.0, 0.0, 0.0);

    public static VehicleCommand Create(
        double steering,
        double throttle,
        double regenerativeBrake,
        double frictionBrake)
    {
        return new VehicleCommand(
            ClampFinite(steering, -1.0, 1.0),
            ClampFinite(throttle, -1.0, 1.0),
            ClampFinite(regenerativeBrake, 0.0, 1.0),
            ClampFinite(frictionBrake, 0.0, 1.0));
    }

    private static double ClampFinite(double value, double minimum, double maximum)
    {
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 0.0;
    }
}

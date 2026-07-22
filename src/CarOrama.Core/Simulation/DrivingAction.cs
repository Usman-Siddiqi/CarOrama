namespace CarOrama.Core.Simulation;

public readonly record struct DrivingAction
{
    public const double MinimumLongitudinalAccelerationMetersPerSecondSquared = -10.0;
    public const double MaximumLongitudinalAccelerationMetersPerSecondSquared = 5.0;

    private DrivingAction(
        long controlTick,
        double steering,
        double longitudinalAccelerationMetersPerSecondSquared)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        ControlTick = controlTick;
        Steering = steering;
        LongitudinalAccelerationMetersPerSecondSquared =
            longitudinalAccelerationMetersPerSecondSquared;
    }

    public int ContractVersion { get; }

    public long ControlTick { get; }

    public double Steering { get; }

    public double LongitudinalAccelerationMetersPerSecondSquared { get; }

    public static DrivingAction Neutral(long controlTick) => Create(controlTick, 0.0, 0.0);

    public static DrivingAction Create(
        long controlTick,
        double steering,
        double longitudinalAccelerationMetersPerSecondSquared)
    {
        if (controlTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(controlTick), "The control tick cannot be negative.");
        }

        var validatedSteering = SimulationContractValidation.RequireFinite(steering, nameof(steering));
        var validatedAcceleration = SimulationContractValidation.RequireFinite(
            longitudinalAccelerationMetersPerSecondSquared,
            nameof(longitudinalAccelerationMetersPerSecondSquared));

        return new DrivingAction(
            controlTick,
            Math.Clamp(validatedSteering, -1.0, 1.0),
            Math.Clamp(
                validatedAcceleration,
                MinimumLongitudinalAccelerationMetersPerSecondSquared,
                MaximumLongitudinalAccelerationMetersPerSecondSquared));
    }
}

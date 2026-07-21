namespace CarOrama.Core.Vehicles;

public enum DrivetrainLayout
{
    FrontWheelDrive,
    RearWheelDrive,
    AllWheelDrive,
}

public sealed record ElectricMotorSpecification
{
    public double MaximumTorqueNewtonMeters { get; init; } = 460.0;

    public double MaximumPowerWatts { get; init; } = 235_000.0;

    public double MaximumSpeedRpm { get; init; } = 18_000.0;

    public double MaximumRegenerativeTorqueNewtonMeters { get; init; } = 280.0;

    public double MaximumRegenerativePowerWatts { get; init; } = 150_000.0;

    public double ReductionRatio { get; init; } = 9.0;

    public double DriveEfficiency { get; init; } = 0.93;

    public double RegenerativeEfficiency { get; init; } = 0.78;
}

public sealed record BatterySpecification
{
    public double CapacityKilowattHours { get; init; } = 75.0;

    public double InitialStateOfCharge { get; init; } = 0.80;

    public double MaximumDischargePowerWatts { get; init; } = 310_000.0;

    public double MaximumChargePowerWatts { get; init; } = 170_000.0;

    public double ChargeEfficiency { get; init; } = 0.94;

    public double DischargeEfficiency { get; init; } = 0.96;

    public double AuxiliaryPowerWatts { get; init; } = 420.0;
}

public sealed record VehicleSpecification
{
    public double MassKilograms { get; init; } = 2_080.0;

    public double WheelRadiusMeters { get; init; } = 0.35;

    public double WheelbaseMeters { get; init; } = 2.88;

    public double TrackWidthMeters { get; init; } = 1.68;

    public double DragCoefficient { get; init; } = 0.24;

    public double FrontalAreaSquareMeters { get; init; } = 2.35;

    public double RollingResistanceCoefficient { get; init; } = 0.011;

    public double MaximumFrictionBrakeForceNewtons { get; init; } = 19_500.0;

    public double MaximumSteeringAngleDegrees { get; init; } = 32.0;

    public DrivetrainLayout DrivetrainLayout { get; init; } = DrivetrainLayout.AllWheelDrive;

    public ElectricMotorSpecification Motor { get; init; } = new();

    public BatterySpecification Battery { get; init; } = new();

    public void Validate()
    {
        if (MassKilograms <= 0.0 || WheelRadiusMeters <= 0.0 || WheelbaseMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MassKilograms), "Vehicle dimensions and mass must be positive.");
        }

        if (Motor.ReductionRatio <= 0.0 || Motor.MaximumTorqueNewtonMeters <= 0.0 || Motor.MaximumPowerWatts <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(Motor), "Motor limits and reduction ratio must be positive.");
        }

        if (Battery.CapacityKilowattHours <= 0.0 || Battery.InitialStateOfCharge is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(Battery));
        }
    }
}


namespace CarOrama.Core.Vehicles;

public enum DrivetrainLayout
{
    FrontWheelDrive,
    RearWheelDrive,
    AllWheelDrive,
}

public sealed record ElectricMotorSpecification
{
    public double MaximumTorqueNewtonMeters { get; init; } = 696.0;

    public double MaximumPowerWatts { get; init; } = 294_000.0;

    public double MaximumSpeedRpm { get; init; } = 18_000.0;

    public double MaximumRegenerativeTorqueNewtonMeters { get; init; } = 170.0;

    public double MaximumRegenerativePowerWatts { get; init; } = 100_000.0;

    public double ReductionRatio { get; init; } = 9.04;

    public double DriveEfficiency { get; init; } = 0.93;

    public double RegenerativeEfficiency { get; init; } = 0.78;
}

public sealed record BatterySpecification
{
    public double CapacityKilowattHours { get; init; } = 90.0;

    public double InitialStateOfCharge { get; init; } = 0.80;

    public double MaximumDischargePowerWatts { get; init; } = 330_000.0;

    public double MaximumChargePowerWatts { get; init; } = 120_000.0;

    public double ChargeEfficiency { get; init; } = 0.94;

    public double DischargeEfficiency { get; init; } = 0.96;

    public double AuxiliaryPowerWatts { get; init; } = 420.0;
}

public sealed record VehicleSpecification
{
    public double MassKilograms { get; init; } = 2_208.0;

    public double WheelRadiusMeters { get; init; } = 0.36;

    public double WheelbaseMeters { get; init; } = 2.99;

    public double TrackWidthMeters { get; init; } = 1.65;

    public double DragCoefficient { get; init; } = 0.29;

    public double FrontalAreaSquareMeters { get; init; } = 2.52;

    public double RollingResistanceCoefficient { get; init; } = 0.011;

    public double MaximumFrictionBrakeForceNewtons { get; init; } = 18_500.0;

    /// <summary>
    /// Dry-road tire coefficient shared by the deterministic and 3D force models.
    /// </summary>
    public double TireFrictionCoefficient { get; init; } = 1.0;

    /// <summary>
    /// Launch-control limit used to reproduce the published 0-100 km/h time
    /// without pretending the motor's peak torque is available without control limits.
    /// </summary>
    public double MaximumDriveAccelerationMetersPerSecondSquared { get; init; } = 6.3;

    public double MaximumReverseAccelerationMetersPerSecondSquared { get; init; } = 2.5;

    public double MaximumReverseSpeedMetersPerSecond { get; init; } = 8.33;

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

        if (TireFrictionCoefficient <= 0.0 ||
            MaximumDriveAccelerationMetersPerSecondSquared <= 0.0 ||
            MaximumReverseAccelerationMetersPerSecondSquared <= 0.0 ||
            MaximumReverseSpeedMetersPerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(TireFrictionCoefficient), "Tire grip and launch acceleration must be positive.");
        }
    }
}

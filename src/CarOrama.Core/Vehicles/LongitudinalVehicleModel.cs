using CarOrama.Core.Control;

namespace CarOrama.Core.Vehicles;

public readonly record struct LongitudinalVehicleState(
    double TimeSeconds,
    double DistanceMeters,
    double SpeedMetersPerSecond,
    double AccelerationMetersPerSecondSquared,
    double StateOfCharge);

/// <summary>
/// Engine-independent straight-line reference plant used for drivetrain tests
/// and repeatable calibration scenarios. The 3D vehicle uses the same drivetrain.
/// </summary>
public sealed class LongitudinalVehicleModel
{
    private const double AirDensityKilogramsPerCubicMeter = 1.225;
    private const double GravityMetersPerSecondSquared = 9.80665;
    private readonly ElectricDrivetrain _drivetrain;

    public LongitudinalVehicleModel(VehicleSpecification specification)
    {
        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        _drivetrain = new ElectricDrivetrain(specification);
        State = new LongitudinalVehicleState(0.0, 0.0, 0.0, 0.0, _drivetrain.Battery.StateOfCharge);
    }

    public VehicleSpecification Specification { get; }

    public LongitudinalVehicleState State { get; private set; }

    public LongitudinalVehicleState Step(VehicleCommand command, double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds is <= 0.0 or > 0.25)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        var wheelSpeed = State.SpeedMetersPerSecond / Specification.WheelRadiusMeters;
        var output = _drivetrain.Evaluate(command, wheelSpeed, deltaSeconds);
        var drag = 0.5 * AirDensityKilogramsPerCubicMeter * Specification.DragCoefficient
            * Specification.FrontalAreaSquareMeters * State.SpeedMetersPerSecond * State.SpeedMetersPerSecond;
        var rolling = State.SpeedMetersPerSecond > 0.05
            ? Specification.RollingResistanceCoefficient * Specification.MassKilograms * GravityMetersPerSecondSquared
            : 0.0;
        var braking = Math.Min(
            output.RegenerativeBrakeForceNewtons + output.FrictionBrakeForceNewtons,
            State.SpeedMetersPerSecond * Specification.MassKilograms / deltaSeconds);
        var netForce = output.DriveForceNewtons - drag - rolling - braking;
        var acceleration = netForce / Specification.MassKilograms;
        var speed = Math.Max(0.0, State.SpeedMetersPerSecond + (acceleration * deltaSeconds));
        var distance = State.DistanceMeters + (0.5 * (State.SpeedMetersPerSecond + speed) * deltaSeconds);

        State = new LongitudinalVehicleState(
            State.TimeSeconds + deltaSeconds,
            distance,
            speed,
            acceleration,
            _drivetrain.Battery.StateOfCharge);
        return State;
    }
}


using CarOrama.Core.Control;
using CarOrama.Core.Vehicles.Dynamics;

namespace CarOrama.Core.Vehicles;

public readonly record struct DrivetrainOutput(
    double DriveForceNewtons,
    double RegenerativeBrakeForceNewtons,
    double FrictionBrakeForceNewtons,
    double MotorTorqueNewtonMeters,
    double MotorSpeedRpm,
    double BatteryPowerWatts);

public sealed class ElectricDrivetrain
{
    private readonly ElectricMotorModel _motor;
    private readonly MechanicalBrakeModel _mechanicalBrake;

    public ElectricDrivetrain(VehicleSpecification specification)
    {
        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        specification.Validate();
        _motor = new ElectricMotorModel(specification.Motor);
        _mechanicalBrake = new MechanicalBrakeModel(specification.MaximumFrictionBrakeForceNewtons);
        Battery = new BatteryModel(specification.Battery);
    }

    public VehicleSpecification Specification { get; }

    public BatteryModel Battery { get; }

    public DrivetrainOutput Evaluate(VehicleCommand command, double wheelAngularSpeedRadiansPerSecond, double deltaSeconds)
    {
        if (!double.IsFinite(wheelAngularSpeedRadiansPerSecond) || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return default;
        }

        var motorSpeed = Math.Abs(wheelAngularSpeedRadiansPerSecond) * Specification.Motor.ReductionRatio;
        var brakingRequested = command.RegenerativeBrake > 0.0 || command.FrictionBrake > 0.0;
        var driveTorque = _motor.GetDriveTorqueNewtonMeters(motorSpeed, brakingRequested ? 0.0 : command.Throttle);
        var motorMechanicalPower = driveTorque * motorSpeed;
        var requestedDriveBatteryPower = motorMechanicalPower / Specification.Motor.DriveEfficiency;
        var acceptedDrivePower = Battery.ApplyPower(requestedDriveBatteryPower, deltaSeconds);
        if (requestedDriveBatteryPower > 1e-6)
        {
            driveTorque *= acceptedDrivePower / requestedDriveBatteryPower;
        }

        var regenerativeTorque = _motor.GetRegenerativeTorqueNewtonMeters(motorSpeed, command.RegenerativeBrake);
        var requestedRegenerativePower = -(regenerativeTorque * motorSpeed * Specification.Motor.RegenerativeEfficiency);
        var acceptedRegenerativePower = Battery.ApplyPower(requestedRegenerativePower, deltaSeconds);
        if (requestedRegenerativePower < -1e-6)
        {
            regenerativeTorque *= acceptedRegenerativePower / requestedRegenerativePower;
        }

        var auxiliaryPower = Battery.ApplyPower(Specification.Battery.AuxiliaryPowerWatts, deltaSeconds);
        var driveForce = (driveTorque * Specification.Motor.ReductionRatio * Specification.Motor.DriveEfficiency)
            / Specification.WheelRadiusMeters;
        var regenerativeForce = (regenerativeTorque * Specification.Motor.ReductionRatio)
            / Specification.WheelRadiusMeters;
        var frictionForce = _mechanicalBrake.GetForceNewtons(command.FrictionBrake);

        return new DrivetrainOutput(
            driveForce,
            regenerativeForce,
            frictionForce,
            driveTorque,
            _motor.GetRpm(motorSpeed),
            acceptedDrivePower + acceptedRegenerativePower + auxiliaryPower);
    }
}

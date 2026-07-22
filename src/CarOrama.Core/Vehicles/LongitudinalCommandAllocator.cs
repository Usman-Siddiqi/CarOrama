using CarOrama.Core.Control;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Vehicles;

/// <summary>
/// Converts a controller's requested longitudinal acceleration into accelerator,
/// regenerative-brake, and friction-brake actuator demands. Controllers remain
/// independent of the available regeneration at a particular speed or pack state.
/// </summary>
public sealed class LongitudinalCommandAllocator
{
    private readonly VehicleSpecification _specification;
    private readonly ElectricMotorModel _motor;

    public LongitudinalCommandAllocator(VehicleSpecification specification)
    {
        _specification = specification ?? throw new ArgumentNullException(nameof(specification));
        specification.Validate();
        _motor = new ElectricMotorModel(specification.Motor);
    }

    public VehicleCommand Allocate(
        DrivingAction action,
        double speedMetersPerSecond,
        double batteryStateOfCharge)
    {
        if (!double.IsFinite(speedMetersPerSecond) || speedMetersPerSecond < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond));
        }

        if (!double.IsFinite(batteryStateOfCharge) || batteryStateOfCharge is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(batteryStateOfCharge));
        }

        var requestedAcceleration = action.LongitudinalAccelerationMetersPerSecondSquared;
        if (requestedAcceleration >= 0.0)
        {
            return VehicleCommand.Create(
                action.Steering,
                requestedAcceleration / _specification.MaximumDriveAccelerationMetersPerSecondSquared,
                regenerativeBrake: 0.0,
                frictionBrake: 0.0);
        }

        var requestedBrakeForce = Math.Min(
            -requestedAcceleration * _specification.MassKilograms,
            _specification.TireFrictionCoefficient * _specification.MassKilograms * 9.80665);
        var maximumRegenerativeForce = GetMaximumRegenerativeForce(
            speedMetersPerSecond,
            batteryStateOfCharge);
        var regenerativeForce = Math.Min(requestedBrakeForce, maximumRegenerativeForce);
        var remainingFrictionForce = Math.Max(0.0, requestedBrakeForce - regenerativeForce);

        return VehicleCommand.Create(
            action.Steering,
            throttle: 0.0,
            maximumRegenerativeForce > 1e-9 ? regenerativeForce / maximumRegenerativeForce : 0.0,
            remainingFrictionForce / _specification.MaximumFrictionBrakeForceNewtons);
    }

    public double GetMaximumRegenerativeForce(
        double speedMetersPerSecond,
        double batteryStateOfCharge)
    {
        if (!double.IsFinite(speedMetersPerSecond) || speedMetersPerSecond < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond));
        }

        if (!double.IsFinite(batteryStateOfCharge) || batteryStateOfCharge is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(batteryStateOfCharge));
        }

        if (batteryStateOfCharge >= 1.0 - 1e-9)
        {
            return 0.0;
        }

        var wheelAngularSpeed = speedMetersPerSecond / _specification.WheelRadiusMeters;
        var motorAngularSpeed = wheelAngularSpeed * _specification.Motor.ReductionRatio;
        var torque = _motor.GetRegenerativeTorqueNewtonMeters(motorAngularSpeed, command: 1.0);
        return torque * _specification.Motor.ReductionRatio / _specification.WheelRadiusMeters;
    }
}

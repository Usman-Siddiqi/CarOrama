namespace CarOrama.Core.Vehicles;

public sealed class ElectricMotorModel(ElectricMotorSpecification specification)
{
    private const double RadiansPerSecondToRpm = 60.0 / (2.0 * Math.PI);

    public ElectricMotorSpecification Specification { get; } = specification ?? throw new ArgumentNullException(nameof(specification));

    public double GetDriveTorqueNewtonMeters(double motorAngularSpeedRadiansPerSecond, double throttle)
    {
        var speed = Math.Abs(motorAngularSpeedRadiansPerSecond);
        var requested = Math.Clamp(double.IsFinite(throttle) ? throttle : 0.0, 0.0, 1.0);
        if (requested <= 0.0 || GetRpm(speed) >= Specification.MaximumSpeedRpm)
        {
            return 0.0;
        }

        var powerLimitedTorque = speed > 1.0
            ? Specification.MaximumPowerWatts / speed
            : Specification.MaximumTorqueNewtonMeters;
        return requested * Math.Min(Specification.MaximumTorqueNewtonMeters, powerLimitedTorque);
    }

    public double GetRegenerativeTorqueNewtonMeters(double motorAngularSpeedRadiansPerSecond, double command)
    {
        var speed = Math.Abs(motorAngularSpeedRadiansPerSecond);
        var requested = Math.Clamp(double.IsFinite(command) ? command : 0.0, 0.0, 1.0);
        if (requested <= 0.0 || speed < 2.0 || GetRpm(speed) >= Specification.MaximumSpeedRpm)
        {
            return 0.0;
        }

        var powerLimitedTorque = Specification.MaximumRegenerativePowerWatts / speed;
        var torque = Math.Min(Specification.MaximumRegenerativeTorqueNewtonMeters, powerLimitedTorque);

        // Regeneration fades below roughly 5 km/h, where friction brakes provide predictable stopping.
        var lowSpeedBlend = Math.Clamp(speed / 35.0, 0.0, 1.0);
        return requested * torque * lowSpeedBlend;
    }

    public double GetRpm(double motorAngularSpeedRadiansPerSecond) =>
        Math.Abs(motorAngularSpeedRadiansPerSecond) * RadiansPerSecondToRpm;
}


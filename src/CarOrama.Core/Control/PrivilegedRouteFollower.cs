using CarOrama.Core.Geometry;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;

namespace CarOrama.Core.Control;

public sealed record PrivilegedRouteFollowerConfig
{
    public double SpeedControlGain { get; init; } = 0.9;

    public double MaximumAccelerationMetersPerSecondSquared { get; init; } = 3.0;

    public double ComfortableDecelerationMetersPerSecondSquared { get; init; } = 3.5;

    public double StopLineBufferMeters { get; init; } = 1.5;

    public double StopSpeedThresholdMetersPerSecond { get; init; } = 0.15;

    public int StopHoldControlTicks { get; init; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(SpeedControlGain) || SpeedControlGain <= 0.0 ||
            !double.IsFinite(MaximumAccelerationMetersPerSecondSquared) ||
            MaximumAccelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(ComfortableDecelerationMetersPerSecondSquared) ||
            ComfortableDecelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(StopLineBufferMeters) || StopLineBufferMeters < 0.0 ||
            !double.IsFinite(StopSpeedThresholdMetersPerSecond) || StopSpeedThresholdMetersPerSecond < 0.0 ||
            StopHoldControlTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PrivilegedRouteFollowerConfig));
        }
    }
}

/// <summary>
/// Deterministic pure-pursuit and speed-control baseline that consumes only the
/// public privileged observation contract. It is an evaluation reference and
/// demonstration source, not an autonomous-driving policy.
/// </summary>
public sealed class PrivilegedRouteFollower
{
    private readonly VehicleSpecification _vehicleSpecification;
    private readonly PrivilegedRouteFollowerConfig _config;
    private readonly HashSet<string> _servedStopSigns = new(StringComparer.Ordinal);
    private string? _heldStopSignId;
    private int _heldStopTicks;

    public PrivilegedRouteFollower(
        VehicleSpecification? vehicleSpecification = null,
        PrivilegedRouteFollowerConfig? config = null)
    {
        _vehicleSpecification = vehicleSpecification ?? new VehicleSpecification();
        _vehicleSpecification.Validate();
        _config = config ?? new PrivilegedRouteFollowerConfig();
        _config.Validate();
    }

    public void Reset()
    {
        _servedStopSigns.Clear();
        _heldStopSignId = null;
        _heldStopTicks = 0;
    }

    public DrivingAction GetAction(PrivilegedObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ContractVersion != SimulationContract.CurrentVersion)
        {
            throw new ArgumentException("The observation contract version is not supported.", nameof(observation));
        }

        UpdateStopSignHold(observation);
        var steering = CalculateSteering(observation);
        var targetSpeed = CalculateTargetSpeed(observation);
        var requestedAcceleration = Math.Clamp(
            (targetSpeed - observation.EgoVehicle.SpeedMetersPerSecond) * _config.SpeedControlGain,
            -_config.ComfortableDecelerationMetersPerSecondSquared,
            _config.MaximumAccelerationMetersPerSecondSquared);
        return DrivingAction.Create(observation.ControlTick, steering, requestedAcceleration);
    }

    private double CalculateSteering(PrivilegedObservation observation)
    {
        var heading = observation.EgoVehicle.HeadingRadians;
        var forward = new Vector2D(Math.Cos(heading), Math.Sin(heading));
        var right = forward.PerpendicularLeft() * -1.0;
        var toLookahead = observation.Route.LookaheadPoint - observation.EgoVehicle.WorldPosition;
        var distanceSquared = Vector2D.Dot(toLookahead, toLookahead);
        if (distanceSquared <= 1e-9)
        {
            return 0.0;
        }

        var lateralRightMeters = Vector2D.Dot(toLookahead, right);
        var curvature = 2.0 * lateralRightMeters / distanceSquared;
        var steeringAngle = Math.Atan(_vehicleSpecification.WheelbaseMeters * curvature);
        var maximumSteeringAngle = _vehicleSpecification.MaximumSteeringAngleDegrees * Math.PI / 180.0;
        return Math.Clamp(steeringAngle / maximumSteeringAngle, -1.0, 1.0);
    }

    private double CalculateTargetSpeed(PrivilegedObservation observation)
    {
        var targetSpeed = observation.Lane.SpeedLimitMetersPerSecond;
        targetSpeed = Math.Min(
            targetSpeed,
            SpeedForStoppingDistance(observation.Route.RemainingDistanceMeters, bufferMeters: 0.0));

        var control = observation.UpcomingTrafficControl;
        if (control.DistanceToStopLineMeters is { } distance && RequiresStop(control))
        {
            targetSpeed = Math.Min(targetSpeed, SpeedForStoppingDistance(distance, _config.StopLineBufferMeters));
        }

        return targetSpeed;
    }

    private bool RequiresStop(UpcomingTrafficControlObservation control)
    {
        if (control.Kind == ObservedTrafficControlKind.StopSign)
        {
            return control.TrafficControlId is not null && !_servedStopSigns.Contains(control.TrafficControlId);
        }

        return control.Kind == ObservedTrafficControlKind.TrafficSignal &&
            control.State is ObservedTrafficControlState.Red or
                ObservedTrafficControlState.Yellow or
                ObservedTrafficControlState.Unknown;
    }

    private void UpdateStopSignHold(PrivilegedObservation observation)
    {
        var control = observation.UpcomingTrafficControl;
        var controlId = control.Kind == ObservedTrafficControlKind.StopSign
            ? control.TrafficControlId
            : null;
        if (controlId is null || _servedStopSigns.Contains(controlId) ||
            control.DistanceToStopLineMeters > _config.StopLineBufferMeters + 0.25 ||
            observation.EgoVehicle.SpeedMetersPerSecond > _config.StopSpeedThresholdMetersPerSecond)
        {
            _heldStopSignId = null;
            _heldStopTicks = 0;
            return;
        }

        if (!string.Equals(_heldStopSignId, controlId, StringComparison.Ordinal))
        {
            _heldStopSignId = controlId;
            _heldStopTicks = 0;
        }

        _heldStopTicks++;
        if (_heldStopTicks < _config.StopHoldControlTicks)
        {
            return;
        }

        _servedStopSigns.Add(controlId);
        _heldStopSignId = null;
        _heldStopTicks = 0;
    }

    private double SpeedForStoppingDistance(double distanceMeters, double bufferMeters)
    {
        var usableDistance = Math.Max(0.0, distanceMeters - bufferMeters);
        return Math.Sqrt(2.0 * _config.ComfortableDecelerationMetersPerSecondSquared * usableDistance);
    }
}

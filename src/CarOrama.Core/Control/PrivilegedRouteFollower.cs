using CarOrama.Core.Geometry;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;

namespace CarOrama.Core.Control;

public sealed record PrivilegedRouteFollowerConfig
{
    public double SpeedControlGain { get; init; } = 0.9;

    public double MaximumAccelerationMetersPerSecondSquared { get; init; } = 3.0;

    public double MaximumCruiseSpeedMetersPerSecond { get; init; } = 10.5;

    public double ComfortableDecelerationMetersPerSecondSquared { get; init; } = 3.5;

    public double MaximumCorneringAccelerationMetersPerSecondSquared { get; init; } = 2.4;

    public double CrossTrackGain { get; init; } = 1.2;

    public double HeadingErrorGain { get; init; } = 0.65;

    public double CrossTrackSofteningSpeedMetersPerSecond { get; init; } = 2.0;

    public double StopLineBufferMeters { get; init; } = 1.5;

    public double StopSpeedThresholdMetersPerSecond { get; init; } = 0.15;

    public int StopHoldControlTicks { get; init; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(SpeedControlGain) || SpeedControlGain <= 0.0 ||
            !double.IsFinite(MaximumAccelerationMetersPerSecondSquared) ||
            MaximumAccelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(MaximumCruiseSpeedMetersPerSecond) ||
            MaximumCruiseSpeedMetersPerSecond <= 0.0 ||
            !double.IsFinite(ComfortableDecelerationMetersPerSecondSquared) ||
            ComfortableDecelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(MaximumCorneringAccelerationMetersPerSecondSquared) ||
            MaximumCorneringAccelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(CrossTrackGain) || CrossTrackGain < 0.0 ||
            !double.IsFinite(HeadingErrorGain) || HeadingErrorGain < 0.0 ||
            !double.IsFinite(CrossTrackSofteningSpeedMetersPerSecond) ||
            CrossTrackSofteningSpeedMetersPerSecond <= 0.0 ||
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
        var targetSpeed = CalculateTargetSpeed(observation, steering);
        var requestedAcceleration = Math.Clamp(
            (targetSpeed - observation.EgoVehicle.SpeedMetersPerSecond) * _config.SpeedControlGain,
            -_config.ComfortableDecelerationMetersPerSecondSquared,
            _config.MaximumAccelerationMetersPerSecondSquared);
        var control = observation.UpcomingTrafficControl;
        if (control.DistanceToStopLineMeters is { } distance && RequiresStop(control))
        {
            var stoppingEnvelopeSpeed = SpeedForStoppingDistance(distance, _config.StopLineBufferMeters);
            if (observation.EgoVehicle.SpeedMetersPerSecond > stoppingEnvelopeSpeed + 0.05)
            {
                // Once outside the comfortable stopping envelope, proportional
                // speed control is too gentle; request the configured full
                // deceleration so the reference driver cannot roll the line.
                requestedAcceleration = -_config.ComfortableDecelerationMetersPerSecondSquared;
            }
        }

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
        steeringAngle -= observation.Lane.HeadingErrorRadians * _config.HeadingErrorGain;
        steeringAngle += Math.Atan(
            _config.CrossTrackGain * observation.Lane.LateralOffsetMeters /
            (observation.EgoVehicle.SpeedMetersPerSecond +
                _config.CrossTrackSofteningSpeedMetersPerSecond));
        var maximumSteeringAngle = _vehicleSpecification.MaximumSteeringAngleDegrees * Math.PI / 180.0;
        return Math.Clamp(steeringAngle / maximumSteeringAngle, -1.0, 1.0);
    }

    private double CalculateTargetSpeed(PrivilegedObservation observation, double steering)
    {
        // The observation contract currently exposes the active lane's limit,
        // not a preview of a lower limit after the next junction. A conservative
        // cap keeps reference demonstrations legal across those transitions.
        var targetSpeed = Math.Min(
            observation.Lane.SpeedLimitMetersPerSecond,
            _config.MaximumCruiseSpeedMetersPerSecond);
        var steeringAngle = steering * _vehicleSpecification.MaximumSteeringAngleDegrees * Math.PI / 180.0;
        var requestedCurvature = Math.Abs(Math.Tan(steeringAngle) / _vehicleSpecification.WheelbaseMeters);
        if (requestedCurvature > 1e-6)
        {
            targetSpeed = Math.Min(
                targetSpeed,
                Math.Sqrt(_config.MaximumCorneringAccelerationMetersPerSecondSquared / requestedCurvature));
        }

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

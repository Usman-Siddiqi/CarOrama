using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;

namespace CarOrama.Core.Control;

public sealed record PrivilegedRouteFollowerConfig
{
    public double SpeedControlGain { get; init; } = 0.9;

    public double MaximumAccelerationMetersPerSecondSquared { get; init; } = 3.0;

    /// <summary>Maximum combined longitudinal/lateral acceleration in g.</summary>
    public double MaximumCombinedAccelerationG { get; init; } = 0.32;

    public double MaximumCruiseSpeedMetersPerSecond { get; init; } = 16.3;

    public double SpeedLimitMarginMetersPerSecond { get; init; } = 0.4;

    public double CruiseSpeedVariationMetersPerSecond { get; init; } = 0.7;

    public long DriverProfileSeed { get; init; }

    public double ComfortableDecelerationMetersPerSecondSquared { get; init; } = 3.8;

    public double MaximumCorneringAccelerationMetersPerSecondSquared { get; init; } = 2.2;

    public double CrossTrackGain { get; init; } = 1.2;

    public double HeadingErrorGain { get; init; } = 0.65;

    public double CrossTrackSofteningSpeedMetersPerSecond { get; init; } = 2.0;

    public double TargetStoppingDecelerationMetersPerSecondSquared { get; init; } = 3.0;

    public double MaximumLongitudinalJerkMetersPerSecondCubed { get; init; } = 3.5;

    public int ControlTicksPerSecond { get; init; } = 20;

    public double StopLineBufferMeters { get; init; } = 1.5;

    public double StopSpeedThresholdMetersPerSecond { get; init; } = 0.15;

    public int StopHoldControlTicks { get; init; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(SpeedControlGain) || SpeedControlGain <= 0.0 ||
            !double.IsFinite(MaximumAccelerationMetersPerSecondSquared) ||
            MaximumAccelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(MaximumCombinedAccelerationG) ||
            MaximumCombinedAccelerationG <= 0.0 ||
            !double.IsFinite(MaximumCruiseSpeedMetersPerSecond) ||
            MaximumCruiseSpeedMetersPerSecond <= 0.0 ||
            !double.IsFinite(SpeedLimitMarginMetersPerSecond) ||
            SpeedLimitMarginMetersPerSecond < 0.0 ||
            !double.IsFinite(CruiseSpeedVariationMetersPerSecond) ||
            CruiseSpeedVariationMetersPerSecond < 0.0 ||
            !double.IsFinite(ComfortableDecelerationMetersPerSecondSquared) ||
            ComfortableDecelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(MaximumCorneringAccelerationMetersPerSecondSquared) ||
            MaximumCorneringAccelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(TargetStoppingDecelerationMetersPerSecondSquared) ||
            TargetStoppingDecelerationMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(MaximumLongitudinalJerkMetersPerSecondCubed) ||
            MaximumLongitudinalJerkMetersPerSecondCubed <= 0.0 ||
            ControlTicksPerSecond <= 0 ||
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
    private double _previousRequestedAcceleration;
    private TurnSignalState _turnSignal;
    private int _turnSignalClearTicks;
    private string? _activeStopControlId;

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
        _previousRequestedAcceleration = 0.0;
        _turnSignal = TurnSignalState.Off;
        _turnSignalClearTicks = 0;
        _activeStopControlId = null;
    }

    public DrivingAction GetAction(PrivilegedObservation observation) => GetAction(observation, route: null);

    public DrivingAction GetAction(PrivilegedObservation observation, DrivingRoute? route)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ContractVersion != SimulationContract.CurrentVersion)
        {
            throw new ArgumentException("The observation contract version is not supported.", nameof(observation));
        }

        UpdateStopSignHold(observation);
        var steering = CalculateSteering(observation, route);
        var targetSpeed = CalculateTargetSpeed(observation, steering, route);
        var speed = observation.EgoVehicle.SpeedMetersPerSecond;
        var requestedAcceleration = Math.Clamp(
            (targetSpeed - speed) * _config.SpeedControlGain,
            -_config.ComfortableDecelerationMetersPerSecondSquared,
            _config.MaximumAccelerationMetersPerSecondSquared);
        var control = observation.UpcomingTrafficControl;
        var hadActiveStopControl = _activeStopControlId is not null;
        var stopControlId = control.TrafficControlId;
        var requiresStop = control.DistanceToStopLineMeters is not null && RequiresStop(control);
        if (requiresStop && stopControlId is not null &&
            control.DistanceToStopLineMeters is { } distance &&
            (_activeStopControlId == stopControlId ||
                IsWithinStoppingEnvelope(distance, speed)))
        {
            _activeStopControlId = stopControlId;
            var usableDistance = Math.Max(0.1, distance - StoppingBufferMeters(speed));
            var requiredDeceleration = -(speed * speed) / (2.0 * usableDistance);
            requestedAcceleration = Math.Min(
                requestedAcceleration,
                Math.Clamp(
                    requiredDeceleration,
                    -_config.ComfortableDecelerationMetersPerSecondSquared,
                    0.0));
        }
        else
        {
            if (hadActiveStopControl && !requiresStop)
            {
                // A green signal releases a pending stop immediately. The speed
                // controller then ramps forward from zero without another brake tick.
                _previousRequestedAcceleration = 0.0;
            }

            _activeStopControlId = null;
        }

        requestedAcceleration = ApplyCombinedGForceLimit(observation, steering, route, requestedAcceleration);

        var maximumAccelerationChange =
            _config.MaximumLongitudinalJerkMetersPerSecondCubed / _config.ControlTicksPerSecond;
        requestedAcceleration = Math.Clamp(
            requestedAcceleration,
            _previousRequestedAcceleration - maximumAccelerationChange,
            _previousRequestedAcceleration + maximumAccelerationChange);
        _previousRequestedAcceleration = requestedAcceleration;

        return DrivingAction.Create(observation.ControlTick, steering, requestedAcceleration);
    }

    private double ApplyCombinedGForceLimit(
        PrivilegedObservation observation,
        double steering,
        DrivingRoute? route,
        double requestedAcceleration)
    {
        const double gravityMetersPerSecondSquared = 9.80665;
        var lateralAcceleration = route is null
            ? EstimateSteeringLateralAcceleration(observation, steering)
            : observation.EgoVehicle.SpeedMetersPerSecond * observation.EgoVehicle.SpeedMetersPerSecond *
                EstimateRouteCurvature(route, observation.Route.DistanceTravelledMeters, 1.5);
        var maximumAcceleration = _config.MaximumCombinedAccelerationG * gravityMetersPerSecondSquared;
        if (lateralAcceleration >= maximumAcceleration)
        {
            // The comfort envelope must never prevent the controller from
            // slowing an already overspeeding corner. Block propulsion while
            // saturated, but retain the requested braking needed to recover.
            return Math.Min(0.0, requestedAcceleration);
        }

        var longitudinalBudget = Math.Sqrt(
            (maximumAcceleration * maximumAcceleration) -
            (lateralAcceleration * lateralAcceleration));
        return Math.Clamp(requestedAcceleration, -longitudinalBudget, longitudinalBudget);
    }

    private double EstimateSteeringLateralAcceleration(
        PrivilegedObservation observation,
        double steering)
    {
        var steeringAngle = steering * _vehicleSpecification.MaximumSteeringAngleDegrees * Math.PI / 180.0;
        var curvature = Math.Abs(Math.Tan(steeringAngle) / _vehicleSpecification.WheelbaseMeters);
        var speed = observation.EgoVehicle.SpeedMetersPerSecond;
        return speed * speed * curvature;
    }
    public VehicleLightingCommand GetLightingCommand(
        PrivilegedObservation observation,
        DrivingRoute route)
    {
        const double signalLookaheadMeters = 30.0;
        const double minimumTurnAngleRadians = 0.30;
        const int cancellationTicks = 10;
        var progress = observation.Route.DistanceTravelledMeters;
        var currentDirection = route.GetDirectionAtDistance(progress);
        var futureDirection = route.GetDirectionAtDistance(progress + signalLookaheadMeters);
        var cross = (currentDirection.X * futureDirection.Y) -
            (currentDirection.Y * futureDirection.X);
        var dot = Vector2D.Dot(currentDirection, futureDirection);
        var turnAngle = Math.Atan2(Math.Abs(cross), dot);
        if (turnAngle >= minimumTurnAngleRadians)
        {
            // Road-plane Y points south, so a positive 2D cross product is a
            // clockwise (right) turn in the simulator's coordinate system.
            _turnSignal = cross > 0.0 ? TurnSignalState.Right : TurnSignalState.Left;
            _turnSignalClearTicks = 0;
        }
        else if (_turnSignal != TurnSignalState.Off && ++_turnSignalClearTicks >= cancellationTicks)
        {
            _turnSignal = TurnSignalState.Off;
            _turnSignalClearTicks = 0;
        }

        return VehicleLightingCommand.Create(
            headlightsEnabled: true,
            _turnSignal,
            hazardLightsEnabled: false);
    }
    private double CalculateSteering(PrivilegedObservation observation, DrivingRoute? route)
    {
        var heading = observation.EgoVehicle.HeadingRadians;
        var forward = new Vector2D(Math.Cos(heading), Math.Sin(heading));
        var right = forward.PerpendicularLeft() * -1.0;
        var lookaheadPoint = observation.Route.LookaheadPoint;
        if (route is not null)
        {
            var lookaheadMeters = Math.Clamp(
                2.5 + (observation.EgoVehicle.SpeedMetersPerSecond * 0.30),
                3.5,
                7.5);
            lookaheadPoint = route.GetPointAtDistance(
                observation.Route.DistanceTravelledMeters + lookaheadMeters);
        }

        var toLookahead = lookaheadPoint - observation.EgoVehicle.WorldPosition;
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

    private double CalculateCruiseSpeed(PrivilegedObservation observation)
    {
        var speedLimit = observation.Lane.SpeedLimitMetersPerSecond;
        var baseTarget = Math.Min(
            _config.MaximumCruiseSpeedMetersPerSecond,
            Math.Max(0.1, speedLimit - _config.SpeedLimitMarginMetersPerSecond));
        if (_config.CruiseSpeedVariationMetersPerSecond <= 1e-9)
        {
            return baseTarget;
        }

        // A pair of long, incommensurate waves produces smooth human-like
        // variation without injecting frame-to-frame noise into training labels.
        // The profile phase is stable for a given scenario seed.
        var timeSeconds = observation.ControlTick / (double)_config.ControlTicksPerSecond;
        var phase = SeedPhaseRadians(_config.DriverProfileSeed);
        var normalizedVariation =
            (0.65 * Math.Sin((2.0 * Math.PI * timeSeconds / 19.0) + phase)) +
            (0.35 * Math.Sin((2.0 * Math.PI * timeSeconds / 43.0) + (phase * 1.61803398875)));
        return Math.Clamp(
            baseTarget + (_config.CruiseSpeedVariationMetersPerSecond * normalizedVariation),
            0.1,
            speedLimit);
    }

    private static double SeedPhaseRadians(long seed)
    {
        unchecked
        {
            var value = (ulong)seed + 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            var unit = (value >> 11) * (1.0 / (1UL << 53));
            return unit * 2.0 * Math.PI;
        }
    }
    private double CalculateTargetSpeed(
        PrivilegedObservation observation,
        double steering,
        DrivingRoute? route)
    {
        // The observation contract currently exposes the active lane's limit,
        // not a preview of a lower limit after the next junction. A conservative
        // cap keeps reference demonstrations legal across those transitions.
        var cruiseTargetSpeed = CalculateCruiseSpeed(observation);
        var targetSpeed = cruiseTargetSpeed;
        if (route is not null)
        {
            targetSpeed = Math.Min(targetSpeed, CalculateCurveApproachSpeed(observation, route));
            if (CalculateOpeningCurveSpeed(observation, route) is { } openingCurveSpeed)
            {
                // Once the corner is opening, recover speed with the available
                // radius instead of obeying the slowest curvature still visible
                // at the far end of the preview window.
                targetSpeed = Math.Max(
                    targetSpeed,
                    Math.Min(cruiseTargetSpeed, openingCurveSpeed));
            }
        }

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
            targetSpeed = Math.Min(
                targetSpeed,
                SpeedForStoppingDistance(
                    distance,
                    StoppingBufferMeters(observation.EgoVehicle.SpeedMetersPerSecond)));
        }

        return targetSpeed;
    }

    private double CalculateCurveApproachSpeed(PrivilegedObservation observation, DrivingRoute route)
    {
        const double previewStartMeters = 4.0;
        const double previewEndMeters = 62.0;
        const double previewStepMeters = 2.0;
        const double curvatureWindowMeters = 2.0;
        var progress = observation.Route.DistanceTravelledMeters;
        var approachSpeed = double.PositiveInfinity;
        for (var offset = previewStartMeters; offset <= previewEndMeters; offset += previewStepMeters)
        {
            var sampleDistance = progress + offset;
            var before = route.GetDirectionAtDistance(sampleDistance - (curvatureWindowMeters * 0.5));
            var after = route.GetDirectionAtDistance(sampleDistance + (curvatureWindowMeters * 0.5));
            var dot = Math.Clamp(Vector2D.Dot(before, after), -1.0, 1.0);
            var headingChange = Math.Acos(dot);
            var curvature = headingChange / curvatureWindowMeters;
            if (curvature <= 1e-5)
            {
                continue;
            }

            var safeCurveSpeed = Math.Sqrt(
                _config.MaximumCorneringAccelerationMetersPerSecondSquared / curvature);
            var decelerationRampSeconds = _config.ComfortableDecelerationMetersPerSecondSquared /
                _config.MaximumLongitudinalJerkMetersPerSecondCubed;
            var usableBrakingDistance = Math.Max(
                0.0,
                offset - (observation.EgoVehicle.SpeedMetersPerSecond * decelerationRampSeconds));
            var speedThatCanBrakeToCurve = Math.Sqrt(
                (safeCurveSpeed * safeCurveSpeed) +
                (2.0 * _config.ComfortableDecelerationMetersPerSecondSquared * usableBrakingDistance));
            approachSpeed = Math.Min(approachSpeed, speedThatCanBrakeToCurve);
        }

        return approachSpeed;
    }
    private double? CalculateOpeningCurveSpeed(PrivilegedObservation observation, DrivingRoute route)
    {
        const double currentSampleWindowMeters = 1.5;
        const double aheadDistanceMeters = 3.0;
        var currentCurvature = EstimateRouteCurvature(
            route,
            observation.Route.DistanceTravelledMeters,
            currentSampleWindowMeters);
        var aheadCurvature = EstimateRouteCurvature(
            route,
            observation.Route.DistanceTravelledMeters + aheadDistanceMeters,
            currentSampleWindowMeters);
        if (currentCurvature <= 1e-5 || aheadCurvature >= currentCurvature * 0.85)
        {
            return null;
        }

        return Math.Sqrt(
            _config.MaximumCorneringAccelerationMetersPerSecondSquared / currentCurvature);
    }

    private static double EstimateRouteCurvature(
        DrivingRoute route,
        double distanceMeters,
        double sampleWindowMeters)
    {
        var before = route.GetDirectionAtDistance(distanceMeters - (sampleWindowMeters * 0.5));
        var after = route.GetDirectionAtDistance(distanceMeters + (sampleWindowMeters * 0.5));
        var dot = Math.Clamp(Vector2D.Dot(before, after), -1.0, 1.0);
        return Math.Acos(dot) / sampleWindowMeters;
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
            control.DistanceToStopLineMeters > _config.StopLineBufferMeters + 0.4 ||
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
        _previousRequestedAcceleration = 0.0;
    }

    private bool IsWithinStoppingEnvelope(double distanceMeters, double speedMetersPerSecond)
    {
        var brakingDistance = speedMetersPerSecond * speedMetersPerSecond /
            (2.0 * _config.TargetStoppingDecelerationMetersPerSecondSquared);
        return distanceMeters <= StoppingBufferMeters(speedMetersPerSecond) + brakingDistance + 1.0;
    }

    private double StoppingBufferMeters(double speedMetersPerSecond)
    {
        // A jerk-limited controller cannot reach its target deceleration
        // instantly. Reserve half of the ramp distance so braking begins early
        // without moving the final stationary position away from the stop line.
        var rampSeconds = _config.TargetStoppingDecelerationMetersPerSecondSquared /
            _config.MaximumLongitudinalJerkMetersPerSecondCubed;
        return _config.StopLineBufferMeters + (0.5 * speedMetersPerSecond * rampSeconds);
    }

    private double SpeedForStoppingDistance(double distanceMeters, double bufferMeters)
    {
        var usableDistance = Math.Max(0.0, distanceMeters - bufferMeters);
        return Math.Sqrt(2.0 * _config.TargetStoppingDecelerationMetersPerSecondSquared * usableDistance);
    }
}

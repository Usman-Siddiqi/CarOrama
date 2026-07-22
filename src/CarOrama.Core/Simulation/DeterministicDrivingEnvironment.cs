using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Vehicles;

namespace CarOrama.Core.Simulation;

/// <summary>
/// Faster-than-real-time reference environment for protocol, route-query, reward,
/// and baseline-controller development. It uses the production EV drivetrain with
/// deterministic bicycle-model planar motion; the Godot rigid-body adapter remains
/// the high-fidelity environment for suspension, tire, and collision evaluation.
/// </summary>
public sealed class DeterministicDrivingEnvironment : ISimulationEnvironment
{
    private const double RouteCompletionToleranceMeters = 0.5;
    private const double ProgressProjectionBacktrackMeters = 2.0;
    private const double ProgressProjectionForwardWindowMeters = 20.0;
    private const double ReferenceLookaheadMeters = 4.0;
    private readonly RoadNetwork _network;
    private readonly DrivingRoute _route;
    private readonly VehicleSpecification _vehicleSpecification;
    private readonly RoadGroundTruthQuery _groundTruthQuery;
    private readonly RoadRuleMetricsMonitor _ruleMetrics;
    private readonly LongitudinalCommandAllocator _commandAllocator;
    private LongitudinalVehicleModel? _vehicle;
    private PrivilegedObservation? _observation;
    private EpisodeTermination _termination = EpisodeTermination.Ongoing;
    private Vector2D _position;
    private double _headingRadians;
    private double _yawRateRadiansPerSecond;
    private double _steering;
    private double _routeProgressMeters;
    private double _previousControlAcceleration;
    private double _absoluteJerkSum;
    private int _jerkSampleCount;
    private long _controlTick;
    private long _physicsTick;

    public DeterministicDrivingEnvironment(
        RoadNetwork network,
        DrivingRoute route,
        VehicleSpecification? vehicleSpecification = null)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _vehicleSpecification = vehicleSpecification ?? new VehicleSpecification();
        _vehicleSpecification.Validate();
        _groundTruthQuery = new RoadGroundTruthQuery(network, route);
        _ruleMetrics = new RoadRuleMetricsMonitor(network, route);
        _commandAllocator = new LongitudinalCommandAllocator(_vehicleSpecification);
    }

    public SimulationScenario? ActiveScenario { get; private set; }

    public PrivilegedObservation Reset(EpisodeResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Seed != _network.Seed)
        {
            throw new ArgumentException("The reset seed does not match this environment's road network.", nameof(request));
        }

        if (!string.Equals(request.RouteId, _route.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("The requested route is not registered in this environment.", nameof(request));
        }

        var spawn = _network.SpawnPoints.SingleOrDefault(candidate => candidate.Id == request.SpawnPointId) ??
            throw new ArgumentException("The requested spawn point does not exist.", nameof(request));
        if (!string.Equals(spawn.LaneId, _route.LaneIds[0], StringComparison.Ordinal))
        {
            throw new ArgumentException("The spawn point must lie on the route's first lane.", nameof(request));
        }

        if (request.Scenario.FixedPhysicsDeltaSeconds > 0.25)
        {
            throw new ArgumentException("The reference EV plant requires at least four physics ticks per second.", nameof(request));
        }

        ActiveScenario = request.Scenario;
        _vehicle = new LongitudinalVehicleModel(_vehicleSpecification);
        _position = spawn.Position;
        _headingRadians = Math.Atan2(spawn.Forward.Y, spawn.Forward.X);
        _yawRateRadiansPerSecond = 0.0;
        _steering = 0.0;
        _previousControlAcceleration = 0.0;
        _absoluteJerkSum = 0.0;
        _jerkSampleCount = 0;
        _controlTick = request.InitialControlTick;
        _physicsTick = request.InitialPhysicsTick;
        _termination = EpisodeTermination.Ongoing;

        var groundTruth = _groundTruthQuery.Observe(
            _position,
            _headingRadians,
            lookaheadDistanceMeters: ReferenceLookaheadMeters);
        _routeProgressMeters = groundTruth.ProgressMeters;
        _ruleMetrics.Reset(groundTruth.ProgressMeters, groundTruth.IsLaneDeparture);
        _observation = CreateObservation(groundTruth);
        return _observation;
    }

    public PrivilegedObservation Observe()
    {
        return _observation ?? throw new InvalidOperationException("Reset must be called before observing.");
    }

    public EpisodeStepResult Step(DrivingAction action)
    {
        var scenario = ActiveScenario ?? throw new InvalidOperationException("Reset must be called before stepping.");
        var vehicle = _vehicle ?? throw new InvalidOperationException("The vehicle plant is unavailable.");
        if (_termination.IsTerminal)
        {
            throw new InvalidOperationException("Reset is required after an episode terminates.");
        }

        if (action.ContractVersion != SimulationContract.CurrentVersion || action.ControlTick != _controlTick)
        {
            throw new ArgumentException(
                $"Expected an action for control tick {_controlTick} using contract v{SimulationContract.CurrentVersion}.",
                nameof(action));
        }

        var previousProgress = _routeProgressMeters;
        var physicsDelta = scenario.FixedPhysicsDeltaSeconds;
        for (var index = 0; index < scenario.PhysicsTicksPerControlTick; index++)
        {
            var command = _commandAllocator.Allocate(
                action,
                vehicle.State.SpeedMetersPerSecond,
                vehicle.State.StateOfCharge);
            var previousDistance = vehicle.State.DistanceMeters;
            var previousSpeed = vehicle.State.SpeedMetersPerSecond;
            vehicle.Step(command, physicsDelta);
            var travelledDistance = vehicle.State.DistanceMeters - previousDistance;
            var meanSpeed = 0.5 * (previousSpeed + vehicle.State.SpeedMetersPerSecond);
            var steeringAngle = action.Steering *
                _vehicleSpecification.MaximumSteeringAngleDegrees * Math.PI / 180.0;
            _yawRateRadiansPerSecond = meanSpeed / _vehicleSpecification.WheelbaseMeters * Math.Tan(steeringAngle);
            var midpointHeading = _headingRadians + (0.5 * _yawRateRadiansPerSecond * physicsDelta);
            _position += new Vector2D(Math.Cos(midpointHeading), Math.Sin(midpointHeading)) * travelledDistance;
            _headingRadians = NormalizeAngle(_headingRadians + (_yawRateRadiansPerSecond * physicsDelta));
            _physicsTick++;
        }

        _controlTick++;
        _steering = action.Steering;
        var groundTruth = _groundTruthQuery.Observe(
            _position,
            _headingRadians,
            Math.Max(0.0, _routeProgressMeters - ProgressProjectionBacktrackMeters),
            ReferenceLookaheadMeters,
            maximumProgressMeters: _routeProgressMeters + ProgressProjectionForwardWindowMeters);
        _routeProgressMeters = groundTruth.ProgressMeters;
        _ruleMetrics.Update(
            groundTruth.ProgressMeters,
            vehicle.State.SpeedMetersPerSecond,
            groundTruth.Lane.SpeedLimitMetersPerSecond,
            groundTruth.IsLaneDeparture,
            scenario.FixedControlDeltaSeconds);
        var jerk = Math.Abs(
            (vehicle.State.AccelerationMetersPerSecondSquared - _previousControlAcceleration) /
            scenario.FixedControlDeltaSeconds);
        _previousControlAcceleration = vehicle.State.AccelerationMetersPerSecondSquared;
        _absoluteJerkSum += jerk;
        _jerkSampleCount++;

        _termination = DetermineTermination(scenario, groundTruth);
        _observation = CreateObservation(groundTruth);
        var metrics = CreateMetrics(groundTruth);
        var reward = CalculateReward(groundTruth, _routeProgressMeters - previousProgress, _termination);
        return EpisodeStepResult.Create(_observation, reward, metrics, _termination);
    }

    private PrivilegedObservation CreateObservation(RoadGroundTruthSnapshot groundTruth)
    {
        var vehicle = _vehicle ?? throw new InvalidOperationException("The vehicle plant is unavailable.");
        return PrivilegedObservation.Create(
            _controlTick,
            _physicsTick,
            EgoVehicleObservation.Create(
                _position,
                _headingRadians,
                vehicle.State.SpeedMetersPerSecond,
                vehicle.State.AccelerationMetersPerSecondSquared,
                _yawRateRadiansPerSecond,
                _steering,
                vehicle.State.StateOfCharge),
            groundTruth.Lane,
            groundTruth.Route,
            groundTruth.UpcomingTrafficControl,
            hasCollision: false,
            groundTruth.IsOutsideDrivableArea,
            groundTruth.IsWrongWay);
    }

    private EpisodeMetrics CreateMetrics(RoadGroundTruthSnapshot groundTruth)
    {
        var vehicle = _vehicle ?? throw new InvalidOperationException("The vehicle plant is unavailable.");
        var consumedWattHours = Math.Max(
            0.0,
            (_vehicleSpecification.Battery.InitialStateOfCharge - vehicle.State.StateOfCharge) *
            _vehicleSpecification.Battery.CapacityKilowattHours * 1_000.0);
        return EpisodeMetrics.Create(
            vehicle.State.TimeSeconds,
            vehicle.State.DistanceMeters,
            _route.TotalLengthMeters <= 1e-9 ? 0.0 : groundTruth.ProgressMeters / _route.TotalLengthMeters,
            collisionCount: 0,
            _ruleMetrics.LaneDepartureCount,
            _ruleMetrics.RedLightViolationCount,
            _ruleMetrics.StopSignViolationCount,
            consumedWattHours,
            _jerkSampleCount == 0 ? 0.0 : _absoluteJerkSum / _jerkSampleCount,
            _ruleMetrics.SpeedingDurationSeconds,
            _ruleMetrics.LaneDepartureDurationSeconds);
    }

    private EpisodeTermination DetermineTermination(
        SimulationScenario scenario,
        RoadGroundTruthSnapshot groundTruth)
    {
        if (groundTruth.IsOutsideDrivableArea)
        {
            return EpisodeTermination.Terminated(EpisodeTerminationReason.LeftDrivableArea);
        }

        if (groundTruth.IsWrongWay)
        {
            return EpisodeTermination.Terminated(EpisodeTerminationReason.WrongWay);
        }

        if (groundTruth.Route.RemainingDistanceMeters <= RouteCompletionToleranceMeters)
        {
            return EpisodeTermination.Terminated(EpisodeTerminationReason.RouteCompleted);
        }

        return _controlTick >= scenario.MaximumControlTicks
            ? EpisodeTermination.Truncated(EpisodeTerminationReason.ControlTickLimitReached)
            : EpisodeTermination.Ongoing;
    }

    private static double CalculateReward(
        RoadGroundTruthSnapshot groundTruth,
        double progressDeltaMeters,
        EpisodeTermination termination)
    {
        var reward = progressDeltaMeters -
            (0.05 * Math.Abs(groundTruth.Lane.LateralOffsetMeters)) -
            (0.02 * Math.Abs(groundTruth.Lane.HeadingErrorRadians));
        if (termination.Reason == EpisodeTerminationReason.RouteCompleted)
        {
            reward += 25.0;
        }
        else if (termination.Kind == EpisodeTerminationKind.Terminated)
        {
            reward -= 10.0;
        }

        return reward;
    }

    private static double NormalizeAngle(double angleRadians)
    {
        return Math.Atan2(Math.Sin(angleRadians), Math.Cos(angleRadians));
    }
}

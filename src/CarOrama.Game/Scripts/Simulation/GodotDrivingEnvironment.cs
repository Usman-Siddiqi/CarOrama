using CarOrama.Core.Control;
using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;
using CarOrama.Game.Environment;
using CarOrama.Game.Input;
using CarOrama.Game.Vehicle;
using Godot;

namespace CarOrama.Game.Simulation;

/// <summary>
/// Pausable asynchronous adapter for the Godot/Jolt plant. The scene tree is
/// paused between calls so wall-clock controller or transport latency cannot add
/// unrequested physics or traffic-signal ticks.
/// </summary>
public sealed partial class GodotDrivingEnvironment : Node
{
    private const double RouteCompletionToleranceMeters = 0.5;
    private const double ProjectionBacktrackMeters = 2.0;
    private const double ProjectionForwardWindowMeters = 20.0;
    private const double LookaheadMeters = 2.0;
    private readonly RoadWorld _roadWorld;
    private readonly ElectricVehicle _vehicle;
    private readonly DrivingRoute _route;
    private readonly BufferedVehicleCommandSource _commandSource;
    private readonly VehicleSpecification _vehicleSpecification;
    private readonly RoadGroundTruthQuery _groundTruthQuery;
    private readonly RoadRuleMetricsMonitor _ruleMetrics;
    private readonly LongitudinalCommandAllocator _commandAllocator;
    private TaskCompletionSource? _physicsStepCompletion;
    private PrivilegedObservation? _observation;
    private EpisodeTermination _termination = EpisodeTermination.Ongoing;
    private int _remainingPhysicsTicks;
    private long _controlTick;
    private long _physicsTick;
    private double _routeProgressMeters;
    private double _previousSpeed;
    private double _previousHeading;
    private double _previousAcceleration;
    private double _absoluteJerkSum;
    private int _jerkSamples;
    private double _distanceTravelledMeters;
    private Vector2D _previousPosition;
    private double _steering;

    public GodotDrivingEnvironment(
        RoadWorld roadWorld,
        ElectricVehicle vehicle,
        DrivingRoute route,
        BufferedVehicleCommandSource commandSource,
        VehicleSpecification vehicleSpecification)
    {
        _roadWorld = roadWorld ?? throw new ArgumentNullException(nameof(roadWorld));
        _vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
        _vehicleSpecification = vehicleSpecification ?? throw new ArgumentNullException(nameof(vehicleSpecification));
        _vehicleSpecification.Validate();
        _groundTruthQuery = new RoadGroundTruthQuery(roadWorld.Network, route);
        _ruleMetrics = new RoadRuleMetricsMonitor(roadWorld.Network, route);
        _commandAllocator = new LongitudinalCommandAllocator(vehicleSpecification);
        Name = "GodotDrivingEnvironment";
        ProcessMode = ProcessModeEnum.Always;
    }

    public SimulationScenario? ActiveScenario { get; private set; }

    public bool IsPausedBetweenSteps => GetTree().Paused && _physicsStepCompletion is null;

    public Task<PrivilegedObservation> ResetAsync(EpisodeResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_physicsStepCompletion is not null)
        {
            throw new InvalidOperationException("Cannot reset while a physics step is running.");
        }

        if (request.Seed != _roadWorld.Network.Seed || request.RouteId != _route.Id)
        {
            throw new ArgumentException("The reset request does not match this world and route.", nameof(request));
        }

        if (request.Scenario.PhysicsTicksPerSecond != Engine.PhysicsTicksPerSecond)
        {
            throw new ArgumentException(
                $"Scenario physics rate {request.Scenario.PhysicsTicksPerSecond} does not match Godot's " +
                $"{Engine.PhysicsTicksPerSecond} Hz setting.",
                nameof(request));
        }

        var spawn = _roadWorld.Network.SpawnPoints.SingleOrDefault(candidate => candidate.Id == request.SpawnPointId) ??
            throw new ArgumentException("The requested spawn point does not exist.", nameof(request));
        if (spawn.LaneId != _route.LaneIds[0])
        {
            throw new ArgumentException("The spawn point must be on the route's first lane.", nameof(request));
        }

        GetTree().Paused = true;
        _commandSource.Reset();
        _roadWorld.TrafficSignals?.Reset();
        _vehicle.SetResetTransform(CreateSpawnTransform(spawn));
        ActiveScenario = request.Scenario;
        _controlTick = 0;
        _physicsTick = 0;
        _routeProgressMeters = 0.0;
        _previousSpeed = 0.0;
        _previousAcceleration = 0.0;
        _absoluteJerkSum = 0.0;
        _jerkSamples = 0;
        _distanceTravelledMeters = 0.0;
        _steering = 0.0;
        _termination = EpisodeTermination.Ongoing;
        _previousPosition = GetPosition();
        _previousHeading = GetHeadingRadians();
        var groundTruth = ObserveGroundTruth();
        _routeProgressMeters = groundTruth.ProgressMeters;
        _ruleMetrics.Reset(groundTruth.ProgressMeters, groundTruth.IsLaneDeparture);
        _observation = CreateObservation(groundTruth, acceleration: 0.0, yawRate: 0.0);
        return Task.FromResult(_observation);
    }

    public PrivilegedObservation Observe()
    {
        return _observation ?? throw new InvalidOperationException("ResetAsync must be called before observing.");
    }

    public async Task<EpisodeStepResult> StepAsync(
        DrivingAction action,
        Func<long, double, Task>? pausedPhysicsTickObserver = null)
    {
        var scenario = ActiveScenario ?? throw new InvalidOperationException("ResetAsync must be called before stepping.");
        if (_physicsStepCompletion is not null)
        {
            throw new InvalidOperationException("A physics step is already running.");
        }

        if (_termination.IsTerminal)
        {
            throw new InvalidOperationException("ResetAsync is required after an episode terminates.");
        }

        if (action.ContractVersion != SimulationContract.CurrentVersion || action.ControlTick != _controlTick)
        {
            throw new ArgumentException($"Expected a contract-v{SimulationContract.CurrentVersion} action for tick {_controlTick}.", nameof(action));
        }

        var command = _commandAllocator.Allocate(action, _vehicle.SpeedMetersPerSecond, _vehicle.StateOfCharge);
        _commandSource.Set(command);
        _steering = action.Steering;
        var previousProgress = _routeProgressMeters;
        if (pausedPhysicsTickObserver is null)
        {
            await AdvancePhysicsAsync(scenario.PhysicsTicksPerControlTick);
        }
        else
        {
            for (var index = 0; index < scenario.PhysicsTicksPerControlTick; index++)
            {
                await AdvancePhysicsAsync(1);
                await pausedPhysicsTickObserver(
                    _physicsTick,
                    _physicsTick / (double)scenario.PhysicsTicksPerSecond);
            }
        }

        _controlTick++;
        var position = GetPosition();
        var heading = GetHeadingRadians();
        var speed = _vehicle.SpeedMetersPerSecond;
        var acceleration = (speed - _previousSpeed) / scenario.FixedControlDeltaSeconds;
        var yawRate = NormalizeAngle(heading - _previousHeading) / scenario.FixedControlDeltaSeconds;
        _distanceTravelledMeters += Vector2D.Distance(_previousPosition, position);
        var jerk = Math.Abs((acceleration - _previousAcceleration) / scenario.FixedControlDeltaSeconds);
        _absoluteJerkSum += jerk;
        _jerkSamples++;
        _previousPosition = position;
        _previousHeading = heading;
        _previousSpeed = speed;
        _previousAcceleration = acceleration;

        var groundTruth = ObserveGroundTruth();
        _routeProgressMeters = groundTruth.ProgressMeters;
        _ruleMetrics.Update(
            groundTruth.ProgressMeters,
            speed,
            groundTruth.Lane.SpeedLimitMetersPerSecond,
            groundTruth.IsLaneDeparture,
            scenario.FixedControlDeltaSeconds,
            ResolveSignalState);
        _termination = DetermineTermination(scenario, groundTruth);
        _observation = CreateObservation(groundTruth, acceleration, yawRate);
        var metrics = CreateMetrics(groundTruth);
        var reward = CalculateReward(groundTruth, _routeProgressMeters - previousProgress, _termination);
        return EpisodeStepResult.Create(_observation, reward, metrics, _termination);
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (_physicsStepCompletion is null || _remainingPhysicsTicks <= 0)
        {
            return;
        }

        _remainingPhysicsTicks--;
        _physicsTick++;
        if (_remainingPhysicsTicks > 0)
        {
            return;
        }

        GetTree().Paused = true;
        var completion = _physicsStepCompletion;
        _physicsStepCompletion = null;
        completion.SetResult();
    }

    private async Task AdvancePhysicsAsync(int tickCount)
    {
        _remainingPhysicsTicks = tickCount;
        _physicsStepCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = _physicsStepCompletion.Task;
        GetTree().Paused = false;
        await completion;
    }

    private RoadGroundTruthSnapshot ObserveGroundTruth()
    {
        return _groundTruthQuery.Observe(
            GetPosition(),
            GetHeadingRadians(),
            Math.Max(0.0, _routeProgressMeters - ProjectionBacktrackMeters),
            LookaheadMeters,
            ResolveSignalState,
            _routeProgressMeters + ProjectionForwardWindowMeters);
    }

    private ObservedTrafficControlState ResolveSignalState(TrafficControl control)
    {
        if (control.Kind != TrafficControlKind.TrafficLight || _roadWorld.TrafficSignals is null)
        {
            return ObservedTrafficControlState.Unknown;
        }

        return _roadWorld.GetTrafficSignalState(control.Id) switch
        {
            TrafficSignalState.Red => ObservedTrafficControlState.Red,
            TrafficSignalState.Yellow => ObservedTrafficControlState.Yellow,
            TrafficSignalState.Green => ObservedTrafficControlState.Green,
            _ => ObservedTrafficControlState.Unknown,
        };
    }

    private PrivilegedObservation CreateObservation(
        RoadGroundTruthSnapshot groundTruth,
        double acceleration,
        double yawRate)
    {
        return PrivilegedObservation.Create(
            _controlTick,
            _physicsTick,
            EgoVehicleObservation.Create(
                GetPosition(),
                GetHeadingRadians(),
                _vehicle.SpeedMetersPerSecond,
                acceleration,
                yawRate,
                _steering,
                _vehicle.StateOfCharge),
            groundTruth.Lane,
            groundTruth.Route,
            groundTruth.UpcomingTrafficControl,
            _vehicle.CollisionCount > 0,
            groundTruth.IsOutsideDrivableArea,
            groundTruth.IsWrongWay);
    }

    private EpisodeMetrics CreateMetrics(RoadGroundTruthSnapshot groundTruth)
    {
        var elapsed = ActiveScenario!.FixedControlDeltaSeconds * _controlTick;
        var energy = Math.Max(
            0.0,
            (_vehicleSpecification.Battery.InitialStateOfCharge - _vehicle.StateOfCharge) *
            _vehicleSpecification.Battery.CapacityKilowattHours * 1_000.0);
        return EpisodeMetrics.Create(
            elapsed,
            _distanceTravelledMeters,
            groundTruth.ProgressMeters / _route.TotalLengthMeters,
            _vehicle.CollisionCount,
            _ruleMetrics.LaneDepartureCount,
            _ruleMetrics.RedLightViolationCount,
            _ruleMetrics.StopSignViolationCount,
            energy,
            _jerkSamples == 0 ? 0.0 : _absoluteJerkSum / _jerkSamples,
            _ruleMetrics.SpeedingDurationSeconds,
            _ruleMetrics.LaneDepartureDurationSeconds);
    }

    private EpisodeTermination DetermineTermination(
        SimulationScenario scenario,
        RoadGroundTruthSnapshot groundTruth)
    {
        if (_vehicle.CollisionCount > 0)
        {
            return EpisodeTermination.Terminated(
                EpisodeTerminationReason.Collision,
                _vehicle.LastCollisionBodyName is null
                    ? null
                    : $"Contacted {_vehicle.LastCollisionBodyName}.");
        }

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

    private Vector2D GetPosition() => new(_vehicle.GlobalPosition.X, _vehicle.GlobalPosition.Z);

    private double GetHeadingRadians()
    {
        var forward = -_vehicle.GlobalTransform.Basis.Z.Normalized();
        return Math.Atan2(forward.Z, forward.X);
    }

    private static Transform3D CreateSpawnTransform(SpawnPoint spawn)
    {
        var forward = new Vector3((float)spawn.Forward.X, 0.0f, (float)spawn.Forward.Y).Normalized();
        var yaw = Mathf.Atan2(-forward.X, -forward.Z);
        return new Transform3D(
            new Basis(Vector3.Up, yaw),
            new Vector3((float)spawn.Position.X, 1.15f, (float)spawn.Position.Y));
    }

    private static double NormalizeAngle(double angle) => Math.Atan2(Math.Sin(angle), Math.Cos(angle));
}

using CarOrama.Core.Simulation;

namespace CarOrama.Core.Control;

public readonly record struct RecoveryPerturbationDecision(
    DrivingAction ExecutedAction,
    bool SuppressSensorFrames,
    bool IsPerturbation);

/// <summary>
/// Injects brief, deterministic steering disturbances on safe straight-road
/// sections. Disturbance ticks are excluded from camera supervision; subsequent
/// teacher-controlled ticks provide recovery examples from imperfect states.
/// </summary>
public sealed class RecoveryPerturbationSchedule
{
    private readonly int _intervalTicks;
    private readonly int _durationTicks;
    private readonly double _steeringOffset;
    private long _nextPerturbationTick;
    private int _remainingPerturbationTicks;
    private double _direction;

    public RecoveryPerturbationSchedule(
        int controlTicksPerSecond,
        long driverSeed,
        double intervalSeconds = 12.0,
        double durationSeconds = 0.2,
        double steeringOffset = 0.22)
    {
        if (controlTicksPerSecond <= 0 || !double.IsFinite(intervalSeconds) || intervalSeconds <= 0.0 ||
            !double.IsFinite(durationSeconds) || durationSeconds <= 0.0 ||
            !double.IsFinite(steeringOffset) || steeringOffset is <= 0.0 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(controlTicksPerSecond));
        }

        _intervalTicks = Math.Max(1, (int)Math.Round(intervalSeconds * controlTicksPerSecond));
        _durationTicks = Math.Max(1, (int)Math.Round(durationSeconds * controlTicksPerSecond));
        _nextPerturbationTick = Math.Max(controlTicksPerSecond * 6L, _intervalTicks / 2L);
        _steeringOffset = steeringOffset;
        _direction = (driverSeed & 1L) == 0L ? 1.0 : -1.0;
    }

    public RecoveryPerturbationDecision Apply(
        PrivilegedObservation observation,
        DrivingAction teacherAction)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (teacherAction.ControlTick != observation.ControlTick)
        {
            throw new ArgumentException("The teacher action must match the observation tick.", nameof(teacherAction));
        }

        if (_remainingPerturbationTicks == 0 &&
            observation.ControlTick >= _nextPerturbationTick &&
            IsSafeToPerturb(observation, teacherAction))
        {
            _remainingPerturbationTicks = _durationTicks;
            _nextPerturbationTick = observation.ControlTick + _intervalTicks;
        }

        if (_remainingPerturbationTicks == 0)
        {
            return new RecoveryPerturbationDecision(teacherAction, false, false);
        }

        _remainingPerturbationTicks--;
        var action = DrivingAction.Create(
            observation.ControlTick,
            teacherAction.Steering + (_direction * _steeringOffset),
            teacherAction.LongitudinalAccelerationMetersPerSecondSquared);
        if (_remainingPerturbationTicks == 0)
        {
            _direction = -_direction;
        }

        return new RecoveryPerturbationDecision(action, true, true);
    }

    private static bool IsSafeToPerturb(
        PrivilegedObservation observation,
        DrivingAction teacherAction)
    {
        var controlDistance = observation.UpcomingTrafficControl.DistanceToStopLineMeters;
        return observation.EgoVehicle.SpeedMetersPerSecond >= 6.0 &&
            Math.Abs(teacherAction.Steering) <= 0.08 &&
            Math.Abs(observation.Lane.CurvaturePerMeter) <= 0.005 &&
            (!controlDistance.HasValue || controlDistance.Value >= 35.0);
    }
}
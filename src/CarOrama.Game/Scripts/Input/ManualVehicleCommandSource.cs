using CarOrama.Core.Control;
using Godot;

namespace CarOrama.Game.Input;

public sealed class ManualVehicleCommandSource : IVehicleCommandSource, IVehicleLightingCommandSource
{
    private const double ThrottleApplicationRatePerSecond = 7.5;
    private const double BrakeReverseApplicationRatePerSecond = 4.0;
    private const double FrictionBrakeApplicationRatePerSecond = 2.5;
    private const double PedalReleaseRatePerSecond = 10.0;

    private bool _headlightsEnabled;
    private bool _hazardLightsEnabled;
    private TurnSignalState _turnSignal;
    private double _forwardThrottle;
    private double _brakeReverseThrottle;
    private double _frictionBrake;

    public VehicleCommand ReadCommand(double deltaSeconds)
    {
        var steering = Godot.Input.GetAxis(InputBindings.SteerLeft, InputBindings.SteerRight);
        _forwardThrottle = MovePedal(
            _forwardThrottle,
            Godot.Input.GetActionStrength(InputBindings.Throttle),
            ThrottleApplicationRatePerSecond,
            deltaSeconds);
        _brakeReverseThrottle = MovePedal(
            _brakeReverseThrottle,
            Godot.Input.GetActionStrength(InputBindings.BrakeReverse),
            BrakeReverseApplicationRatePerSecond,
            deltaSeconds);
        _frictionBrake = MovePedal(
            _frictionBrake,
            Godot.Input.GetActionStrength(InputBindings.FrictionBrake),
            FrictionBrakeApplicationRatePerSecond,
            deltaSeconds);
        return VehicleCommand.Create(
            steering,
            _forwardThrottle - _brakeReverseThrottle,
            regenerativeBrake: 0.0,
            _frictionBrake);
    }

    public VehicleLightingCommand ReadLightingCommand(double deltaSeconds)
    {
        _ = deltaSeconds;
        if (Godot.Input.IsActionJustPressed(InputBindings.Headlights))
        {
            _headlightsEnabled = !_headlightsEnabled;
        }

        if (Godot.Input.IsActionJustPressed(InputBindings.HazardLights))
        {
            _hazardLightsEnabled = !_hazardLightsEnabled;
            if (_hazardLightsEnabled)
            {
                _turnSignal = TurnSignalState.Off;
            }
        }

        var leftPressed = Godot.Input.IsActionJustPressed(InputBindings.LeftTurnSignal);
        var rightPressed = Godot.Input.IsActionJustPressed(InputBindings.RightTurnSignal);
        if (leftPressed != rightPressed)
        {
            _hazardLightsEnabled = false;
            var requestedSignal = leftPressed ? TurnSignalState.Left : TurnSignalState.Right;
            _turnSignal = _turnSignal == requestedSignal ? TurnSignalState.Off : requestedSignal;
        }

        return VehicleLightingCommand.Create(_headlightsEnabled, _turnSignal, _hazardLightsEnabled);
    }

    private static double MovePedal(double current, double target, double applicationRate, double deltaSeconds)
    {
        var rate = target > current ? applicationRate : PedalReleaseRatePerSecond;
        var maximumChange = Math.Max(0.0, deltaSeconds) * rate;
        return Math.Clamp(target, current - maximumChange, current + maximumChange);
    }
}

using CarOrama.Core.Control;
using Godot;

namespace CarOrama.Game.Input;

public sealed class ManualVehicleCommandSource : IVehicleCommandSource, IVehicleLightingCommandSource
{
    private bool _headlightsEnabled;
    private bool _hazardLightsEnabled;
    private TurnSignalState _turnSignal;

    public VehicleCommand ReadCommand(double deltaSeconds)
    {
        _ = deltaSeconds;
        var steering = Godot.Input.GetAxis(InputBindings.SteerLeft, InputBindings.SteerRight);
        var throttle = Godot.Input.GetActionStrength(InputBindings.Throttle);
        var regenerativeBrake = Godot.Input.GetActionStrength(InputBindings.RegenerativeBrake);
        var frictionBrake = Godot.Input.GetActionStrength(InputBindings.FrictionBrake);
        return VehicleCommand.Create(steering, throttle, regenerativeBrake, frictionBrake);
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
}

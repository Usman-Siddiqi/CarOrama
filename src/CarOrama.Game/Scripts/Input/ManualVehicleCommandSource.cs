using CarOrama.Core.Control;
using Godot;

namespace CarOrama.Game.Input;

public sealed class ManualVehicleCommandSource : IVehicleCommandSource
{
    public VehicleCommand ReadCommand(double deltaSeconds)
    {
        _ = deltaSeconds;
        var steering = Godot.Input.GetAxis(InputBindings.SteerLeft, InputBindings.SteerRight);
        var throttle = Godot.Input.GetActionStrength(InputBindings.Throttle);
        var regenerativeBrake = Godot.Input.GetActionStrength(InputBindings.RegenerativeBrake);
        var frictionBrake = Godot.Input.GetActionStrength(InputBindings.FrictionBrake);
        return VehicleCommand.Create(steering, throttle, regenerativeBrake, frictionBrake);
    }
}


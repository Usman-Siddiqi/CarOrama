using Godot;

namespace CarOrama.Game.Input;

internal static class InputBindings
{
    public const string SteerLeft = "vehicle_steer_left";
    public const string SteerRight = "vehicle_steer_right";
    public const string Throttle = "vehicle_throttle";
    public const string RegenerativeBrake = "vehicle_regenerative_brake";
    public const string FrictionBrake = "vehicle_friction_brake";
    public const string Reset = "vehicle_reset";
    public const string NewSeed = "world_new_seed";

    public static void EnsureConfigured()
    {
        AddKey(SteerLeft, Key.A);
        AddKey(SteerLeft, Key.Left);
        AddKey(SteerRight, Key.D);
        AddKey(SteerRight, Key.Right);
        AddKey(Throttle, Key.W);
        AddKey(Throttle, Key.Up);
        AddKey(RegenerativeBrake, Key.S);
        AddKey(RegenerativeBrake, Key.Down);
        AddKey(FrictionBrake, Key.Space);
        AddKey(Reset, Key.R);
        AddKey(NewSeed, Key.N);

        AddJoyAxis(SteerLeft, JoyAxis.LeftX, -1.0f);
        AddJoyAxis(SteerRight, JoyAxis.LeftX, 1.0f);
        AddJoyAxis(Throttle, JoyAxis.TriggerRight, 1.0f);
        AddJoyAxis(RegenerativeBrake, JoyAxis.TriggerLeft, 1.0f);
        AddJoyButton(FrictionBrake, JoyButton.A);
        AddJoyButton(Reset, JoyButton.Start);
        AddJoyButton(NewSeed, JoyButton.Y);
    }

    private static void AddKey(StringName action, Key key)
    {
        EnsureAction(action);
        var input = new InputEventKey { PhysicalKeycode = key };
        if (!InputMap.ActionHasEvent(action, input))
        {
            InputMap.ActionAddEvent(action, input);
        }
    }

    private static void AddJoyAxis(StringName action, JoyAxis axis, float value)
    {
        EnsureAction(action);
        var input = new InputEventJoypadMotion { Axis = axis, AxisValue = value };
        if (!InputMap.ActionHasEvent(action, input))
        {
            InputMap.ActionAddEvent(action, input);
        }
    }

    private static void AddJoyButton(StringName action, JoyButton button)
    {
        EnsureAction(action);
        var input = new InputEventJoypadButton { ButtonIndex = button };
        if (!InputMap.ActionHasEvent(action, input))
        {
            InputMap.ActionAddEvent(action, input);
        }
    }

    private static void EnsureAction(StringName action)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action, 0.12f);
        }
    }
}


using CarOrama.Game.Vehicle;
using Godot;

namespace CarOrama.Game.UI;

public partial class TelemetryHud : CanvasLayer
{
    private readonly Label _label;

    public TelemetryHud()
    {
        Name = "TelemetryHud";
        var panel = new ColorRect
        {
            Name = "Panel",
            Position = new Vector2(18.0f, 18.0f),
            Size = new Vector2(470.0f, 238.0f),
            Color = new Color(0.025f, 0.04f, 0.05f, 0.82f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(panel);
        _label = new Label
        {
            Name = "Telemetry",
            Position = new Vector2(18.0f, 14.0f),
            Size = new Vector2(440.0f, 212.0f),
        };
        _label.AddThemeColorOverride("font_color", new Color("e9f4f7"));
        _label.AddThemeFontSizeOverride("font_size", 17);
        panel.AddChild(_label);
    }

    public ElectricVehicle? Vehicle { get; set; }

    public long Seed { get; set; }

    public override void _Process(double delta)
    {
        _ = delta;
        if (Vehicle is null || !IsInstanceValid(Vehicle))
        {
            return;
        }

        var lighting = Vehicle.LastLightingCommand;
        var signal = lighting.HazardLightsEnabled
            ? "HAZARD"
            : lighting.TurnSignal.ToString().ToUpperInvariant();
        _label.Text =
            $"CARORAMA  |  SEED {Seed}\n" +
            $"{Vehicle.SpeedMetersPerSecond * 3.6,6:F1} km/h    SOC {Vehicle.StateOfCharge * 100.0,5:F1}%\n" +
            $"Motor {Vehicle.MotorSpeedRpm,7:F0} rpm    Pack {Vehicle.BatteryPowerKilowatts,6:F1} kW\n" +
            $"Contact {Vehicle.GroundedWheelCount}/4    Collisions {Vehicle.CollisionCount}\n" +
            $"Headlights {(lighting.HeadlightsEnabled ? "ON" : "OFF"),-3}    Signal {signal}\n\n" +
            "WASD/Arrows drive  |  Space friction brake\n" +
            "Q/E indicators  |  X hazards  |  H headlights\n" +
            "R reset  |  N new seeded world";
    }
}

using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public sealed partial class TrafficSignalHead : Node3D
{
    private static readonly Color RedColor = new("d52b2b");
    private static readonly Color YellowColor = new("f0b323");
    private static readonly Color GreenColor = new("22c55e");

    private MeshInstance3D? _redLamp;
    private MeshInstance3D? _yellowLamp;
    private MeshInstance3D? _greenLamp;

    public TrafficSignalHead(string controlId)
    {
        ControlId = controlId;
        Name = controlId;
    }

    public string ControlId { get; }

    public TrafficSignalState State { get; private set; } = TrafficSignalState.Red;

    public void SetLamps(MeshInstance3D redLamp, MeshInstance3D yellowLamp, MeshInstance3D greenLamp)
    {
        _redLamp = redLamp;
        _yellowLamp = yellowLamp;
        _greenLamp = greenLamp;
        SetState(State, force: true);
    }

    public void SetState(TrafficSignalState state, bool force = false)
    {
        if (!force && State == state)
        {
            return;
        }

        State = state;
        ApplyLamp(_redLamp, RedColor, state == TrafficSignalState.Red);
        ApplyLamp(_yellowLamp, YellowColor, state == TrafficSignalState.Yellow);
        ApplyLamp(_greenLamp, GreenColor, state == TrafficSignalState.Green);
    }

    private static void ApplyLamp(MeshInstance3D? lamp, Color color, bool active)
    {
        if (lamp is null)
        {
            return;
        }

        lamp.MaterialOverride = PrimitiveFactory.Material(
            active ? color : color.Darkened(0.82f),
            0.42f,
            active);
    }
}

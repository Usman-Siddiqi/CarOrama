using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public sealed partial class TrafficSignalHead : Node3D
{
    private static readonly Color RedColor = new("d52b2b");
    private static readonly Color YellowColor = new("f0b323");
    private static readonly Color GreenColor = new("22c55e");

    private readonly List<LampGroup> _lampGroups = [];

    public TrafficSignalHead(string controlId)
    {
        ControlId = controlId;
        Name = controlId;
    }

    public string ControlId { get; }

    public TrafficSignalState State { get; private set; } = TrafficSignalState.Red;

    public int SignalHeadCount => _lampGroups.Count;

    public void AddLamps(MeshInstance3D redLamp, MeshInstance3D yellowLamp, MeshInstance3D greenLamp)
    {
        _lampGroups.Add(new LampGroup(redLamp, yellowLamp, greenLamp));
        SetState(State, force: true);
    }

    public void SetState(TrafficSignalState state, bool force = false)
    {
        if (!force && State == state)
        {
            return;
        }

        State = state;
        foreach (var lamps in _lampGroups)
        {
            ApplyLamp(lamps.Red, RedColor, state == TrafficSignalState.Red);
            ApplyLamp(lamps.Yellow, YellowColor, state == TrafficSignalState.Yellow);
            ApplyLamp(lamps.Green, GreenColor, state == TrafficSignalState.Green);
        }
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

    private sealed record LampGroup(
        MeshInstance3D Red,
        MeshInstance3D Yellow,
        MeshInstance3D Green);
}

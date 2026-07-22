using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public partial class RoadWorld : Node3D
{
    private readonly Dictionary<string, TrafficSignalHead> _trafficSignalHeads = new(StringComparer.Ordinal);

    public RoadWorld(RoadNetwork network)
    {
        Network = network;
        Name = "RoadWorld";
    }

    public RoadNetwork Network { get; }

    public TrafficSignalSystem? TrafficSignals { get; private set; }

    public string ExportStructuredJson() => RoadNetworkSerializer.ToJson(Network);

    public TrafficSignalState GetTrafficSignalState(string controlId) =>
        TrafficSignals?.GetState(controlId) ??
        throw new InvalidOperationException("This road world has no active traffic signals.");

    public TrafficSignalHead GetTrafficSignalHead(string controlId) => _trafficSignalHeads[controlId];

    public void RegisterTrafficSignalHead(TrafficSignalHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        _trafficSignalHeads.Add(head.ControlId, head);
    }

    public void ActivateTrafficSignals()
    {
        if (_trafficSignalHeads.Count == 0 || TrafficSignals is not null)
        {
            return;
        }

        TrafficSignals = new TrafficSignalSystem(Network, _trafficSignalHeads);
        AddChild(TrafficSignals);
    }
}

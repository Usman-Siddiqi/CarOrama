using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public partial class RoadWorld : Node3D
{
    public RoadWorld(RoadNetwork network)
    {
        Network = network;
        Name = "RoadWorld";
    }

    public RoadNetwork Network { get; }

    public string ExportStructuredJson() => RoadNetworkSerializer.ToJson(Network);
}


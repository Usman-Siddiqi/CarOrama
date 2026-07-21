namespace CarOrama.Core.Roads;

public interface IRoadNetworkGenerator
{
    RoadNetwork Generate(RoadNetworkConfig config);
}


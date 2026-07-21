using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarOrama.Core.Roads;

public static class RoadNetworkSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(RoadNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var document = new RoadNetworkDocument(
            network.Seed,
            network.Nodes,
            network.Segments,
            network.Lanes,
            network.Intersections,
            network.StopLines,
            network.TrafficControls,
            network.SpawnPoints);
        return JsonSerializer.Serialize(document, Options);
    }

    private sealed record RoadNetworkDocument(
        long Seed,
        IReadOnlyList<RoadNode> Nodes,
        IReadOnlyList<RoadSegment> Segments,
        IReadOnlyList<Lane> Lanes,
        IReadOnlyList<Intersection> Intersections,
        IReadOnlyList<StopLine> StopLines,
        IReadOnlyList<TrafficControl> TrafficControls,
        IReadOnlyList<SpawnPoint> SpawnPoints);
}


namespace CarOrama.Core.Roads;

public static class RoutePlanner
{
    public static IReadOnlyList<string> FindLaneRoute(RoadNetwork network, string startLaneId, string destinationLaneId)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (!network.TryGetLane(startLaneId, out _))
        {
            throw new ArgumentException("The start lane does not exist.", nameof(startLaneId));
        }

        if (!network.TryGetLane(destinationLaneId, out _))
        {
            throw new ArgumentException("The destination lane does not exist.", nameof(destinationLaneId));
        }

        var previous = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [startLaneId] = null,
        };
        var queue = new Queue<string>();
        queue.Enqueue(startLaneId);

        while (queue.TryDequeue(out var current))
        {
            if (current == destinationLaneId)
            {
                return Reconstruct(previous, current);
            }

            foreach (var successor in network.GetLane(current).SuccessorLaneIds)
            {
                if (previous.TryAdd(successor, current))
                {
                    queue.Enqueue(successor);
                }
            }
        }

        return [];
    }

    private static IReadOnlyList<string> Reconstruct(IReadOnlyDictionary<string, string?> previous, string destination)
    {
        var route = new List<string>();
        string? current = destination;
        while (current is not null)
        {
            route.Add(current);
            current = previous[current];
        }

        route.Reverse();
        return route;
    }
}


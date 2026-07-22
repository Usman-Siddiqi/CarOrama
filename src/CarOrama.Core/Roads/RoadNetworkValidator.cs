using CarOrama.Core.Geometry;

namespace CarOrama.Core.Roads;

public sealed record RoadNetworkValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class RoadNetworkValidator
{
    public static RoadNetworkValidationResult Validate(RoadNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var errors = new List<string>();

        CheckUnique(network.Nodes.Select(node => node.Id), "node", errors);
        CheckUnique(network.Segments.Select(segment => segment.Id), "segment", errors);
        CheckUnique(network.Lanes.Select(lane => lane.Id), "lane", errors);
        CheckUnique(network.StopLines.Select(line => line.Id), "stop line", errors);
        CheckUnique(network.TrafficControls.Select(control => control.Id), "traffic control", errors);
        CheckUnique(network.SpawnPoints.Select(spawn => spawn.Id), "spawn", errors);

        var nodeIds = network.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var segmentIds = network.Segments.Select(segment => segment.Id).ToHashSet(StringComparer.Ordinal);
        var laneIds = network.Lanes.Select(lane => lane.Id).ToHashSet(StringComparer.Ordinal);
        var stopLineIds = network.StopLines.Select(line => line.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var segment in network.Segments)
        {
            Require(nodeIds.Contains(segment.StartNodeId), $"Segment {segment.Id} has an unknown start node.", errors);
            Require(nodeIds.Contains(segment.EndNodeId), $"Segment {segment.Id} has an unknown end node.", errors);
            Require(segment.LanesPerDirection > 0, $"Segment {segment.Id} has no lanes per direction.", errors);
            Require(
                segment.Classification != RoadClassification.Arterial || segment.LanesPerDirection >= 2,
                $"Arterial segment {segment.Id} must provide multiple lanes per direction.",
                errors);
            Require(segment.WidthMeters > 0.0, $"Segment {segment.Id} has a non-positive width.", errors);
            Require(segment.CenterLine.Count >= 2, $"Segment {segment.Id} has no usable centre line.", errors);
            Require(
                segment.LaneIds.Count == segment.LanesPerDirection * 2,
                $"Segment {segment.Id} does not have balanced lane capacity.",
                errors);

            var segmentLanes = new List<Lane>(segment.LaneIds.Count);
            foreach (var laneId in segment.LaneIds)
            {
                Require(laneIds.Contains(laneId), $"Segment {segment.Id} references unknown lane {laneId}.", errors);
                if (network.TryGetLane(laneId, out var lane))
                {
                    segmentLanes.Add(lane);
                    Require(lane.SegmentId == segment.Id, $"Segment {segment.Id} references lane {laneId} owned by another segment.", errors);
                }
            }

            if (segmentLanes.Count == segment.LaneIds.Count)
            {
                var forwardCount = segmentLanes.Count(lane =>
                    lane.StartNodeId == segment.StartNodeId && lane.EndNodeId == segment.EndNodeId);
                var reverseCount = segmentLanes.Count(lane =>
                    lane.StartNodeId == segment.EndNodeId && lane.EndNodeId == segment.StartNodeId);
                Require(
                    forwardCount == segment.LanesPerDirection && reverseCount == segment.LanesPerDirection,
                    $"Segment {segment.Id} does not have balanced opposing lanes.",
                    errors);

                var combinedLaneWidth = segmentLanes.Sum(lane => lane.WidthMeters);
                Require(
                    Math.Abs(segment.WidthMeters - combinedLaneWidth) < 1e-6,
                    $"Segment {segment.Id} width does not match its lane geometry.",
                    errors);
            }
        }

        foreach (var lane in network.Lanes)
        {
            Require(segmentIds.Contains(lane.SegmentId), $"Lane {lane.Id} has an unknown segment.", errors);
            if (segmentIds.Contains(lane.SegmentId))
            {
                Require(
                    network.GetSegment(lane.SegmentId).LaneIds.Contains(lane.Id, StringComparer.Ordinal),
                    $"Lane {lane.Id} is not referenced by its segment.",
                    errors);
            }

            Require(nodeIds.Contains(lane.StartNodeId) && nodeIds.Contains(lane.EndNodeId), $"Lane {lane.Id} has an unknown endpoint.", errors);
            Require(lane.CenterLine.Count >= 2, $"Lane {lane.Id} has no usable centre line.", errors);
            Require(lane.LeftBoundary.Count == lane.CenterLine.Count, $"Lane {lane.Id} left boundary is misaligned.", errors);
            Require(lane.RightBoundary.Count == lane.CenterLine.Count, $"Lane {lane.Id} right boundary is misaligned.", errors);
            Require(Math.Abs(lane.Direction.Length - 1.0) < 1e-6, $"Lane {lane.Id} direction is not normalized.", errors);
            Require(lane.SpeedLimitMetersPerSecond > 0.0, $"Lane {lane.Id} has a non-positive speed limit.", errors);
            Require(lane.SuccessorLaneIds.Count > 0, $"Lane {lane.Id} has no valid continuation.", errors);

            foreach (var successor in lane.SuccessorLaneIds)
            {
                Require(laneIds.Contains(successor), $"Lane {lane.Id} references unknown successor {successor}.", errors);
                if (network.TryGetLane(successor, out var successorLane))
                {
                    Require(successorLane.StartNodeId == lane.EndNodeId, $"Lane {lane.Id} successor {successor} is disconnected.", errors);
                }
            }

            if (lane.StopLineId is not null)
            {
                Require(stopLineIds.Contains(lane.StopLineId), $"Lane {lane.Id} references unknown stop line.", errors);
            }
        }

        foreach (var stopLine in network.StopLines)
        {
            Require(laneIds.Contains(stopLine.LaneId), $"Stop line {stopLine.Id} references an unknown lane.", errors);
            Require(nodeIds.Contains(stopLine.IntersectionNodeId), $"Stop line {stopLine.Id} references an unknown intersection.", errors);
            Require(Vector2D.Distance(stopLine.LeftPoint, stopLine.RightPoint) > 1.0, $"Stop line {stopLine.Id} has invalid geometry.", errors);
        }

        foreach (var control in network.TrafficControls)
        {
            Require(laneIds.Contains(control.IncomingLaneId), $"Control {control.Id} references an unknown lane.", errors);
            Require(nodeIds.Contains(control.IntersectionNodeId), $"Control {control.Id} references an unknown intersection.", errors);
            Require(control.Kind != TrafficControlKind.None, $"Control {control.Id} has no behavior.", errors);
        }

        foreach (var spawn in network.SpawnPoints)
        {
            Require(laneIds.Contains(spawn.LaneId), $"Spawn {spawn.Id} references an unknown lane.", errors);
            Require(Math.Abs(spawn.Forward.Length - 1.0) < 1e-6, $"Spawn {spawn.Id} has an invalid heading.", errors);
        }

        ValidateConnectivity(network, errors);
        return new RoadNetworkValidationResult(errors);
    }

    private static void ValidateConnectivity(RoadNetwork network, ICollection<string> errors)
    {
        if (network.Nodes.Count == 0)
        {
            errors.Add("The network has no nodes.");
            return;
        }

        var neighbors = network.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var segment in network.Segments)
        {
            if (neighbors.TryGetValue(segment.StartNodeId, out var from) && neighbors.TryGetValue(segment.EndNodeId, out var to))
            {
                from.Add(segment.EndNodeId);
                to.Add(segment.StartNodeId);
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(network.Nodes[0].Id);
        visited.Add(network.Nodes[0].Id);
        while (queue.TryDequeue(out var current))
        {
            foreach (var neighbor in neighbors[current])
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        Require(visited.Count == network.Nodes.Count, "The road graph is not connected.", errors);
    }

    private static void CheckUnique(IEnumerable<string> identifiers, string kind, ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in identifiers)
        {
            Require(seen.Add(identifier), $"Duplicate {kind} identifier: {identifier}.", errors);
        }
    }

    private static void Require(bool condition, string message, ICollection<string> errors)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}

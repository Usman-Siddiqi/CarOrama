using CarOrama.Core.Roads;
using CarOrama.Core.Geometry;
using System.Text.Json;

namespace CarOrama.Core.Tests.Roads;

public sealed class RoadNetworkTests
{
    private readonly GridRoadNetworkGenerator _generator = new();

    [Fact]
    public void SameSeedProducesIdenticalNetwork()
    {
        var config = new RoadNetworkConfig { Seed = 2026 };

        var first = RoadNetworkSerializer.ToJson(_generator.Generate(config));
        var second = RoadNetworkSerializer.ToJson(_generator.Generate(config));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsVaryTopology()
    {
        var first = _generator.Generate(new RoadNetworkConfig { Seed = 7 });
        var second = _generator.Generate(new RoadNetworkConfig { Seed = 42 });

        Assert.NotEqual(
            first.Segments.Select(segment => segment.Id),
            second.Segments.Select(segment => segment.Id));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    [InlineData(-91)]
    public void GeneratedNetworksSatisfyStructuralInvariants(long seed)
    {
        var network = _generator.Generate(new RoadNetworkConfig { Seed = seed });

        var validation = RoadNetworkValidator.Validate(network);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal(25, network.Nodes.Count);
        Assert.True(network.Segments.Count >= network.Nodes.Count - 1);
        Assert.Equal(network.Segments.Sum(segment => segment.LaneIds.Count), network.Lanes.Count);
        Assert.Equal(network.Lanes.Count, network.SpawnPoints.Count);
        Assert.Contains(network.Segments, segment => segment.Classification == RoadClassification.Local);
        Assert.Contains(network.Segments, segment => segment.Classification == RoadClassification.Arterial);

        foreach (var segment in network.Segments)
        {
            var lanes = segment.LaneIds.Select(network.GetLane).ToArray();
            Assert.Equal(segment.LanesPerDirection * 2, lanes.Length);
            Assert.Equal(
                segment.LanesPerDirection,
                lanes.Count(lane => lane.StartNodeId == segment.StartNodeId && lane.EndNodeId == segment.EndNodeId));
            Assert.Equal(
                segment.LanesPerDirection,
                lanes.Count(lane => lane.StartNodeId == segment.EndNodeId && lane.EndNodeId == segment.StartNodeId));
            Assert.Equal(segment.WidthMeters, lanes.Sum(lane => lane.WidthMeters), 6);
        }
    }

    [Fact]
    public void EveryLaneCanRouteBackToItsReverseDirection()
    {
        var network = _generator.Generate(new RoadNetworkConfig { Seed = 73 });

        foreach (var lane in network.Lanes)
        {
            var reverseLanes = network.Lanes.Where(candidate =>
                candidate.SegmentId == lane.SegmentId &&
                candidate.StartNodeId == lane.EndNodeId &&
                candidate.EndNodeId == lane.StartNodeId);
            foreach (var reverse in reverseLanes)
            {
                var route = RoutePlanner.FindLaneRoute(network, lane.Id, reverse.Id);

                Assert.NotEmpty(route);
                Assert.Equal(lane.Id, route[0]);
                Assert.Equal(reverse.Id, route[^1]);
            }
        }
    }

    [Fact]
    public void DefaultLayoutProvidesStructuredLocalAndArterialRoads()
    {
        var config = new RoadNetworkConfig { Seed = 42 };
        var network = _generator.Generate(config);

        var localRoads = network.Segments.Where(segment => segment.Classification == RoadClassification.Local).ToArray();
        var arterials = network.Segments.Where(segment => segment.Classification == RoadClassification.Arterial).ToArray();

        Assert.NotEmpty(localRoads);
        Assert.NotEmpty(arterials);
        Assert.All(localRoads, segment => Assert.Equal(config.LocalLanesPerDirection, segment.LanesPerDirection));
        Assert.All(arterials, segment => Assert.Equal(config.ArterialLanesPerDirection, segment.LanesPerDirection));
        Assert.Equal((config.GridWidth - 1) + (config.GridHeight - 1), arterials.Length);
        Assert.True(
            arterials.SelectMany(segment => new[] { segment.StartNodeId, segment.EndNodeId }).Distinct().Count() <
            arterials.Length * 2,
            "The arterial segments should form a connected central corridor.");
    }

    [Fact]
    public void DirectedLanesUseRightHandTrafficPlacement()
    {
        var network = _generator.Generate(new RoadNetworkConfig { Seed = 42 });

        foreach (var segment in network.Segments)
        {
            var segmentDirection =
                (network.GetNode(segment.EndNodeId).Position - network.GetNode(segment.StartNodeId).Position)
                .Normalized();
            var worldLeft = new Vector2D(segmentDirection.Y, -segmentDirection.X);
            var segmentMidpoint = Vector2D.Lerp(segment.CenterLine[0], segment.CenterLine[^1], 0.5);

            foreach (var lane in segment.LaneIds.Select(network.GetLane))
            {
                var laneMidpoint = Vector2D.Lerp(lane.CenterLine[0], lane.CenterLine[^1], 0.5);
                var signedOffset = Vector2D.Dot(laneMidpoint - segmentMidpoint, worldLeft);
                var travelsForward = lane.StartNodeId == segment.StartNodeId;

                Assert.True(
                    travelsForward ? signedOffset < 0.0 : signedOffset > 0.0,
                    $"Lane {lane.Id} is not on the right side of its travel direction.");
            }
        }
    }

    [Fact]
    public void LaneEndpointsRespectWidestIncidentRoadAtMixedWidthJunctions()
    {
        var config = new RoadNetworkConfig { Seed = 42 };
        var network = _generator.Generate(config);

        foreach (var lane in network.Lanes)
        {
            var startNode = network.GetNode(lane.StartNodeId);
            var endNode = network.GetNode(lane.EndNodeId);
            var awayFromStart = (endNode.Position - startNode.Position).Normalized();
            var awayFromEnd = (startNode.Position - endNode.Position).Normalized();
            var expectedStartInset = startNode.ConnectedSegmentIds
                .Select(network.GetSegment)
                .Max(segment => segment.WidthMeters * 0.5) + config.IntersectionApproachSetbackMeters;
            var expectedEndInset = endNode.ConnectedSegmentIds
                .Select(network.GetSegment)
                .Max(segment => segment.WidthMeters * 0.5) + config.IntersectionApproachSetbackMeters;

            Assert.Equal(
                expectedStartInset,
                Vector2D.Dot(lane.CenterLine[0] - startNode.Position, awayFromStart),
                6);
            Assert.Equal(
                expectedEndInset,
                Vector2D.Dot(lane.CenterLine[^1] - endNode.Position, awayFromEnd),
                6);
        }
    }

    [Fact]
    public void SerializedRoadSegmentsExposeClassificationAndLaneCapacity()
    {
        var network = _generator.Generate(new RoadNetworkConfig { Seed = 42 });
        using var document = JsonDocument.Parse(RoadNetworkSerializer.ToJson(network));
        var segments = document.RootElement.GetProperty("segments").EnumerateArray().ToArray();

        Assert.Contains(segments, segment =>
            segment.GetProperty("classification").GetString() == nameof(RoadClassification.Local) &&
            segment.GetProperty("lanesPerDirection").GetInt32() == 1);
        Assert.Contains(segments, segment =>
            segment.GetProperty("classification").GetString() == nameof(RoadClassification.Arterial) &&
            segment.GetProperty("lanesPerDirection").GetInt32() == 2);
    }

    [Fact]
    public void ConfigurationRejectsBlocksTooShortForArterialApproaches()
    {
        var config = new RoadNetworkConfig { BlockSizeMeters = 25.0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(config));
    }

    [Fact]
    public void ControlledApproachesExposeStopLinesAndState()
    {
        var network = _generator.Generate(new RoadNetworkConfig { Seed = 42 });

        Assert.NotEmpty(network.TrafficControls);
        foreach (var control in network.TrafficControls)
        {
            var lane = network.GetLane(control.IncomingLaneId);
            Assert.NotNull(lane.StopLineId);
            Assert.Contains(network.StopLines, line => line.Id == lane.StopLineId);
            Assert.Contains(control.State, new[] { "Stop", "Green" });
        }
    }
}

using CarOrama.Core.Roads;

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
        Assert.Equal(network.Segments.Count * 2, network.Lanes.Count);
        Assert.NotEmpty(network.SpawnPoints);
    }

    [Fact]
    public void EveryLaneCanRouteBackToItsReverseDirection()
    {
        var network = _generator.Generate(new RoadNetworkConfig { Seed = 73 });

        foreach (var lane in network.Lanes)
        {
            var reverse = network.Lanes.Single(candidate =>
                candidate.SegmentId == lane.SegmentId && candidate.Id != lane.Id);
            var route = RoutePlanner.FindLaneRoute(network, lane.Id, reverse.Id);

            Assert.NotEmpty(route);
            Assert.Equal(lane.Id, route[0]);
            Assert.Equal(reverse.Id, route[^1]);
        }
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

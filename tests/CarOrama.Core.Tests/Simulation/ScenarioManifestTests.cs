using System.Text.Json;
using System.Text.Json.Serialization;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Simulation;

public sealed class ScenarioManifestTests
{
    [Fact]
    public void BuilderProducesDeterministicDisjointAndValidScenarios()
    {
        var configuration = CreateConfiguration();
        var builder = new ScenarioManifestBuilder();

        var first = builder.Build(configuration);
        var second = builder.Build(configuration);

        Assert.Equal(ToJson(first), ToJson(second));
        Assert.Equal(8, first.Scenarios.Count);
        Assert.Equal(4, first.Scenarios.Count(scenario => scenario.Split == DatasetSplit.Training));
        Assert.Equal(2, first.Scenarios.Count(scenario => scenario.Split == DatasetSplit.Validation));
        Assert.Equal(2, first.Scenarios.Count(scenario => scenario.Split == DatasetSplit.Test));
        Assert.Equal(first.Scenarios.Count, first.Scenarios.Select(scenario => scenario.ScenarioId).Distinct().Count());

        foreach (var scenario in first.Scenarios)
        {
            var network = new GridRoadNetworkGenerator().Generate(new RoadNetworkConfig { Seed = scenario.Seed });
            var spawn = network.SpawnPoints.Single(candidate => candidate.Id == scenario.SpawnPointId);
            Assert.Equal(spawn.LaneId, scenario.LaneIds[0]);
            Assert.Equal(scenario.DestinationLaneId, scenario.LaneIds[^1]);
            Assert.True(scenario.LaneIds.Count >= 3);
            Assert.All(scenario.LaneIds.Zip(scenario.LaneIds.Skip(1)), transition =>
                Assert.True(CarOrama.Core.Geometry.Vector2D.Dot(
                    network.GetLane(transition.First).Direction,
                    network.GetLane(transition.Second).Direction) >= -0.5));
            _ = DrivingRoute.Create(scenario.RouteId, network, scenario.LaneIds);
        }
    }

    [Fact]
    public void ConfigurationRejectsSeedLeakageAcrossSplits()
    {
        var configuration = CreateConfiguration() with
        {
            TestSeeds = [11, 41],
        };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public void ConfigurationAndManifestHaveStableJsonRoundTrips()
    {
        var options = CreateJsonOptions();
        var configuration = CreateConfiguration();
        var configurationJson = JsonSerializer.Serialize(configuration, options);
        var restoredConfiguration = JsonSerializer.Deserialize<ScenarioSplitConfiguration>(configurationJson, options)!;
        restoredConfiguration.Validate();
        var manifest = new ScenarioManifestBuilder().Build(restoredConfiguration);
        var manifestJson = JsonSerializer.Serialize(manifest, options);
        var restoredManifest = JsonSerializer.Deserialize<ScenarioManifest>(manifestJson, options)!;

        restoredManifest.Validate();
        Assert.Equal(ToJson(manifest), ToJson(restoredManifest));
    }

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, CreateJsonOptions());

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static ScenarioSplitConfiguration CreateConfiguration() => new(
        TrainingSeeds: [11, 19],
        ValidationSeeds: [31],
        TestSeeds: [41],
        RoutesPerSeed: 2);
}

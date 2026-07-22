using System.Text.Json;
using CarOrama.Core.Data;
using CarOrama.Core.Roads;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Data;

public sealed class EpisodeDatasetWriterTests
{
    [Fact]
    public void WriterCreatesVersionedTickAlignedCompletedEpisode()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var fixture = CreateFixture();
            var writer = new EpisodeDatasetWriter(
                temporaryRoot,
                "dataset-test",
                "commit-123",
                fixture.Manifest,
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));
            using var episode = writer.BeginEpisode(
                fixture.Scenario.ScenarioId,
                CameraSensorLayout.CreateModel3Inspired().Cameras);
            var reset = fixture.Environment.Reset(fixture.ResetRequest);
            episode.WriteReset(reset);
            var action = DrivingAction.Neutral(0);
            var result = fixture.Environment.Step(action);
            episode.WriteStep(
                action,
                result,
                [new SensorFrameReference(
                    CameraSensorId.WindshieldMain,
                    result.Observation.PhysicsTick,
                    result.Metrics.ElapsedSimulationSeconds,
                    "frames/WindshieldMain/000000000006.png")]);

            var summary = episode.Complete();
            writer.CompleteDataset();

            Assert.Equal(1, summary.RecordedStepCount);
            Assert.Equal(1, summary.RecordedSensorFrameCount);
            Assert.True(File.Exists(Path.Combine(writer.DatasetDirectory, "dataset.json")));
            Assert.True(File.Exists(Path.Combine(writer.DatasetDirectory, "scenario-manifest.json")));
            Assert.True(File.Exists(Path.Combine(episode.EpisodeDirectory, "episode.json")));
            Assert.True(File.Exists(Path.Combine(episode.EpisodeDirectory, "steps.jsonl")));
            Assert.True(File.Exists(Path.Combine(episode.EpisodeDirectory, "summary.json")));
            Assert.True(File.Exists(Path.Combine(episode.EpisodeDirectory, "_COMPLETE")));
            Assert.True(File.Exists(Path.Combine(writer.DatasetDirectory, "_COMPLETE")));
            Assert.False(File.Exists(Path.Combine(episode.EpisodeDirectory, "steps.jsonl.tmp")));

            var lines = File.ReadAllLines(Path.Combine(episode.EpisodeDirectory, "steps.jsonl"));
            Assert.Equal(2, lines.Length);
            using var resetDocument = JsonDocument.Parse(lines[0]);
            using var stepDocument = JsonDocument.Parse(lines[1]);
            Assert.Equal("reset", resetDocument.RootElement.GetProperty("recordType").GetString());
            Assert.Equal("step", stepDocument.RootElement.GetProperty("recordType").GetString());
            Assert.Equal(6, stepDocument.RootElement
                .GetProperty("observation")
                .GetProperty("physicsTick")
                .GetInt64());
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void WriterRejectsOverwriteAndUnsafeFramePaths()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var fixture = CreateFixture();
            var writer = new EpisodeDatasetWriter(
                temporaryRoot,
                "dataset-test",
                "commit-123",
                fixture.Manifest);
            Assert.Throws<IOException>(() => new EpisodeDatasetWriter(
                temporaryRoot,
                "dataset-test",
                "commit-456",
                fixture.Manifest));

            using var episode = writer.BeginEpisode(fixture.Scenario.ScenarioId);
            episode.WriteReset(fixture.Environment.Reset(fixture.ResetRequest));
            var action = DrivingAction.Neutral(0);
            var result = fixture.Environment.Step(action);

            Assert.Throws<ArgumentException>(() => episode.WriteStep(
                action,
                result,
                [new SensorFrameReference(
                    CameraSensorId.Rear,
                    result.Observation.PhysicsTick,
                    result.Metrics.ElapsedSimulationSeconds,
                    "../outside.png")]));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static DatasetFixture CreateFixture()
    {
        var configuration = new ScenarioSplitConfiguration(
            TrainingSeeds: [42],
            ValidationSeeds: [43],
            TestSeeds: [44],
            RoutesPerSeed: 1,
            MaximumControlTicks: 1);
        var manifest = new ScenarioManifestBuilder().Build(configuration);
        var scenario = manifest.Scenarios.Single(candidate => candidate.Split == DatasetSplit.Training);
        manifest = new ScenarioManifest(
            ScenarioManifest.CurrentSchemaVersion,
            SimulationContract.CurrentVersion,
            [scenario]);
        var network = new GridRoadNetworkGenerator().Generate(new RoadNetworkConfig { Seed = scenario.Seed });
        var route = DrivingRoute.Create(scenario.RouteId, network, scenario.LaneIds);
        var simulationScenario = SimulationScenario.Create(
            scenario.ScenarioId,
            scenario.PhysicsTicksPerSecond,
            scenario.ControlTicksPerSecond,
            scenario.MaximumControlTicks);
        var reset = EpisodeResetRequest.Create(
            simulationScenario,
            scenario.Seed,
            scenario.RouteId,
            scenario.SpawnPointId);
        return new DatasetFixture(
            manifest,
            scenario,
            reset,
            new DeterministicDrivingEnvironment(network, route));
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "CarOrama.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record DatasetFixture(
        ScenarioManifest Manifest,
        ScenarioManifestEntry Scenario,
        EpisodeResetRequest ResetRequest,
        DeterministicDrivingEnvironment Environment);
}

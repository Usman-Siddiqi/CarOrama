using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarOrama.Core.Control;
using CarOrama.Core.Data;
using CarOrama.Core.Roads;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;
using CarOrama.Game.Environment;
using CarOrama.Game.Input;
using CarOrama.Game.Sensors;
using CarOrama.Game.Vehicle;
using Godot;

namespace CarOrama.Game.Simulation;

public sealed record GodotRecordingWorld(
    RoadWorld RoadWorld,
    ElectricVehicle Vehicle,
    VehicleCameraRig CameraRig);

/// <summary>
/// Executes manifest-selected reference demonstrations against the real Jolt
/// plant and writes quality-gated, optionally rendered datasets.
/// </summary>
public sealed partial class GodotDatasetRecorder : Node
{
    private readonly BufferedVehicleCommandSource _commandSource;
    private readonly VehicleSpecification _vehicleSpecification;
    private readonly Node _episodeHost;

    public GodotDatasetRecorder(
        BufferedVehicleCommandSource commandSource,
        VehicleSpecification vehicleSpecification,
        Node episodeHost)
    {
        _commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
        _vehicleSpecification = vehicleSpecification ?? throw new ArgumentNullException(nameof(vehicleSpecification));
        _episodeHost = episodeHost ?? throw new ArgumentNullException(nameof(episodeHost));
        Name = "GodotDatasetRecorder";
    }

    public async Task<int> RecordAsync(
        IReadOnlyList<string> arguments,
        Func<long, CameraSensorLayout, Task<GodotRecordingWorld>> prepareWorldAsync)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(prepareWorldAsync);
        var options = ReadNamedOptions(arguments);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var configPath = ResolveRepositoryPath(RequireOption(options, "config"));
        var configuration = JsonSerializer.Deserialize<ScenarioSplitConfiguration>(
            File.ReadAllText(configPath),
            jsonOptions) ?? throw new InvalidDataException($"Could not parse '{configPath}'.");
        configuration.Validate();
        var fullManifest = new ScenarioManifestBuilder().Build(configuration);
        var selectedScenarios = SelectScenarios(fullManifest, options).ToArray();
        if (selectedScenarios.Length == 0)
        {
            throw new ArgumentException("The supplied filters selected no scenarios.");
        }

        var captureCameras = !arguments.Contains("--no-cameras");
        if (captureCameras && IsHeadlessDisplay())
        {
            throw new InvalidOperationException(
                "Camera recording requires a render-capable Godot process; remove --headless or use --no-cameras.");
        }

        var layout = CameraSensorLayout.CreateModel3Inspired(
            options.TryGetValue("camera-width", out var width) ? ParsePositiveInt(width, "camera-width") : 1280,
            options.TryGetValue("camera-height", out var height) ? ParsePositiveInt(height, "camera-height") : 720,
            options.TryGetValue("camera-hz", out var frequency)
                ? ParsePositiveDouble(frequency, "camera-hz")
                : 30.0);
        var manifest = new ScenarioManifest(
            ScenarioManifest.CurrentSchemaVersion,
            SimulationContract.CurrentVersion,
            selectedScenarios);
        manifest.Validate();
        var writer = new EpisodeDatasetWriter(
            ResolveRepositoryPath(RequireOption(options, "output")),
            RequireOption(options, "dataset-id"),
            RequireOption(options, "build-id"),
            manifest);
        var qualityFailures = 0;
        foreach (var definition in selectedScenarios)
        {
            GetTree().Paused = false;
            var world = await prepareWorldAsync(definition.Seed, layout);
            var route = DrivingRoute.Create(definition.RouteId, world.RoadWorld.Network, definition.LaneIds);
            var scenario = SimulationScenario.Create(
                definition.ScenarioId,
                definition.PhysicsTicksPerSecond,
                definition.ControlTicksPerSecond,
                definition.MaximumControlTicks);
            var environment = new GodotDrivingEnvironment(
                world.RoadWorld,
                world.Vehicle,
                route,
                _commandSource,
                _vehicleSpecification);
            _episodeHost.AddChild(environment);
            var controller = CreateReferenceController();
            using var episode = writer.BeginEpisode(
                definition.ScenarioId,
                captureCameras ? layout.Cameras : null);
            var observation = await environment.ResetAsync(EpisodeResetRequest.Create(
                scenario,
                definition.Seed,
                definition.RouteId,
                definition.SpawnPointId));
            episode.WriteReset(observation);
            var scheduler = captureCameras
                ? new CameraCaptureScheduler(layout, scenario.PhysicsTicksPerSecond)
                : null;
            EpisodeStepResult result;
            do
            {
                var action = controller.GetAction(observation);
                var frames = new List<SensorFrameReference>();
                if (scheduler is null)
                {
                    result = await environment.StepAsync(action);
                }
                else
                {
                    result = await environment.StepAsync(action, async (physicsTick, simulationTimeSeconds) =>
                    {
                        var due = scheduler.Advance(physicsTick);
                        if (due.Count > 0)
                        {
                            frames.AddRange(await world.CameraRig.CaptureAsync(
                                episode.EpisodeDirectory,
                                due,
                                physicsTick,
                                simulationTimeSeconds));
                        }
                    });
                }

                episode.WriteStep(action, result, frames);
                observation = result.Observation;
            }
            while (!result.IsTerminal);

            episode.Complete();
            var passed = IsQualityEpisode(result);
            if (!passed)
            {
                qualityFailures++;
            }

            GD.Print(
                $"{definition.ScenarioId}: {result.Termination.Reason}, " +
                $"completion={result.Metrics.RouteCompletionFraction:P1}, " +
                $"steps={result.ControlTick}, frames={(captureCameras ? "recorded" : "disabled")}, " +
                $"quality={(passed ? "PASS" : "FAIL")}");
            GetTree().Paused = false;
            environment.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        writer.CompleteDataset();
        GD.Print(
            $"DATASET RECORDING COMPLETE: {selectedScenarios.Length} episodes at {writer.DatasetDirectory}; " +
            $"quality failures={qualityFailures}.");
        return qualityFailures == 0 ? 0 : 3;
    }

    private PrivilegedRouteFollower CreateReferenceController() => new(
        _vehicleSpecification,
        new PrivilegedRouteFollowerConfig
        {
            MaximumCruiseSpeedMetersPerSecond = 9.5,
            ComfortableDecelerationMetersPerSecondSquared = 4.5,
            MaximumCorneringAccelerationMetersPerSecondSquared = 0.8,
            CrossTrackGain = 1.6,
            HeadingErrorGain = 0.8,
        });

    private static IEnumerable<ScenarioManifestEntry> SelectScenarios(
        ScenarioManifest manifest,
        IReadOnlyDictionary<string, string> options)
    {
        IEnumerable<ScenarioManifestEntry> selected = manifest.Scenarios;
        if (options.TryGetValue("split", out var splitText))
        {
            if (!Enum.TryParse<DatasetSplit>(splitText, true, out var split) || !Enum.IsDefined(split))
            {
                throw new ArgumentException($"Unknown dataset split '{splitText}'.");
            }

            selected = selected.Where(scenario => scenario.Split == split);
        }

        if (options.TryGetValue("scenario", out var scenarioId))
        {
            selected = selected.Where(scenario => scenario.ScenarioId == scenarioId);
        }

        if (options.TryGetValue("max-episodes", out var maximumText))
        {
            selected = selected.Take(ParsePositiveInt(maximumText, "max-episodes"));
        }

        return selected;
    }

    private static bool IsQualityEpisode(EpisodeStepResult result)
    {
        var metrics = result.Metrics;
        return result.Termination.Reason == EpisodeTerminationReason.RouteCompleted &&
            metrics.CollisionCount == 0 &&
            metrics.LaneDepartureCount == 0 &&
            metrics.RedLightViolationCount == 0 &&
            metrics.StopSignViolationCount == 0 &&
            metrics.SpeedingDurationSeconds <= 1e-9;
    }

    private static Dictionary<string, string> ReadNamedOptions(IReadOnlyList<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal) ||
                arguments[index] is "--record-dataset" or "--no-cameras")
            {
                continue;
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            options[arguments[index][2..]] = arguments[++index];
        }

        return options;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option --{name}.");

    private static int ParsePositiveInt(string value, string name) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"--{name} must be a positive integer.");

    private static double ParsePositiveDouble(string value, string name) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) && parsed > 0.0
            ? parsed
            : throw new ArgumentException($"--{name} must be a positive finite number.");

    private static string ResolveRepositoryPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var projectDirectory = ProjectSettings.GlobalizePath("res://");
        var repositoryDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "..", ".."));
        return Path.GetFullPath(Path.Combine(repositoryDirectory, path));
    }

    private static bool IsHeadlessDisplay() => string.Equals(
        DisplayServer.GetName(),
        "headless",
        StringComparison.OrdinalIgnoreCase);
}

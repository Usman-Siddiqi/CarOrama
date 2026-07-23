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
        var livePreview = arguments.Contains("--live-preview");
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
        var enableRecoveryPerturbations = arguments.Contains("--recovery-perturbations");
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
            var controller = CreateReferenceController(definition);
            var recoverySchedule = enableRecoveryPerturbations && definition.Split == DatasetSplit.Training
                ? new RecoveryPerturbationSchedule(
                    definition.ControlTicksPerSecond,
                    StableDriverSeed(definition.Seed, definition.ScenarioId))
                : null;
            var perturbationTicks = 0;
            using var episode = writer.BeginEpisode(
                definition.ScenarioId,
                captureCameras ? layout.Cameras : null);
            var observation = await environment.ResetAsync(EpisodeResetRequest.Create(
                scenario,
                definition.Seed,
                definition.RouteId,
                definition.SpawnPointId));
            episode.WriteReset(observation);
            if (livePreview)
            {
                GetTree().Paused = false;
            }
            var cameraLayout = captureCameras ? layout : null;
            if (cameraLayout is not null)
            {
                ValidateControlAlignedCameraRates(cameraLayout, scenario.ControlTicksPerSecond);
            }
            EpisodeStepResult result;
            do
            {
                var teacherAction = controller.GetAction(observation, route);
                var perturbation = recoverySchedule?.Apply(observation, teacherAction) ??
                    new RecoveryPerturbationDecision(teacherAction, false, false);
                var action = perturbation.ExecutedAction;
                if (perturbation.IsPerturbation)
                {
                    perturbationTicks++;
                }

                var lightingCommand = controller.GetLightingCommand(observation, route);
                var frames = new List<SensorFrameReference>();
                if (cameraLayout is not null)
                {
                    // Capture while the environment is paused at the exact
                    // observation used to choose this action. Disturbance ticks
                    // intentionally have no frames; the following teacher ticks
                    // become supervised recovery examples.
                    var due = CamerasDueAtControlTick(
                        cameraLayout,
                        scenario.ControlTicksPerSecond,
                        observation.ControlTick);
                    if (due.Count > 0 && !perturbation.SuppressSensorFrames)
                    {
                        frames.AddRange(await world.CameraRig.CaptureAsync(
                            episode.EpisodeDirectory,
                            due,
                            observation.PhysicsTick,
                            observation.PhysicsTick / (double)scenario.PhysicsTicksPerSecond));
                    }
                }

                result = await environment.StepAsync(action, lightingCommand: lightingCommand);
                episode.WriteStep(action, result, frames);
                observation = result.Observation;
                if (livePreview && !result.IsTerminal)
                {
                    GetTree().Paused = false;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            while (!result.IsTerminal);

            // The preview can remain open after recording, so replace the final
            // driving command with a brake hold before releasing the environment.
            _commandSource.Set(VehicleCommand.Create(
                steering: 0.0,
                throttle: 0.0,
                regenerativeBrake: 0.0,
                frictionBrake: 1.0));

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
                $"recoveryPerturbationTicks={perturbationTicks}, quality={(passed ? "PASS" : "FAIL")}");
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

    private static void ValidateControlAlignedCameraRates(
        CameraSensorLayout layout,
        int controlTicksPerSecond)
    {
        foreach (var camera in layout.Cameras)
        {
            var interval = controlTicksPerSecond / camera.CaptureFrequencyHertz;
            if (camera.CaptureFrequencyHertz > controlTicksPerSecond ||
                Math.Abs(interval - Math.Round(interval)) > 1e-9)
            {
                throw new ArgumentException(
                    $"Camera '{camera.Id}' frequency {camera.CaptureFrequencyHertz} Hz must divide " +
                    $"the {controlTicksPerSecond} Hz control rate for action-aligned imitation data.");
            }
        }
    }

    private static IReadOnlyList<CameraSensorId> CamerasDueAtControlTick(
        CameraSensorLayout layout,
        int controlTicksPerSecond,
        long controlTick) => layout.Cameras
            .Where(camera => controlTick % (long)Math.Round(
                controlTicksPerSecond / camera.CaptureFrequencyHertz) == 0)
            .Select(camera => camera.Id)
            .ToArray();

    private PrivilegedRouteFollower CreateReferenceController(ScenarioManifestEntry definition) => new(
        _vehicleSpecification,
        new PrivilegedRouteFollowerConfig
        {
            MaximumCruiseSpeedMetersPerSecond = 16.3,
            MaximumAccelerationMetersPerSecondSquared = 2.0,
            MaximumCombinedAccelerationG = 0.32,
            SpeedLimitMarginMetersPerSecond = 0.4,
            CruiseSpeedVariationMetersPerSecond = 0.75,
            DriverProfileSeed = StableDriverSeed(definition.Seed, definition.ScenarioId),
            ComfortableDecelerationMetersPerSecondSquared = 3.0,
            TargetStoppingDecelerationMetersPerSecondSquared = 2.8,
            MaximumCorneringAccelerationMetersPerSecondSquared = 1.25,
            MaximumLongitudinalJerkMetersPerSecondCubed = 4.0,
            CrossTrackGain = 2.2,
            HeadingErrorGain = 1.4,
            StopLineBufferMeters = 2.2,
        });

    private static long StableDriverSeed(long scenarioSeed, string scenarioId)
    {
        unchecked
        {
            var hash = 14695981039346656037UL ^ (ulong)scenarioSeed;
            foreach (var character in scenarioId)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            return (long)hash;
        }
    }

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

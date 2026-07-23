using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarOrama.Core.Control;
using CarOrama.Core.Roads;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;
using CarOrama.Game.Input;
using Godot;

namespace CarOrama.Game.Simulation;

/// <summary>
/// Evaluates a learned policy in closed loop against disjoint deterministic
/// routes. Unlike offline imitation metrics, every action changes the next
/// camera observation and exposes compounding control errors.
/// </summary>
public sealed partial class GodotPolicyEvaluator : Node
{
    private readonly BufferedVehicleCommandSource _commandSource;
    private readonly VehicleSpecification _vehicleSpecification;
    private readonly Node _episodeHost;

    public GodotPolicyEvaluator(
        BufferedVehicleCommandSource commandSource,
        VehicleSpecification vehicleSpecification,
        Node episodeHost)
    {
        _commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
        _vehicleSpecification = vehicleSpecification ?? throw new ArgumentNullException(nameof(vehicleSpecification));
        _episodeHost = episodeHost ?? throw new ArgumentNullException(nameof(episodeHost));
        Name = "GodotPolicyEvaluator";
    }

    public async Task<int> EvaluateAsync(
        IReadOnlyList<string> arguments,
        OnnxVehiclePolicy policy,
        Func<long, CameraSensorLayout, Task<GodotRecordingWorld>> prepareWorldAsync)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(prepareWorldAsync);
        var options = ReadNamedOptions(arguments);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var configPath = ResolveRepositoryPath(RequireOption(options, "config"));
        var configuration = JsonSerializer.Deserialize<ScenarioSplitConfiguration>(
            File.ReadAllText(configPath),
            jsonOptions) ?? throw new InvalidDataException($"Could not parse '{configPath}'.");
        configuration.Validate();
        var split = options.TryGetValue("split", out var splitText)
            ? ParseSplit(splitText)
            : DatasetSplit.Validation;
        var selected = new ScenarioManifestBuilder().Build(configuration).Scenarios
            .Where(scenario => scenario.Split == split);
        if (options.TryGetValue("scenario", out var scenarioId))
        {
            selected = selected.Where(scenario => scenario.ScenarioId == scenarioId);
        }

        var maximumEpisodes = options.TryGetValue("max-episodes", out var maximumText)
            ? ParsePositiveInt(maximumText, "max-episodes")
            : 1;
        var scenarios = selected.Take(maximumEpisodes).ToArray();
        if (scenarios.Length == 0)
        {
            throw new ArgumentException("The supplied policy evaluation filters selected no scenarios.");
        }

        var layout = CameraSensorLayout.CreateModel3Inspired(
            policy.ImageWidth,
            policy.ImageHeight,
            captureFrequencyHertz: configuration.ControlTicksPerSecond);
        if (!policy.Cameras.SequenceEqual(layout.Cameras.Select(camera => camera.Id)))
        {
            throw new InvalidDataException("Policy camera order does not match the configured vehicle rig.");
        }

        var episodeReports = new List<PolicyEpisodeReport>();
        foreach (var definition in scenarios)
        {
            GetTree().Paused = false;
            var world = await prepareWorldAsync(definition.Seed, layout);
            var route = DrivingRoute.Create(definition.RouteId, world.RoadWorld.Network, definition.LaneIds);
            var maximumControlTicks = options.TryGetValue("maximum-control-ticks", out var tickLimitText)
                ? Math.Min(definition.MaximumControlTicks, ParsePositiveInt(tickLimitText, "maximum-control-ticks"))
                : definition.MaximumControlTicks;
            var policyFrequencyHertz = options.TryGetValue("policy-frequency-hz", out var frequencyText)
                ? ParsePositiveInt(frequencyText, "policy-frequency-hz")
                : 10;
            if (definition.ControlTicksPerSecond % policyFrequencyHertz != 0)
            {
                throw new ArgumentException("--policy-frequency-hz must divide the scenario control frequency exactly.");
            }

            var inferenceInterval = definition.ControlTicksPerSecond / policyFrequencyHertz;
            var scenario = SimulationScenario.Create(
                definition.ScenarioId,
                definition.PhysicsTicksPerSecond,
                definition.ControlTicksPerSecond,
                maximumControlTicks);
            var environment = new GodotDrivingEnvironment(
                world.RoadWorld,
                world.Vehicle,
                route,
                _commandSource,
                _vehicleSpecification);
            _episodeHost.AddChild(environment);
            var observation = await environment.ResetAsync(EpisodeResetRequest.Create(
                scenario,
                definition.Seed,
                definition.RouteId,
                definition.SpawnPointId));
            var inferenceMilliseconds = new List<double>();
            EpisodeStepResult result;
            DrivingAction? latestPolicyAction = null;
            do
            {
                DrivingAction action;
                if (latestPolicyAction is null || observation.ControlTick % inferenceInterval == 0)
                {
                    var stopwatch = Stopwatch.StartNew();
                    action = await policy.GetActionAsync(observation, world.CameraRig);
                    stopwatch.Stop();
                    inferenceMilliseconds.Add(stopwatch.Elapsed.TotalMilliseconds);
                    latestPolicyAction = action;
                }
                else
                {
                    action = DrivingAction.Create(
                        observation.ControlTick,
                        latestPolicyAction.Value.Steering,
                        latestPolicyAction.Value.LongitudinalAccelerationMetersPerSecondSquared);
                }

                result = await environment.StepAsync(action);
                observation = result.Observation;
                if (result.ControlTick % 100 == 0)
                {
                    GD.Print(
                        $"POLICY PROGRESS {definition.ScenarioId}: tick={result.ControlTick}/{scenario.MaximumControlTicks}, " +
                        $"completion={result.Metrics.RouteCompletionFraction:P1}, " +
                        $"speed={result.Observation.EgoVehicle.SpeedMetersPerSecond:F1} m/s");
                }
            }
            while (!result.IsTerminal);

            _commandSource.Set(VehicleCommand.Create(
                steering: 0.0,
                throttle: 0.0,
                regenerativeBrake: 0.0,
                frictionBrake: 1.0));
            var passed = IsSafeCompletion(result);
            episodeReports.Add(new PolicyEpisodeReport(
                definition.ScenarioId,
                result.Termination.Kind.ToString(),
                result.Termination.Reason.ToString(),
                result.ControlTick,
                result.Metrics.RouteCompletionFraction,
                result.Metrics.CollisionCount,
                result.Metrics.LaneDepartureCount,
                result.Metrics.RedLightViolationCount,
                result.Metrics.StopSignViolationCount,
                result.Metrics.SpeedingDurationSeconds,
                inferenceMilliseconds.Average(),
                inferenceMilliseconds.Max(),
                passed));
            GD.Print(
                $"POLICY {definition.ScenarioId}: {result.Termination.Reason}, " +
                $"completion={result.Metrics.RouteCompletionFraction:P1}, " +
                $"inference={inferenceMilliseconds.Average():F1} ms, quality={(passed ? "PASS" : "FAIL")}");
            GetTree().Paused = false;
            environment.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var outputPath = ResolveRepositoryPath(
            options.TryGetValue("evaluation-output", out var configuredOutput)
                ? configuredOutput
                : "artifacts/evaluations/policy-evaluation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var report = new PolicyEvaluationReport(
            1,
            DateTimeOffset.UtcNow,
            policy.CheckpointEpoch,
            split.ToString(),
            episodeReports,
            episodeReports.All(episode => episode.Passed));
        var temporaryPath = outputPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        File.Move(temporaryPath, outputPath, overwrite: true);
        GD.Print($"POLICY EVALUATION COMPLETE: {outputPath}");
        return report.Passed ? 0 : 4;
    }

    private static bool IsSafeCompletion(EpisodeStepResult result) =>
        result.Termination.Reason == EpisodeTerminationReason.RouteCompleted &&
        result.Metrics.CollisionCount == 0 &&
        result.Metrics.LaneDepartureCount == 0 &&
        result.Metrics.RedLightViolationCount == 0 &&
        result.Metrics.StopSignViolationCount == 0 &&
        result.Metrics.SpeedingDurationSeconds <= 1e-9;

    private static DatasetSplit ParseSplit(string value) =>
        Enum.TryParse<DatasetSplit>(value, ignoreCase: true, out var split) && Enum.IsDefined(split)
            ? split
            : throw new ArgumentException($"Unknown dataset split '{value}'.");

    private static Dictionary<string, string> ReadNamedOptions(IReadOnlyList<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index].StartsWith("--", StringComparison.Ordinal) &&
                !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[arguments[index][2..]] = arguments[++index];
            }
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

    private sealed record PolicyEpisodeReport(
        string ScenarioId,
        string TerminationKind,
        string TerminationReason,
        long ControlTicks,
        double RouteCompletionFraction,
        int CollisionCount,
        int LaneDepartureCount,
        int RedLightViolationCount,
        int StopSignViolationCount,
        double SpeedingDurationSeconds,
        double MeanInferenceMilliseconds,
        double MaximumInferenceMilliseconds,
        bool Passed);

    private sealed record PolicyEvaluationReport(
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        int CheckpointEpoch,
        string Split,
        IReadOnlyList<PolicyEpisodeReport> Episodes,
        bool Passed);
}
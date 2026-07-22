using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarOrama.Core.Control;
using CarOrama.Core.Data;
using CarOrama.Core.Roads;
using CarOrama.Core.Simulation;

return await DatasetCommand.RunAsync(args);

internal static class DatasetCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                WriteUsage();
                return Task.FromResult(args.Length == 0 ? 2 : 0);
            }

            var options = ParseOptions(args.Skip(1).ToArray());
            return Task.FromResult(args[0] switch
            {
                "manifest" => ExportManifest(options),
                "record-reference" => RecordReferenceEpisodes(options),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return Task.FromResult(1);
        }
    }

    private static int ExportManifest(IReadOnlyDictionary<string, string> options)
    {
        var configuration = LoadConfiguration(Require(options, "config"));
        var manifest = new ScenarioManifestBuilder().Build(configuration);
        var outputPath = Path.GetFullPath(Require(options, "output"));
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException($"Output already exists: '{outputPath}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        Console.WriteLine($"Wrote {manifest.Scenarios.Count} scenarios to {outputPath}");
        return 0;
    }

    private static int RecordReferenceEpisodes(IReadOnlyDictionary<string, string> options)
    {
        var configuration = LoadConfiguration(Require(options, "config"));
        var manifest = new ScenarioManifestBuilder().Build(configuration);
        var selectedScenarios = SelectScenarios(manifest, options).ToArray();
        if (selectedScenarios.Length == 0)
        {
            throw new ArgumentException("The supplied filters selected no scenarios.");
        }

        var selectedManifest = new ScenarioManifest(
            ScenarioManifest.CurrentSchemaVersion,
            SimulationContract.CurrentVersion,
            selectedScenarios);
        selectedManifest.Validate();
        var writer = new EpisodeDatasetWriter(
            Require(options, "output"),
            Require(options, "dataset-id"),
            Require(options, "build-id"),
            selectedManifest);
        var completedRoutes = 0;
        var qualityEpisodes = 0;
        foreach (var definition in selectedScenarios)
        {
            var network = new GridRoadNetworkGenerator().Generate(new RoadNetworkConfig { Seed = definition.Seed });
            var route = DrivingRoute.Create(definition.RouteId, network, definition.LaneIds);
            var scenario = SimulationScenario.Create(
                definition.ScenarioId,
                definition.PhysicsTicksPerSecond,
                definition.ControlTicksPerSecond,
                definition.MaximumControlTicks);
            var resetRequest = EpisodeResetRequest.Create(
                scenario,
                definition.Seed,
                definition.RouteId,
                definition.SpawnPointId);
            var environment = new DeterministicDrivingEnvironment(network, route);
            var controller = new PrivilegedRouteFollower();
            using var episode = writer.BeginEpisode(definition.ScenarioId);
            var observation = environment.Reset(resetRequest);
            episode.WriteReset(observation);
            EpisodeStepResult result;
            do
            {
                var action = controller.GetAction(observation);
                result = environment.Step(action);
                episode.WriteStep(action, result);
                observation = result.Observation;
            }
            while (!result.IsTerminal);

            episode.Complete();
            if (result.Termination.Reason == EpisodeTerminationReason.RouteCompleted)
            {
                completedRoutes++;
            }

            var passedQuality = result.Termination.Reason == EpisodeTerminationReason.RouteCompleted &&
                result.Metrics.CollisionCount == 0 &&
                result.Metrics.LaneDepartureCount == 0 &&
                result.Metrics.RedLightViolationCount == 0 &&
                result.Metrics.StopSignViolationCount == 0 &&
                result.Metrics.SpeedingDurationSeconds <= 1e-9;
            if (passedQuality)
            {
                qualityEpisodes++;
            }

            Console.WriteLine(
                $"{definition.ScenarioId}: {result.Termination.Reason}, " +
                $"completion={result.Metrics.RouteCompletionFraction:P1}, " +
                $"steps={result.ControlTick}, " +
                $"quality={(passedQuality ? "PASS" : "FAIL")}");
        }

        writer.CompleteDataset();
        Console.WriteLine(
            $"Recorded {selectedScenarios.Length} episodes to {writer.DatasetDirectory}; " +
            $"route completions={completedRoutes}/{selectedScenarios.Length}, " +
            $"quality passes={qualityEpisodes}/{selectedScenarios.Length}.");
        return completedRoutes == selectedScenarios.Length && qualityEpisodes == selectedScenarios.Length ? 0 : 3;
    }

    private static IEnumerable<ScenarioManifestEntry> SelectScenarios(
        ScenarioManifest manifest,
        IReadOnlyDictionary<string, string> options)
    {
        IEnumerable<ScenarioManifestEntry> selected = manifest.Scenarios;
        if (options.TryGetValue("split", out var splitText))
        {
            if (!Enum.TryParse<DatasetSplit>(splitText, ignoreCase: true, out var split) || !Enum.IsDefined(split))
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
            if (!int.TryParse(maximumText, out var maximum) || maximum <= 0)
            {
                throw new ArgumentException("--max-episodes must be a positive integer.");
            }

            selected = selected.Take(maximum);
        }

        return selected;
    }

    private static ScenarioSplitConfiguration LoadConfiguration(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var configuration = JsonSerializer.Deserialize<ScenarioSplitConfiguration>(
            File.ReadAllText(fullPath),
            JsonOptions) ?? throw new InvalidDataException($"Could not parse scenario configuration '{fullPath}'.");
        configuration.Validate();
        return configuration;
    }

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
            {
                throw new ArgumentException($"Expected --name value option near '{key}'.");
            }

            if (!options.TryAdd(key[2..], args[index + 1]))
            {
                throw new ArgumentException($"Option '{key}' was supplied more than once.");
            }
        }

        return options;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option --{name}.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            CarOrama dataset tools

              manifest --config <scenario-splits.json> --output <manifest.json>

              record-reference --config <scenario-splits.json> --output <directory>
                --dataset-id <id> --build-id <source revision>
                [--split Training|Validation|Test] [--scenario <id>] [--max-episodes <count>]

            Outputs are never overwritten. Reference recording returns exit code 3 if any
            selected baseline episode fails to complete its route.
            """);
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Data;

/// <summary>
/// Writes crash-detectable episode folders. A completed episode contains a
/// descriptor, JSON Lines reset/step stream, terminal summary, and `_COMPLETE`
/// marker; interrupted `.tmp` streams are never mistaken for finished data.
/// </summary>
public sealed class EpisodeDatasetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ScenarioManifest _manifest;
    private readonly string _datasetDirectory;

    public EpisodeDatasetWriter(
        string outputDirectory,
        string datasetId,
        string buildId,
        ScenarioManifest manifest,
        DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        ValidatePathSegment(datasetId, nameof(datasetId));
        if (string.IsNullOrWhiteSpace(buildId))
        {
            throw new ArgumentException("A source build identifier is required.", nameof(buildId));
        }

        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        manifest.Validate();
        _datasetDirectory = Path.Combine(Path.GetFullPath(outputDirectory), datasetId);
        if (Directory.Exists(_datasetDirectory) || File.Exists(_datasetDirectory))
        {
            throw new IOException($"Dataset output already exists: '{_datasetDirectory}'.");
        }

        Directory.CreateDirectory(_datasetDirectory);
        WriteJson(Path.Combine(_datasetDirectory, "scenario-manifest.json"), manifest);
        WriteJson(
            Path.Combine(_datasetDirectory, "dataset.json"),
            new DatasetDescriptor(
                EpisodeDataSchema.CurrentVersion,
                SimulationContract.CurrentVersion,
                datasetId,
                buildId.Trim(),
                createdAtUtc ?? DateTimeOffset.UtcNow,
                "scenario-manifest.json",
                "+X east, +Y south, heading positive clockwise",
                "SI (metres, seconds, radians, watts, watt-hours)"));
    }

    public string DatasetDirectory => _datasetDirectory;

    public EpisodeSession BeginEpisode(
        string scenarioId,
        IReadOnlyList<CameraSensorSpecification>? cameras = null)
    {
        var scenario = _manifest.Scenarios.SingleOrDefault(candidate => candidate.ScenarioId == scenarioId) ??
            throw new ArgumentException($"Scenario '{scenarioId}' is not in this dataset manifest.", nameof(scenarioId));
        ValidatePathSegment(scenarioId, nameof(scenarioId));
        var splitDirectory = Path.Combine(_datasetDirectory, scenario.Split.ToString().ToLowerInvariant());
        var episodeDirectory = Path.Combine(splitDirectory, scenarioId);
        if (Directory.Exists(episodeDirectory) || File.Exists(episodeDirectory))
        {
            throw new IOException($"Episode output already exists: '{episodeDirectory}'.");
        }

        Directory.CreateDirectory(episodeDirectory);
        var descriptor = new EpisodeDescriptor(
            EpisodeDataSchema.CurrentVersion,
            scenario.ScenarioId,
            scenario.Split,
            scenario.Seed,
            scenario.RouteId,
            scenario.SpawnPointId,
            scenario.DestinationLaneId,
            scenario.LaneIds,
            scenario.PhysicsTicksPerSecond,
            scenario.ControlTicksPerSecond,
            scenario.MaximumControlTicks,
            cameras?.Select(CameraCalibrationRecord.From).ToArray() ?? []);
        WriteJson(Path.Combine(episodeDirectory, "episode.json"), descriptor);
        return new EpisodeSession(episodeDirectory, scenario, JsonOptions);
    }

    public void CompleteDataset()
    {
        var missing = _manifest.Scenarios
            .Where(scenario => !File.Exists(Path.Combine(
                _datasetDirectory,
                scenario.Split.ToString().ToLowerInvariant(),
                scenario.ScenarioId,
                "_COMPLETE")))
            .Select(scenario => scenario.ScenarioId)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Cannot complete a dataset with unfinished episodes: {string.Join(", ", missing)}.");
        }

        var marker = Path.Combine(_datasetDirectory, "_COMPLETE");
        if (File.Exists(marker))
        {
            throw new InvalidOperationException("The dataset has already been completed.");
        }

        File.WriteAllText(marker, string.Empty, Encoding.UTF8);
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, Encoding.UTF8);
    }

    private static void ValidatePathSegment(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("A safe, non-empty path segment is required.", parameterName);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public sealed class EpisodeSession : IDisposable
    {
        private readonly ScenarioManifestEntry _scenario;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _temporaryStepsPath;
        private readonly StreamWriter _stepsWriter;
        private EpisodeStepResult? _lastResult;
        private bool _resetWritten;
        private bool _completed;
        private int _stepCount;
        private int _sensorFrameCount;

        internal EpisodeSession(
            string episodeDirectory,
            ScenarioManifestEntry scenario,
            JsonSerializerOptions jsonOptions)
        {
            EpisodeDirectory = episodeDirectory;
            _scenario = scenario;
            _jsonOptions = jsonOptions;
            _temporaryStepsPath = Path.Combine(episodeDirectory, "steps.jsonl.tmp");
            _stepsWriter = new StreamWriter(_temporaryStepsPath, append: false, new UTF8Encoding(false));
        }

        public string EpisodeDirectory { get; }

        public void WriteReset(PrivilegedObservation observation)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            if (_resetWritten || observation.ControlTick != 0 || observation.PhysicsTick != 0)
            {
                throw new InvalidOperationException("Exactly one tick-zero reset observation must begin an episode.");
            }

            WriteLine(EpisodeResetDataRecord.Create(observation));
            _resetWritten = true;
        }

        public void WriteStep(
            DrivingAction action,
            EpisodeStepResult result,
            IReadOnlyList<SensorFrameReference>? sensorFrames = null)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            ArgumentNullException.ThrowIfNull(result);
            if (!_resetWritten)
            {
                throw new InvalidOperationException("Write the reset observation before episode steps.");
            }

            if (action.ControlTick != _stepCount || result.ControlTick != action.ControlTick + 1)
            {
                throw new InvalidOperationException("Episode actions and results must be contiguous and tick aligned.");
            }

            var frames = sensorFrames ?? [];
            foreach (var frame in frames)
            {
                if (frame.PhysicsTick > result.Observation.PhysicsTick ||
                    !double.IsFinite(frame.SimulationTimeSeconds) || frame.SimulationTimeSeconds < 0.0 ||
                    string.IsNullOrWhiteSpace(frame.RelativePath) || Path.IsPathRooted(frame.RelativePath) ||
                    frame.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
                {
                    throw new ArgumentException("Sensor frame references must be safe and aligned to the recorded step.", nameof(sensorFrames));
                }
            }

            WriteLine(EpisodeStepDataRecord.Create(action, result, frames));
            _lastResult = result;
            _stepCount++;
            _sensorFrameCount += frames.Count;
        }

        public EpisodeSummary Complete()
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            if (_lastResult is null || !_lastResult.IsTerminal)
            {
                throw new InvalidOperationException("Only a terminal or truncated episode can be completed.");
            }

            _stepsWriter.Flush();
            _stepsWriter.Dispose();
            var stepsPath = Path.Combine(EpisodeDirectory, "steps.jsonl");
            File.Move(_temporaryStepsPath, stepsPath);
            var summary = new EpisodeSummary(
                EpisodeDataSchema.CurrentVersion,
                _scenario.ScenarioId,
                _lastResult.ControlTick,
                EpisodeMetricsRecord.From(_lastResult.Metrics),
                new EpisodeTerminationRecord(
                    _lastResult.Termination.Kind,
                    _lastResult.Termination.Reason,
                    _lastResult.Termination.Detail),
                _stepCount,
                _sensorFrameCount);
            File.WriteAllText(
                Path.Combine(EpisodeDirectory, "summary.json"),
                JsonSerializer.Serialize(summary, _jsonOptions) + Environment.NewLine,
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(EpisodeDirectory, "_COMPLETE"), string.Empty, Encoding.UTF8);
            _completed = true;
            return summary;
        }

        public void Dispose()
        {
            if (!_completed)
            {
                _stepsWriter.Dispose();
            }
        }

        private void WriteLine<T>(T value)
        {
            var compactOptions = new JsonSerializerOptions(_jsonOptions) { WriteIndented = false };
            _stepsWriter.WriteLine(JsonSerializer.Serialize(value, compactOptions));
        }
    }
}

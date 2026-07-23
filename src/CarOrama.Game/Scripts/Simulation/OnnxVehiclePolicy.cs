using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;
using CarOrama.Game.Sensors;
using Godot;
using Microsoft.ML.OnnxRuntime;

namespace CarOrama.Game.Simulation;

/// <summary>
/// Runs a trained multi-camera policy without exposing road geometry, traffic
/// control state, or other privileged simulator values to the neural network.
/// The three auxiliary values match the training contract: onboard speed and a
/// local route lookahead vector supplied by the navigation layer.
/// </summary>
public sealed class OnnxVehiclePolicy : IDisposable
{
    private static readonly object NativeLoadLock = new();
    private static nint _nativeRuntimeHandle;
    private readonly InferenceSession _session;
    private readonly CameraSensorId[] _cameras;
    private readonly float[] _targetMean;
    private readonly float[] _targetStandardDeviation;
    private readonly string[] _inputNames = ["images", "auxiliary"];
    private readonly string[] _outputNames = ["normalized_action"];

    public OnnxVehiclePolicy(string modelPath, string metadataPath)
    {
        EnsureNativeRuntimeLoaded();
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The ONNX policy does not exist.", modelPath);
        }

        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("The policy metadata does not exist.", metadataPath);
        }

        var metadata = JsonSerializer.Deserialize<PolicyMetadata>(
            File.ReadAllText(metadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new InvalidDataException($"Could not parse policy metadata '{metadataPath}'.");
        metadata.Validate();
        _cameras = metadata.Cameras
            .Select(camera => Enum.TryParse<CameraSensorId>(camera, ignoreCase: false, out var id) && Enum.IsDefined(id)
                ? id
                : throw new InvalidDataException($"Policy metadata contains unknown camera '{camera}'."))
            .ToArray();
        _targetMean = metadata.TargetMean.Select(value => (float)value).ToArray();
        _targetStandardDeviation = metadata.TargetStandardDeviation.Select(value => (float)value).ToArray();
        ImageWidth = metadata.ImageWidth;
        ImageHeight = metadata.ImageHeight;
        CheckpointEpoch = metadata.CheckpointEpoch;

        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
        };
        _session = new InferenceSession(Path.GetFullPath(modelPath), sessionOptions);
        if (!_session.InputNames.SequenceEqual(_inputNames, StringComparer.Ordinal) ||
            !_session.OutputNames.SequenceEqual(_outputNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The ONNX model inputs or outputs do not match the driving-policy contract.");
        }
    }

    public int ImageWidth { get; }

    public int ImageHeight { get; }

    public int CheckpointEpoch { get; }

    public IReadOnlyList<CameraSensorId> Cameras => _cameras;

    public async Task<DrivingAction> GetActionAsync(
        PrivilegedObservation observation,
        VehicleCameraRig cameraRig)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(cameraRig);
        var images = await cameraRig.CaptureNormalizedTensorAsync(_cameras, ImageWidth, ImageHeight);
        var auxiliary = CreateNavigationFeatures(observation);
        using var imageValue = OrtValue.CreateTensorValueFromMemory(
            images,
            [1, _cameras.Length, 3, ImageHeight, ImageWidth]);
        using var auxiliaryValue = OrtValue.CreateTensorValueFromMemory(auxiliary, [1, 3]);
        using var runOptions = new RunOptions();
        using var outputs = _session.Run(
            runOptions,
            _inputNames,
            [imageValue, auxiliaryValue],
            _outputNames);
        var normalizedAction = outputs.First().GetTensorDataAsSpan<float>().ToArray();
        if (normalizedAction.Length != 2 || !float.IsFinite(normalizedAction[0]) || !float.IsFinite(normalizedAction[1]))
        {
            throw new InvalidOperationException("The learned policy returned an invalid driving action.");
        }

        var steering = (normalizedAction[0] * _targetStandardDeviation[0]) + _targetMean[0];
        var acceleration = (normalizedAction[1] * _targetStandardDeviation[1]) + _targetMean[1];
        return DrivingAction.Create(observation.ControlTick, steering, acceleration);
    }

    public void Dispose() => _session.Dispose();

    private static void EnsureNativeRuntimeLoaded()
    {
        lock (NativeLoadLock)
        {
            if (_nativeRuntimeHandle != 0)
            {
                return;
            }

            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"ONNX policy inference does not support {RuntimeInformation.ProcessArchitecture} on Windows."),
            };
            var assemblyLocation = typeof(OnnxVehiclePolicy).Assembly.Location;
            var projectDirectory = ProjectSettings.GlobalizePath("res://");
            var candidateDirectories = new[]
            {
                string.IsNullOrWhiteSpace(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation),
                Path.Combine(projectDirectory, ".godot", "mono", "temp", "bin", "Debug"),
                Path.Combine(projectDirectory, ".godot", "mono", "temp", "bin", "Release"),
                AppContext.BaseDirectory,
            };
            var nativePath = candidateDirectories
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => Path.Combine(
                    directory!,
                    "runtimes",
                    architecture,
                    "native",
                    "onnxruntime.dll"))
                .FirstOrDefault(File.Exists) ??
                throw new FileNotFoundException(
                    "The packaged ONNX Runtime native library could not be found in the Godot build output.");

            // Windows includes an older system onnxruntime.dll. Load the NuGet
            // package's matching native binary by absolute path before the
            // managed binding initializes, preventing a fatal ABI mismatch.
            _nativeRuntimeHandle = NativeLibrary.Load(nativePath);
        }
    }

    internal static float[] CreateNavigationFeatures(PrivilegedObservation observation)
    {
        var position = observation.EgoVehicle.WorldPosition;
        var target = observation.Route.LookaheadPoint;
        var heading = observation.EgoVehicle.HeadingRadians;
        var deltaX = target.X - position.X;
        var deltaY = target.Y - position.Y;
        var forwardX = Math.Cos(heading);
        var forwardY = Math.Sin(heading);
        var leftX = forwardY;
        var leftY = -forwardX;
        var forwardMeters = (deltaX * forwardX) + (deltaY * forwardY);
        var leftMeters = (deltaX * leftX) + (deltaY * leftY);
        return
        [
            (float)Math.Clamp(observation.EgoVehicle.SpeedMetersPerSecond / 15.0, -1.0, 1.0),
            (float)Math.Clamp(forwardMeters / 10.0, -1.0, 1.0),
            (float)Math.Clamp(leftMeters / 10.0, -1.0, 1.0),
        ];
    }

    private sealed record PolicyMetadata
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("checkpoint_epoch")]
        public int CheckpointEpoch { get; init; }

        [JsonPropertyName("cameras")]
        public string[] Cameras { get; init; } = [];

        [JsonPropertyName("image_width")]
        public int ImageWidth { get; init; }

        [JsonPropertyName("image_height")]
        public int ImageHeight { get; init; }

        [JsonPropertyName("target_mean")]
        public double[] TargetMean { get; init; } = [];

        [JsonPropertyName("target_standard_deviation")]
        public double[] TargetStandardDeviation { get; init; } = [];

        public void Validate()
        {
            if (SchemaVersion != 1 || CheckpointEpoch <= 0 || Cameras.Length == 0 ||
                Cameras.Distinct(StringComparer.Ordinal).Count() != Cameras.Length ||
                ImageWidth <= 0 || ImageHeight <= 0 || TargetMean.Length != 2 ||
                TargetStandardDeviation.Length != 2 ||
                TargetMean.Any(value => !double.IsFinite(value)) ||
                TargetStandardDeviation.Any(value => !double.IsFinite(value) || value <= 0.0))
            {
                throw new InvalidDataException("The policy metadata is incomplete or invalid.");
            }
        }
    }
}
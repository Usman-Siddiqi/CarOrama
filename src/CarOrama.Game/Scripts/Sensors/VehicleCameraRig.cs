using CarOrama.Core.Data;
using CarOrama.Core.Sensors;
using Godot;

namespace CarOrama.Game.Sensors;

/// <summary>
/// Owns the vehicle's render-camera channels without making any of them part of
/// the primary game viewport. Disabled channels perform no rendering work.
/// </summary>
public partial class VehicleCameraRig : Node
{
    private readonly CameraSensorLayout _layout;
    private readonly Dictionary<CameraSensorId, CameraChannel> _channels = [];
    private readonly HashSet<CameraSensorId> _captureChannels = [];
    private Node3D? _target;
    private CameraSensorId? _previewChannel;
    private bool _allChannelsEnabled;
    private bool _captureInProgress;

    public VehicleCameraRig(CameraSensorLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Name = "VehicleCameraRig";

        foreach (var specification in layout.Cameras)
        {
            AddChannel(specification);
        }
    }

    public IReadOnlyList<CameraSensorSpecification> Specifications => _layout.Cameras;

    public Node3D? Target
    {
        get => _target;
        set
        {
            _target = value;
            UpdateCameraTransforms();
        }
    }

    /// <summary>
    /// The one channel requested by the on-screen engineering monitor. This is
    /// tracked separately from capture requests so cycling the monitor cannot
    /// accidentally disable a dataset recorder.
    /// </summary>
    public CameraSensorId? PreviewChannel
    {
        get => _previewChannel;
        set
        {
            if (value.HasValue && !_channels.ContainsKey(value.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown vehicle camera channel.");
            }

            _previewChannel = value;
            RefreshRenderingRequests();
        }
    }

    /// <summary>
    /// Enables every sensor channel for a future synchronized capture pipeline.
    /// It is false by default so privileged-state and headless runs render none.
    /// </summary>
    public bool AllChannelsEnabled
    {
        get => _allChannelsEnabled;
        set
        {
            _allChannelsEnabled = value;
            RefreshRenderingRequests();
        }
    }

    public int RenderingChannelCount => _channels.Values.Count(channel => channel.RenderingRequested);

    public void SetChannelEnabled(CameraSensorId id, bool enabled)
    {
        RequireChannel(id);
        if (enabled)
        {
            _captureChannels.Add(id);
        }
        else
        {
            _captureChannels.Remove(id);
        }

        RefreshRenderingRequests();
    }

    public bool IsChannelEnabled(CameraSensorId id) => RequireChannel(id).RenderingRequested;

    public Texture2D GetTexture(CameraSensorId id) => RequireChannel(id).Viewport.GetTexture();

    public CameraSensorSpecification GetSpecification(CameraSensorId id) => RequireChannel(id).Specification;

    /// <summary>
    /// Renders and atomically saves a set of channels while the caller holds the
    /// simulation at a known physics tick.
    /// </summary>
    public async Task<IReadOnlyList<SensorFrameReference>> CaptureAsync(
        string episodeDirectory,
        IReadOnlyCollection<CameraSensorId> cameraIds,
        long physicsTick,
        double simulationTimeSeconds)
    {
        if (_captureInProgress)
        {
            throw new InvalidOperationException("A camera capture is already in progress.");
        }

        if (string.IsNullOrWhiteSpace(episodeDirectory))
        {
            throw new ArgumentException("An episode directory is required.", nameof(episodeDirectory));
        }

        if (physicsTick < 0 || !double.IsFinite(simulationTimeSeconds) || simulationTimeSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicsTick));
        }

        ArgumentNullException.ThrowIfNull(cameraIds);
        var requested = cameraIds.Distinct().Order().ToArray();
        if (requested.Length == 0)
        {
            return [];
        }

        _captureInProgress = true;
        try
        {
            UpdateCameraTransforms();
            foreach (var id in requested)
            {
                RequireChannel(id).Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            }

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var frames = new List<SensorFrameReference>(requested.Length);
            foreach (var id in requested)
            {
                var channel = RequireChannel(id);
                using var image = channel.Viewport.GetTexture().GetImage();
                if (image.IsEmpty() ||
                    image.GetWidth() != channel.Specification.ImageWidthPixels ||
                    image.GetHeight() != channel.Specification.ImageHeightPixels)
                {
                    throw new InvalidOperationException($"Camera '{id}' did not produce its calibrated image dimensions.");
                }

                var channelDirectory = id.ToString().ToLowerInvariant();
                var relativePath = $"frames/{channelDirectory}/{physicsTick:D10}.png";
                var absolutePath = Path.Combine(
                    Path.GetFullPath(episodeDirectory),
                    "frames",
                    channelDirectory,
                    $"{physicsTick:D10}.png");
                if (File.Exists(absolutePath))
                {
                    throw new IOException($"A sensor frame already exists at '{absolutePath}'.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                var temporaryPath = absolutePath + ".tmp";
                var error = image.SavePng(temporaryPath);
                if (error != Error.Ok)
                {
                    throw new IOException($"Godot failed to save '{absolutePath}' ({error}).");
                }

                File.Move(temporaryPath, absolutePath);
                frames.Add(new SensorFrameReference(id, physicsTick, simulationTimeSeconds, relativePath));
            }

            return frames;
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateCameraTransforms();

        foreach (var channel in _channels.Values)
        {
            if (!channel.RenderingRequested)
            {
                continue;
            }

            channel.SecondsUntilCapture -= delta;
            if (channel.SecondsUntilCapture > 0.0)
            {
                continue;
            }

            channel.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            var period = 1.0 / channel.Specification.CaptureFrequencyHertz;
            do
            {
                channel.SecondsUntilCapture += period;
            }
            while (channel.SecondsUntilCapture <= 0.0);
        }
    }

    private void AddChannel(CameraSensorSpecification specification)
    {
        if (_channels.ContainsKey(specification.Id))
        {
            throw new ArgumentException($"Duplicate vehicle camera id '{specification.Id}'.", nameof(specification));
        }

        var viewport = new SubViewport
        {
            Name = $"{specification.Id}Viewport",
            Size = new Vector2I(specification.ImageWidthPixels, specification.ImageHeightPixels),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            OwnWorld3D = false,
            Msaa3D = Viewport.Msaa.Disabled,
        };
        AddChild(viewport);

        var camera = new Camera3D
        {
            Name = $"{specification.Id}Camera",
            Current = true,
            Fov = (float)specification.HorizontalFieldOfViewDegrees,
            KeepAspect = Camera3D.KeepAspectEnum.Width,
            Near = 0.05f,
            Far = 800.0f,
        };
        viewport.AddChild(camera);

        _channels.Add(specification.Id, new CameraChannel(specification, viewport, camera));
    }

    private void RefreshRenderingRequests()
    {
        foreach (var (id, channel) in _channels)
        {
            var requested = _allChannelsEnabled || _captureChannels.Contains(id) || _previewChannel == id;
            if (channel.RenderingRequested == requested)
            {
                continue;
            }

            channel.RenderingRequested = requested;
            channel.SecondsUntilCapture = 0.0;
            channel.Viewport.RenderTargetUpdateMode = requested
                ? SubViewport.UpdateMode.Once
                : SubViewport.UpdateMode.Disabled;
        }
    }

    private void UpdateCameraTransforms()
    {
        if (_target is null || !IsInstanceValid(_target))
        {
            return;
        }

        foreach (var channel in _channels.Values)
        {
            channel.Camera.GlobalTransform = _target.GlobalTransform * CreateMountTransform(channel.Specification.Pose);
        }
    }

    private CameraChannel RequireChannel(CameraSensorId id)
    {
        if (!_channels.TryGetValue(id, out var channel))
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown vehicle camera channel.");
        }

        return channel;
    }

    private static Transform3D CreateMountTransform(CameraSensorPose pose)
    {
        // Core uses vehicle coordinates (+forward, +left, +up). Godot cameras
        // look down local -Z with +X right and +Y up.
        var position = new Vector3(
            (float)-pose.LeftMeters,
            (float)pose.UpMeters,
            (float)-pose.ForwardMeters);
        var yaw = new Basis(Vector3.Up, Mathf.DegToRad((float)pose.YawDegrees));
        var pitch = new Basis(Vector3.Right, Mathf.DegToRad((float)pose.PitchDegrees));
        var roll = new Basis(Vector3.Forward, Mathf.DegToRad((float)pose.RollDegrees));
        return new Transform3D(yaw * pitch * roll, position);
    }

    private sealed class CameraChannel(
        CameraSensorSpecification specification,
        SubViewport viewport,
        Camera3D camera)
    {
        public CameraSensorSpecification Specification { get; } = specification;

        public SubViewport Viewport { get; } = viewport;

        public Camera3D Camera { get; } = camera;

        public bool RenderingRequested { get; set; }

        public double SecondsUntilCapture { get; set; }
    }
}

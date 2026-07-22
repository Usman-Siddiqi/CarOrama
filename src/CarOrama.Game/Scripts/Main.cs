using CarOrama.Core.Control;
using CarOrama.Core.Data;
using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;
using CarOrama.Game.Environment;
using CarOrama.Game.Input;
using CarOrama.Game.Sensors;
using CarOrama.Game.Simulation;
using CarOrama.Game.UI;
using CarOrama.Game.Validation;
using CarOrama.Game.Vehicle;
using Godot;

namespace CarOrama.Game;

public partial class Main : Node3D
{
    private long _seed = 42;
    private RoadWorld? _roadWorld;
    private ElectricVehicle? _vehicle;
    private FollowVehicleCamera? _followCamera;
    private TelemetryHud? _hud;
    private VehicleCameraRig? _vehicleCameraRig;
    private VehicleCameraMonitor? _vehicleCameraMonitor;
    private Transform3D _vehicleSpawnTransform;
    private IVehicleCommandSource? _commandSourceOverride;
    private BufferedVehicleCommandSource? _episodeCommandSource;
    private readonly VehicleSpecification _vehicleSpecification = new();
    private CameraSensorLayout _cameraSensorLayout = CameraSensorLayout.CreateModel3Inspired();
    private bool _cameraMonitorEnabled;

    public override async void _Ready()
    {
        InputBindings.EnsureConfigured();
        var smokeTest = HasArgument("--smoke-test");
        var episodeSmokeTest = HasArgument("--episode-smoke-test");
        var datasetSmokeTest = HasArgument("--dataset-smoke-test");
        var recordDataset = HasArgument("--record-dataset");
        if (episodeSmokeTest || datasetSmokeTest || recordDataset)
        {
            _episodeCommandSource = new BufferedVehicleCommandSource();
            _commandSourceOverride = _episodeCommandSource;
        }
        else if (smokeTest)
        {
            _commandSourceOverride = new ScriptedVehicleCommandSource();
        }

        _cameraMonitorEnabled = !smokeTest && !episodeSmokeTest && !datasetSmokeTest && !recordDataset &&
            !HasArgument("--traffic-control-preview") &&
            !HasArgument("--traffic-signal-preview") &&
            !HasArgument("--corner-preview") &&
            !HasArgument("--intersection-preview") &&
            !IsHeadlessDisplay();

        _seed = ReadSeed();
        BuildWorld();
        AddLighting();
        if (HasArgument("--traffic-control-preview"))
        {
            AddTrafficControlPreviewCamera();
        }
        else if (HasArgument("--traffic-signal-preview"))
        {
            AddTrafficSignalPreviewCamera();
        }
        else if (HasArgument("--corner-preview"))
        {
            AddCornerPreviewCamera();
        }
        else if (HasArgument("--intersection-preview"))
        {
            AddIntersectionPreviewCamera();
        }

        if (recordDataset)
        {
            await RunDatasetRecordingAsync();
        }
        else if (datasetSmokeTest)
        {
            await RunDatasetSmokeTestAsync();
        }
        else if (episodeSmokeTest)
        {
            await RunEpisodeSmokeTestAsync();
        }
        else if (smokeTest)
        {
            await RunSmokeTestAsync();
        }
    }

    private void BuildWorld()
    {
        if (_roadWorld is not null)
        {
            _roadWorld.QueueFree();
        }

        if (_vehicle is not null)
        {
            _vehicle.QueueFree();
        }

        var generator = new GridRoadNetworkGenerator();
        var network = generator.Generate(new RoadNetworkConfig { Seed = _seed });
        _roadWorld = new RoadSceneBuilder().Build(network);
        AddChild(_roadWorld);
        SpawnVehicle(network);
        GD.Print($"Road seed {_seed}: {network.Nodes.Count} nodes, {network.Segments.Count} segments, {network.Lanes.Count} directed lanes.");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (Godot.Input.IsActionJustPressed(InputBindings.Reset))
        {
            _vehicle?.ResetVehicle();
        }

        if (Godot.Input.IsActionJustPressed(InputBindings.NewSeed))
        {
            _seed++;
            BuildWorld();
        }
    }

    private void AddLighting()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-58.0f, -28.0f, 0.0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(sun);

        var worldEnvironment = new WorldEnvironment
        {
            Name = "WorldEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("8db6d4"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b7c7cf"),
                AmbientLightEnergy = 0.65f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        };
        AddChild(worldEnvironment);
    }

    private void SpawnVehicle(RoadNetwork network)
    {
        var spawn = network.SpawnPoints[0];
        var forward = new Vector3((float)spawn.Forward.X, 0.0f, (float)spawn.Forward.Y).Normalized();
        var yaw = Mathf.Atan2(-forward.X, -forward.Z);
        _vehicleSpawnTransform = new Transform3D(
            new Basis(Vector3.Up, yaw),
            new Vector3((float)spawn.Position.X, 1.15f, (float)spawn.Position.Y));

        var commandSource = _commandSourceOverride ?? new ManualVehicleCommandSource();
        _vehicle = new ElectricVehicle(_vehicleSpecification, commandSource);
        AddChild(_vehicle);
        _vehicle.SetResetTransform(_vehicleSpawnTransform);
        if (_roadWorld?.TrafficSignals is not null)
        {
            _roadWorld.TrafficSignals.ObservedVehicle = _vehicle;
        }

        if (_followCamera is null)
        {
            _followCamera = new FollowVehicleCamera();
            AddChild(_followCamera);
            _followCamera.GlobalPosition = _vehicle.GlobalPosition + new Vector3(0.0f, 3.0f, 8.0f);
        }

        _followCamera.Target = _vehicle;

        if (_hud is null)
        {
            _hud = new TelemetryHud();
            AddChild(_hud);
        }

        _hud.Vehicle = _vehicle;
        _hud.Seed = _seed;

        if (_vehicleCameraRig is null)
        {
            _vehicleCameraRig = new VehicleCameraRig(_cameraSensorLayout);
            AddChild(_vehicleCameraRig);
        }

        _vehicleCameraRig.Target = _vehicle;

        if (_cameraMonitorEnabled)
        {
            if (_vehicleCameraMonitor is null)
            {
                _vehicleCameraMonitor = new VehicleCameraMonitor();
                AddChild(_vehicleCameraMonitor);
            }

            _vehicleCameraMonitor.Rig = _vehicleCameraRig;
            _vehicleCameraMonitor.SetPreviewEnabled(true);
            if (ReadCameraPreview() is { } cameraPreview)
            {
                _vehicleCameraMonitor.SelectCamera(cameraPreview);
            }
        }
        else
        {
            _vehicleCameraRig.PreviewChannel = null;
        }
    }

    private void AddIntersectionPreviewCamera()
    {
        if (_roadWorld is null)
        {
            return;
        }

        var intersection = _roadWorld.Network.Intersections
            .Where(candidate => candidate.Kind is IntersectionKind.Corner or IntersectionKind.ThreeWay or IntersectionKind.FourWay)
            .OrderByDescending(candidate => candidate.IncomingLaneIds
                .Select(_roadWorld.Network.GetLane)
                .Select(lane => _roadWorld.Network.GetSegment(lane.SegmentId).WidthMeters)
                .DefaultIfEmpty(0.0)
                .Max())
            .ThenByDescending(candidate => candidate.Kind)
            .ThenBy(candidate => candidate.NodeId, StringComparer.Ordinal)
            .FirstOrDefault() ?? _roadWorld.Network.Intersections[0];
        var target = new Vector3((float)intersection.Position.X, 0.0f, (float)intersection.Position.Y);
        var camera = new Camera3D
        {
            Name = "IntersectionPreviewCamera",
            Position = target + new Vector3(17.0f, 18.0f, 21.0f),
            Current = true,
            Far = 500.0f,
            Fov = 58.0f,
        };
        AddChild(camera);
        camera.LookAt(target, Vector3.Up);
        PreparePreview();
    }

    private void AddCornerPreviewCamera()
    {
        if (_roadWorld is null)
        {
            return;
        }

        var intersection = _roadWorld.Network.Intersections
            .Where(candidate => candidate.Kind == IntersectionKind.Corner)
            .OrderByDescending(candidate => candidate.IncomingLaneIds
                .Select(_roadWorld.Network.GetLane)
                .Select(lane => _roadWorld.Network.GetSegment(lane.SegmentId).WidthMeters)
                .DefaultIfEmpty(0.0)
                .Max())
            .ThenBy(candidate => candidate.NodeId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (intersection is null)
        {
            AddIntersectionPreviewCamera();
            return;
        }

        var target = new Vector3((float)intersection.Position.X, 0.0f, (float)intersection.Position.Y);
        var camera = new Camera3D
        {
            Name = "CornerPreviewCamera",
            Position = target + new Vector3(14.0f, 16.0f, 18.0f),
            Current = true,
            Far = 300.0f,
            Fov = 54.0f,
        };
        AddChild(camera);
        camera.LookAt(target, Vector3.Up);
        PreparePreview();
    }

    private void AddTrafficControlPreviewCamera()
    {
        if (_roadWorld is null)
        {
            return;
        }

        var control = _roadWorld.Network.TrafficControls
            .Where(candidate => candidate.Kind == TrafficControlKind.StopSign)
            .OrderByDescending(candidate => _roadWorld.Network.GetSegment(candidate.ApproachSegmentId).WidthMeters)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (control is null)
        {
            AddIntersectionPreviewCamera();
            return;
        }

        var groundPosition = new Vector3((float)control.Position.X, 0.0f, (float)control.Position.Y);
        var approachDirection = new Vector3(
            (float)control.FacingDirection.X,
            0.0f,
            (float)control.FacingDirection.Y).Normalized();
        var target = groundPosition + (Vector3.Up * 2.08f);
        var camera = new Camera3D
        {
            Name = "TrafficControlPreviewCamera",
            Position = target - (approachDirection * 3.4f) + (Vector3.Up * 0.05f),
            Current = true,
            Far = 200.0f,
            Fov = 30.0f,
        };
        AddChild(camera);
        camera.LookAt(target, Vector3.Up);
        PreparePreview();
    }

    private void AddTrafficSignalPreviewCamera()
    {
        if (_roadWorld is null)
        {
            return;
        }

        var control = _roadWorld.Network.TrafficControls
            .Where(candidate => candidate.Kind == TrafficControlKind.TrafficLight)
            .OrderByDescending(candidate => _roadWorld.Network.GetSegment(candidate.ApproachSegmentId).WidthMeters)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (control is null)
        {
            AddIntersectionPreviewCamera();
            return;
        }

        var intersection = _roadWorld.Network.Intersections
            .Single(candidate => candidate.NodeId == control.IntersectionNodeId);
        var target = new Vector3(
            (float)intersection.Position.X,
            3.1f,
            (float)intersection.Position.Y);
        var approachDirection = new Vector3(
            (float)control.FacingDirection.X,
            0.0f,
            (float)control.FacingDirection.Y).Normalized();
        var camera = new Camera3D
        {
            Name = "TrafficSignalPreviewCamera",
            Position = target - (approachDirection * 19.0f) + (Vector3.Up * 2.2f),
            Current = true,
            Far = 250.0f,
            Fov = 52.0f,
        };
        AddChild(camera);
        camera.LookAt(target, Vector3.Up);
        PreparePreview();
    }

    private void PreparePreview()
    {
        if (_hud is not null)
        {
            _hud.Visible = false;
        }

        if (_vehicle is not null)
        {
            _vehicle.Freeze = true;
        }

        if (_vehicleCameraMonitor is not null)
        {
            _vehicleCameraMonitor.SetPreviewEnabled(false);
        }
    }

    private async Task RunSmokeTestAsync()
    {
        if (_roadWorld is null || _vehicle is null)
        {
            GD.PushError("Smoke test failed: road world or vehicle was not created.");
            GetTree().Quit(1);
            return;
        }

        var result = RoadNetworkValidator.Validate(_roadWorld.Network);
        var referencePlant = new LongitudinalVehicleModel(new VehicleSpecification());
        for (var index = 0; index < 240; index++)
        {
            referencePlant.Step(VehicleCommand.Create(0.0, 0.7, 0.0, 0.0), 1.0 / 120.0);
        }

        var startingPosition = _vehicle.GlobalPosition;
        for (var index = 0; index < 500; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        var position = _vehicle.GlobalPosition;
        var finitePosition = float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
        var vehicleMoved = position.DistanceTo(startingPosition) > 1.0f;
        var vehicleAccelerated = _vehicle.PeakSpeedMetersPerSecond > 1.0;
        var signalStatesValid = _roadWorld.Network.TrafficControls
            .Where(control => control.Kind == TrafficControlKind.TrafficLight)
            .All(control => Enum.IsDefined(_roadWorld.GetTrafficSignalState(control.Id)));
        var signalPlacementsValid = _roadWorld.Network.TrafficControls
            .Where(control => control.Kind == TrafficControlKind.TrafficLight)
            .All(control =>
            {
                var signalHead = _roadWorld.GetTrafficSignalHead(control.Id);
                var intersection = _roadWorld.Network.Intersections
                    .Single(candidate => candidate.NodeId == control.IntersectionNodeId);
                var intersectionPosition = new Vector2D(intersection.Position.X, intersection.Position.Y);
                var signalPosition = new Vector2D(signalHead.GlobalPosition.X, signalHead.GlobalPosition.Z);
                var isFarSide = Vector2D.Dot(
                    signalPosition - intersectionPosition,
                    control.FacingDirection) > 0.0;
                return isFarSide && signalHead.SignalHeadCount == control.IncomingLaneIds.Count;
            });
        var cameraRigValid = _vehicleCameraRig is not null &&
            _vehicleCameraRig.Specifications.Count == 8 &&
            _vehicleCameraRig.Specifications.Select(camera => camera.Id).Distinct().Count() == 8 &&
            _vehicleCameraRig.RenderingChannelCount == 0;

        if (!result.IsValid ||
            _roadWorld.Network.SpawnPoints.Count == 0 ||
            referencePlant.State.SpeedMetersPerSecond <= 0.0 ||
            !finitePosition ||
            !vehicleMoved ||
            !vehicleAccelerated ||
            !signalStatesValid ||
            !signalPlacementsValid ||
            !cameraRigValid ||
            !_vehicle.LastLightingCommand.HeadlightsEnabled ||
            !_vehicle.LastLightingCommand.HazardLightsEnabled)
        {
            GD.PushError(
                $"Smoke test failed: {string.Join("; ", result.Errors)} " +
                $"finite={finitePosition}, moved={vehicleMoved}, peakSpeed={_vehicle.PeakSpeedMetersPerSecond:F2} m/s, " +
                $"signals={signalStatesValid}, signalPlacement={signalPlacementsValid}, " +
                $"cameraRig={cameraRigValid}, " +
                $"lights={_vehicle.LastLightingCommand}.");
            GetTree().Quit(1);
            return;
        }

        GD.Print("SMOKE TEST PASSED: structured road world and electric drivetrain created and validated.");
        GetTree().Quit(0);
    }

    private async Task RunEpisodeSmokeTestAsync()
    {
        if (_roadWorld is null || _vehicle is null || _episodeCommandSource is null)
        {
            GD.PushError("Episode smoke test failed: world, vehicle, or buffered command source is missing.");
            GetTree().Quit(1);
            return;
        }

        var spawn = _roadWorld.Network.SpawnPoints[0];
        var route = DrivingRoute.Create("godot-episode-smoke-route", _roadWorld.Network, [spawn.LaneId]);
        var scenario = SimulationScenario.Create(
            "godot-episode-smoke",
            physicsTicksPerSecond: Engine.PhysicsTicksPerSecond,
            controlTicksPerSecond: 20,
            maximumControlTicks: 40);
        var resetRequest = EpisodeResetRequest.Create(
            scenario,
            _roadWorld.Network.Seed,
            route.Id,
            spawn.Id);
        var environment = new GodotDrivingEnvironment(
            _roadWorld,
            _vehicle,
            route,
            _episodeCommandSource,
            _vehicleSpecification);
        AddChild(environment);
        var initial = await environment.ResetAsync(resetRequest);
        EpisodeStepResult? result = null;
        while (result is null || !result.IsTerminal)
        {
            var action = DrivingAction.Create(
                result?.ControlTick ?? initial.ControlTick,
                steering: 0.0,
                longitudinalAccelerationMetersPerSecondSquared: 1.5);
            result = await environment.StepAsync(action);
        }

        var position = _vehicle.GlobalPosition;
        var finite = float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
        var valid = finite &&
            result.IsTruncated &&
            result.Termination.Reason == EpisodeTerminationReason.ControlTickLimitReached &&
            result.ControlTick == scenario.MaximumControlTicks &&
            result.Observation.PhysicsTick == scenario.MaximumControlTicks * scenario.PhysicsTicksPerControlTick &&
            result.Observation.Route.DistanceTravelledMeters > initial.Route.DistanceTravelledMeters &&
            result.Observation.EgoVehicle.SpeedMetersPerSecond > 0.5 &&
            environment.IsPausedBetweenSteps;
        if (!valid)
        {
            GD.PushError(
                $"Episode smoke test failed: finite={finite}, termination={result.Termination}, " +
                $"controlTick={result.ControlTick}, physicsTick={result.Observation.PhysicsTick}, " +
                $"progress={result.Observation.Route.DistanceTravelledMeters:F2}, " +
                $"speed={result.Observation.EgoVehicle.SpeedMetersPerSecond:F2}, " +
                $"collisions={result.Metrics.CollisionCount}, paused={environment.IsPausedBetweenSteps}.");
            GetTree().Paused = false;
            GetTree().Quit(1);
            return;
        }

        GD.Print(
            $"EPISODE SMOKE TEST PASSED: {result.ControlTick} control ticks, " +
            $"{result.Observation.PhysicsTick} physics ticks, paused between actions.");
        GetTree().Paused = false;
        GetTree().Quit(0);
    }

    private async Task RunDatasetSmokeTestAsync()
    {
        if (_roadWorld is null || _vehicle is null || _episodeCommandSource is null)
        {
            GD.PushError("Dataset smoke test failed: simulation components are missing.");
            GetTree().Quit(1);
            return;
        }

        var outputRoot = Path.Combine(Path.GetTempPath(), $"carorama-dataset-smoke-{Guid.NewGuid():N}");
        var succeeded = false;
        try
        {
            var spawn = _roadWorld.Network.SpawnPoints[0];
            var route = DrivingRoute.Create("dataset-smoke-route", _roadWorld.Network, [spawn.LaneId]);
            var scenario = SimulationScenario.Create(
                "dataset-smoke",
                physicsTicksPerSecond: Engine.PhysicsTicksPerSecond,
                controlTicksPerSecond: 20,
                maximumControlTicks: 4);
            var manifestEntry = new ScenarioManifestEntry(
                scenario.ScenarioId,
                DatasetSplit.Test,
                _roadWorld.Network.Seed,
                route.Id,
                spawn.Id,
                route.LaneIds[^1],
                route.LaneIds,
                scenario.PhysicsTicksPerSecond,
                scenario.ControlTicksPerSecond,
                scenario.MaximumControlTicks);
            var manifest = new ScenarioManifest(
                ScenarioManifest.CurrentSchemaVersion,
                SimulationContract.CurrentVersion,
                [manifestEntry]);
            var layout = CameraSensorLayout.CreateModel3Inspired(
                imageWidthPixels: 64,
                imageHeightPixels: 36,
                captureFrequencyHertz: 20.0);
            var captureRig = new VehicleCameraRig(layout) { Target = _vehicle };
            AddChild(captureRig);
            var writer = new EpisodeDatasetWriter(
                outputRoot,
                "godot-jolt-smoke",
                "validation",
                manifest,
                DateTimeOffset.UnixEpoch);
            using var episode = writer.BeginEpisode(scenario.ScenarioId, layout.Cameras);
            var environment = new GodotDrivingEnvironment(
                _roadWorld,
                _vehicle,
                route,
                _episodeCommandSource,
                _vehicleSpecification);
            AddChild(environment);
            var reset = await environment.ResetAsync(EpisodeResetRequest.Create(
                scenario,
                _roadWorld.Network.Seed,
                route.Id,
                spawn.Id));
            episode.WriteReset(reset);
            var scheduler = new CameraCaptureScheduler(layout, scenario.PhysicsTicksPerSecond);
            EpisodeStepResult? result = null;
            while (result is null || !result.IsTerminal)
            {
                var action = DrivingAction.Create(result?.ControlTick ?? reset.ControlTick, 0.0, 1.0);
                var frames = new List<SensorFrameReference>();
                result = await environment.StepAsync(action, async (physicsTick, simulationTimeSeconds) =>
                {
                    var due = scheduler.Advance(physicsTick);
                    if (due.Count > 0)
                    {
                        frames.AddRange(await captureRig.CaptureAsync(
                            episode.EpisodeDirectory,
                            due,
                            physicsTick,
                            simulationTimeSeconds));
                    }
                });
                episode.WriteStep(action, result, frames);
            }

            var summary = episode.Complete();
            writer.CompleteDataset();
            var expectedFrameCount = layout.Cameras.Count * (int)scenario.MaximumControlTicks;
            var completeMarker = Path.Combine(episode.EpisodeDirectory, "_COMPLETE");
            var stepStream = Path.Combine(episode.EpisodeDirectory, "steps.jsonl");
            var temporaryStream = stepStream + ".tmp";
            var pngCount = Directory.EnumerateFiles(
                Path.Combine(episode.EpisodeDirectory, "frames"),
                "*.png",
                SearchOption.AllDirectories).Count();
            if (!result.IsTruncated ||
                summary.RecordedSensorFrameCount != expectedFrameCount ||
                pngCount != expectedFrameCount ||
                !File.Exists(completeMarker) ||
                !File.Exists(Path.Combine(writer.DatasetDirectory, "_COMPLETE")) ||
                !File.Exists(stepStream) ||
                File.Exists(temporaryStream))
            {
                throw new InvalidOperationException(
                    $"Invalid completed dataset: termination={result.Termination}, " +
                    $"indexed={summary.RecordedSensorFrameCount}, png={pngCount}, expected={expectedFrameCount}.");
            }

            succeeded = true;
            GD.Print(
                $"DATASET SMOKE TEST PASSED: {summary.RecordedStepCount} steps, " +
                $"{summary.RecordedSensorFrameCount} synchronized frames, crash-safe completion.");
            GetTree().Paused = false;
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"Dataset smoke test failed: {exception}\nArtifacts: {outputRoot}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
        finally
        {
            if (succeeded && Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private async Task RunDatasetRecordingAsync()
    {
        try
        {
            var recorder = new GodotDatasetRecorder(_episodeCommandSource!, _vehicleSpecification, this);
            AddChild(recorder);
            var exitCode = await recorder.RecordAsync(OS.GetCmdlineUserArgs(), PrepareRecordingWorldAsync);
            GetTree().Paused = false;
            GetTree().Quit(exitCode);
        }
        catch (Exception exception)
        {
            GD.PushError($"Dataset recording failed: {exception}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private async Task<GodotRecordingWorld> PrepareRecordingWorldAsync(
        long seed,
        CameraSensorLayout layout)
    {
        if (!ReferenceEquals(_cameraSensorLayout, layout))
        {
            _cameraSensorLayout = layout;
            if (_vehicleCameraRig is not null)
            {
                _vehicleCameraRig.QueueFree();
                _vehicleCameraRig = null;
            }
        }

        _seed = seed;
        BuildWorld();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return new GodotRecordingWorld(
            _roadWorld ?? throw new InvalidOperationException("Road world rebuild failed."),
            _vehicle ?? throw new InvalidOperationException("Vehicle rebuild failed."),
            _vehicleCameraRig ?? throw new InvalidOperationException("Camera rig rebuild failed."));
    }

    private static long ReadSeed()
    {
        var arguments = OS.GetCmdlineUserArgs();
        for (var index = 0; index + 1 < arguments.Length; index++)
        {
            if (arguments[index] == "--seed" && long.TryParse(arguments[index + 1], out var seed))
            {
                return seed;
            }
        }

        return 42;
    }

    private static bool HasArgument(string expected) => OS.GetCmdlineUserArgs().Contains(expected);

    private static CameraSensorId? ReadCameraPreview()
    {
        var arguments = OS.GetCmdlineUserArgs();
        for (var index = 0; index + 1 < arguments.Length; index++)
        {
            if (arguments[index] == "--camera-preview" &&
                Enum.TryParse<CameraSensorId>(arguments[index + 1], ignoreCase: true, out var cameraId) &&
                Enum.IsDefined(cameraId))
            {
                return cameraId;
            }
        }

        return null;
    }

    private static bool IsHeadlessDisplay() => string.Equals(
        DisplayServer.GetName(),
        "headless",
        StringComparison.OrdinalIgnoreCase);
}

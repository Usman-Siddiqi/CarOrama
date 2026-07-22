using CarOrama.Core.Control;
using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Core.Sensors;
using CarOrama.Core.Vehicles;
using CarOrama.Game.Environment;
using CarOrama.Game.Input;
using CarOrama.Game.Sensors;
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
    private bool _cameraMonitorEnabled;

    public override async void _Ready()
    {
        InputBindings.EnsureConfigured();
        var smokeTest = HasArgument("--smoke-test");
        if (smokeTest)
        {
            _commandSourceOverride = new ScriptedVehicleCommandSource();
        }

        _cameraMonitorEnabled = !smokeTest &&
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

        if (smokeTest)
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
        _vehicle = new ElectricVehicle(new VehicleSpecification(), commandSource);
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
            _vehicleCameraRig = new VehicleCameraRig(CameraSensorLayout.CreateModel3Inspired());
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

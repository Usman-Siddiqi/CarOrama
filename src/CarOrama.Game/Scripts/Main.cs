using CarOrama.Core.Roads;
using CarOrama.Game.Environment;
using Godot;

namespace CarOrama.Game;

public partial class Main : Node3D
{
    private long _seed = 42;
    private RoadWorld? _roadWorld;

    public override void _Ready()
    {
        _seed = ReadSeed();
        BuildWorld();
        AddLighting();
        AddOverviewCamera();

        if (HasArgument("--smoke-test"))
        {
            RunSmokeTest();
        }
    }

    private void BuildWorld()
    {
        if (_roadWorld is not null)
        {
            _roadWorld.QueueFree();
        }

        var generator = new GridRoadNetworkGenerator();
        var network = generator.Generate(new RoadNetworkConfig { Seed = _seed });
        _roadWorld = new RoadSceneBuilder().Build(network);
        AddChild(_roadWorld);
        GD.Print($"Road seed {_seed}: {network.Nodes.Count} nodes, {network.Segments.Count} segments, {network.Lanes.Count} directed lanes.");
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

    private void AddOverviewCamera()
    {
        var camera = new Camera3D
        {
            Name = "OverviewCamera",
            Position = new Vector3(140.0f, 165.0f, 185.0f),
            Current = true,
            Far = 1000.0f,
        };
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private void RunSmokeTest()
    {
        if (_roadWorld is null)
        {
            GD.PushError("Smoke test failed: road world was not created.");
            GetTree().Quit(1);
            return;
        }

        var result = RoadNetworkValidator.Validate(_roadWorld.Network);
        if (!result.IsValid || _roadWorld.Network.SpawnPoints.Count == 0)
        {
            GD.PushError($"Smoke test failed: {string.Join("; ", result.Errors)}");
            GetTree().Quit(1);
            return;
        }

        GD.Print("SMOKE TEST PASSED: structured road world created and validated.");
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
}

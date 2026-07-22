using CarOrama.Core.Control;
using CarOrama.Game.Environment;
using Godot;

namespace CarOrama.Game.Vehicle;

/// <summary>
/// Owns the vehicle's lamp geometry and translates lighting and braking
/// requests into visible lamp and headlight-beam state.
/// </summary>
internal sealed partial class VehicleExteriorLighting : Node3D
{
    private const double BlinkCycleSeconds = 2.0 / 3.0;
    private const double BlinkOnSeconds = BlinkCycleSeconds * 0.5;
    private const double BrakeActivationThreshold = 0.02;

    private static readonly Color HeadlightColor = new("fff4d6");
    private static readonly Color HeadlightOffColor = new("aeb7b8");
    private static readonly Color TailLightColor = new("e00000");
    private static readonly Color TailLightOffColor = new("4e0b12");
    private static readonly Color IndicatorColor = new("f07800");
    private static readonly Color IndicatorOffColor = new("5b3108");

    private readonly StandardMaterial3D _headlightMaterial = CreateLampMaterial(HeadlightOffColor);
    private readonly StandardMaterial3D _tailLightMaterial = CreateLampMaterial(TailLightOffColor);
    private readonly StandardMaterial3D _centerBrakeLightMaterial = CreateLampMaterial(TailLightOffColor);
    private readonly StandardMaterial3D _leftIndicatorMaterial = CreateLampMaterial(IndicatorOffColor);
    private readonly StandardMaterial3D _rightIndicatorMaterial = CreateLampMaterial(IndicatorOffColor);
    private readonly List<SpotLight3D> _headlightBeams = [];
    private VehicleLightingCommand _previousCommand = VehicleLightingCommand.Off;
    private double _blinkElapsedSeconds;

    public VehicleExteriorLighting()
    {
        Name = "ExteriorLighting";
        BuildLampGeometry();
        ApplyLampState(VehicleLightingCommand.Off, VehicleCommand.Neutral, 0.0);
    }

    public bool BrakeLightsActive { get; private set; }

    public bool LeftIndicatorLit { get; private set; }

    public bool RightIndicatorLit { get; private set; }

    public void ApplyLampState(
        VehicleLightingCommand lightingCommand,
        VehicleCommand motionCommand,
        double deltaSeconds)
    {
        var indicatorSelectionChanged =
            lightingCommand.LeftIndicatorEnabled != _previousCommand.LeftIndicatorEnabled ||
            lightingCommand.RightIndicatorEnabled != _previousCommand.RightIndicatorEnabled;
        if (indicatorSelectionChanged)
        {
            _blinkElapsedSeconds = 0.0;
        }

        var indicatorsEnabled = lightingCommand.LeftIndicatorEnabled || lightingCommand.RightIndicatorEnabled;
        if (indicatorsEnabled)
        {
            _blinkElapsedSeconds = (_blinkElapsedSeconds + Math.Max(0.0, deltaSeconds)) % BlinkCycleSeconds;
        }
        else
        {
            _blinkElapsedSeconds = 0.0;
        }

        var blinkOn = indicatorsEnabled && _blinkElapsedSeconds < BlinkOnSeconds;
        LeftIndicatorLit = lightingCommand.LeftIndicatorEnabled && blinkOn;
        RightIndicatorLit = lightingCommand.RightIndicatorEnabled && blinkOn;
        BrakeLightsActive =
            motionCommand.RegenerativeBrake > BrakeActivationThreshold ||
            motionCommand.FrictionBrake > BrakeActivationThreshold;

        SetLampMaterial(
            _headlightMaterial,
            lightingCommand.HeadlightsEnabled,
            HeadlightColor,
            HeadlightOffColor,
            2.4f);

        var tailLightsEnabled = lightingCommand.HeadlightsEnabled || BrakeLightsActive;
        var tailLightEnergy = BrakeLightsActive ? 1.65f : 0.35f;
        SetLampMaterial(
            _tailLightMaterial,
            tailLightsEnabled,
            TailLightColor,
            TailLightOffColor,
            tailLightEnergy);
        SetLampMaterial(
            _centerBrakeLightMaterial,
            BrakeLightsActive,
            TailLightColor,
            TailLightOffColor,
            1.65f);
        SetLampMaterial(
            _leftIndicatorMaterial,
            LeftIndicatorLit,
            IndicatorColor,
            IndicatorOffColor,
            1.6f);
        SetLampMaterial(
            _rightIndicatorMaterial,
            RightIndicatorLit,
            IndicatorColor,
            IndicatorOffColor,
            1.6f);

        foreach (var beam in _headlightBeams)
        {
            beam.Visible = lightingCommand.HeadlightsEnabled;
        }

        _previousCommand = lightingCommand;
    }

    private void BuildLampGeometry()
    {
        AddLamp(
            "LeftHeadlight",
            new Vector3(-0.62f, 0.29f, -2.305f),
            new Vector3(0.48f, 0.18f, 0.07f),
            _headlightMaterial);
        AddLamp(
            "RightHeadlight",
            new Vector3(0.62f, 0.29f, -2.305f),
            new Vector3(0.48f, 0.18f, 0.07f),
            _headlightMaterial);

        AddLamp(
            "LeftTailAndBrakeLight",
            new Vector3(-0.62f, 0.31f, 2.305f),
            new Vector3(0.48f, 0.2f, 0.07f),
            _tailLightMaterial);
        AddLamp(
            "RightTailAndBrakeLight",
            new Vector3(0.62f, 0.31f, 2.305f),
            new Vector3(0.48f, 0.2f, 0.07f),
            _tailLightMaterial);
        AddLamp(
            "CenterBrakeLight",
            new Vector3(0.0f, 0.96f, 1.315f),
            new Vector3(0.7f, 0.075f, 0.045f),
            _centerBrakeLightMaterial);

        AddIndicatorSet("Left", -0.875f, _leftIndicatorMaterial);
        AddIndicatorSet("Right", 0.875f, _rightIndicatorMaterial);

        AddHeadlightBeam("LeftHeadlightBeam", -0.62f);
        AddHeadlightBeam("RightHeadlightBeam", 0.62f);
    }

    private void AddIndicatorSet(string side, float x, Material material)
    {
        AddLamp(
            $"{side}FrontIndicator",
            new Vector3(x, 0.27f, -2.31f),
            new Vector3(0.16f, 0.15f, 0.075f),
            material);
        AddLamp(
            $"{side}RearIndicator",
            new Vector3(x, 0.27f, 2.31f),
            new Vector3(0.16f, 0.15f, 0.075f),
            material);
        AddLamp(
            $"{side}SideRepeater",
            new Vector3(x < 0.0f ? -0.958f : 0.958f, 0.32f, -0.92f),
            new Vector3(0.045f, 0.11f, 0.24f),
            material);
    }

    private void AddLamp(string name, Vector3 position, Vector3 size, Material material)
    {
        AddChild(PrimitiveFactory.Box(
            name,
            size,
            new Transform3D(Basis.Identity, position),
            material));
    }

    private void AddHeadlightBeam(string name, float x)
    {
        var beam = new SpotLight3D
        {
            Name = name,
            Position = new Vector3(x, 0.3f, -2.35f),
            RotationDegrees = new Vector3(-4.0f, 0.0f, 0.0f),
            LightColor = HeadlightColor,
            LightEnergy = 4.0f,
            SpotRange = 52.0f,
            SpotAngle = 33.0f,
            SpotAngleAttenuation = 1.15f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(beam);
        _headlightBeams.Add(beam);
    }

    private static StandardMaterial3D CreateLampMaterial(Color color)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.18f,
            Metallic = 0.05f,
            EmissionEnabled = true,
            Emission = Colors.Black,
            EmissionEnergyMultiplier = 0.0f,
        };
    }

    private static void SetLampMaterial(
        StandardMaterial3D material,
        bool lit,
        Color litColor,
        Color unlitColor,
        float emissionEnergy)
    {
        material.AlbedoColor = lit ? litColor : unlitColor;
        material.Emission = lit ? litColor : Colors.Black;
        material.EmissionEnergyMultiplier = lit ? emissionEnergy : 0.0f;
    }
}

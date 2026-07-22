using CarOrama.Core.Sensors;
using CarOrama.Game.Input;
using CarOrama.Game.Sensors;
using Godot;

namespace CarOrama.Game.UI;

/// <summary>
/// Displays one selectable vehicle-camera channel without controlling sensor
/// capture policy for the other channels.
/// </summary>
public partial class VehicleCameraMonitor : CanvasLayer
{
    private readonly Label _title;
    private readonly Label _details;
    private readonly TextureRect _image;
    private VehicleCameraRig? _rig;
    private CameraSensorId[] _cameraOrder = [];
    private int _selectedIndex;
    private bool _previewEnabled = true;

    public VehicleCameraMonitor()
    {
        Name = "VehicleCameraMonitor";
        Layer = 2;

        var panel = new Panel
        {
            Name = "Frame",
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -556.0f,
            OffsetRight = -20.0f,
            OffsetTop = 20.0f,
            OffsetBottom = 390.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.018f, 0.028f, 0.035f, 0.94f),
            BorderColor = new Color("58c8de"),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        _title = new Label
        {
            Name = "Title",
            Position = new Vector2(12.0f, 6.0f),
            Size = new Vector2(512.0f, 25.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _title.AddThemeColorOverride("font_color", new Color("d9f7fb"));
        _title.AddThemeFontSizeOverride("font_size", 17);
        panel.AddChild(_title);

        _details = new Label
        {
            Name = "Details",
            Position = new Vector2(12.0f, 29.0f),
            Size = new Vector2(512.0f, 20.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _details.AddThemeColorOverride("font_color", new Color("8fb3bd"));
        _details.AddThemeFontSizeOverride("font_size", 13);
        panel.AddChild(_details);

        _image = new TextureRect
        {
            Name = "CameraImage",
            Position = new Vector2(12.0f, 54.0f),
            Size = new Vector2(512.0f, 298.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _image.SelfModulate = Colors.White;
        panel.AddChild(_image);
    }

    public VehicleCameraRig? Rig
    {
        get => _rig;
        set
        {
            if (_rig is not null)
            {
                _rig.PreviewChannel = null;
            }

            _rig = value;
            _cameraOrder = _rig?.Specifications.Select(specification => specification.Id).ToArray() ?? [];
            _selectedIndex = FindDefaultCameraIndex(_cameraOrder);
            ApplySelection();
        }
    }

    public CameraSensorId? SelectedCamera => _cameraOrder.Length == 0
        ? null
        : _cameraOrder[_selectedIndex];

    public override void _Process(double delta)
    {
        _ = delta;
        if (_previewEnabled && Godot.Input.IsActionJustPressed(InputBindings.CycleCamera))
        {
            CycleCamera();
        }
    }

    public void CycleCamera()
    {
        if (_cameraOrder.Length == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + 1) % _cameraOrder.Length;
        ApplySelection();
    }

    public void SelectCamera(CameraSensorId id)
    {
        var index = Array.IndexOf(_cameraOrder, id);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "The camera is not available in this rig.");
        }

        _selectedIndex = index;
        ApplySelection();
    }

    public void SetPreviewEnabled(bool enabled)
    {
        _previewEnabled = enabled;
        Visible = enabled;
        ApplySelection();
    }

    private void ApplySelection()
    {
        if (_rig is null || _cameraOrder.Length == 0)
        {
            _image.Texture = null;
            _title.Text = "VEHICLE CAMERAS  •  OFFLINE";
            _details.Text = string.Empty;
            return;
        }

        var id = _cameraOrder[_selectedIndex];
        var specification = _rig.GetSpecification(id);
        _rig.PreviewChannel = _previewEnabled ? id : null;
        _image.Texture = _previewEnabled ? _rig.GetTexture(id) : null;
        _title.Text = $"{FriendlyName(id).ToUpperInvariant()}  •  LIVE    [C / RB]";
        _details.Text =
            $"{specification.ImageWidthPixels} x {specification.ImageHeightPixels}  •  " +
            $"{specification.HorizontalFieldOfViewDegrees:F0}° HFOV  •  " +
            $"{specification.CaptureFrequencyHertz:F0} Hz";
    }

    private static int FindDefaultCameraIndex(IReadOnlyList<CameraSensorId> cameras)
    {
        for (var index = 0; index < cameras.Count; index++)
        {
            if (cameras[index] == CameraSensorId.WindshieldMain)
            {
                return index;
            }
        }

        return 0;
    }

    private static string FriendlyName(CameraSensorId id) => id switch
    {
        CameraSensorId.FrontBumper => "Front bumper",
        CameraSensorId.WindshieldMain => "Windshield main",
        CameraSensorId.WindshieldWide => "Windshield wide",
        CameraSensorId.DoorPillarLeft => "Left pillar",
        CameraSensorId.DoorPillarRight => "Right pillar",
        CameraSensorId.FenderLeft => "Left fender",
        CameraSensorId.FenderRight => "Right fender",
        CameraSensorId.Rear => "Rear",
        _ => id.ToString(),
    };
}

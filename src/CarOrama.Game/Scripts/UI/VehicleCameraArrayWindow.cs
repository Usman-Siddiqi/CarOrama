using CarOrama.Core.Sensors;
using CarOrama.Game.Sensors;
using Godot;

namespace CarOrama.Game.UI;

/// <summary>
/// Opens a native, resizable engineering monitor containing every exterior
/// camera feed. The monitor is deliberately separate from the main simulator
/// viewport so it can be snapped beside the driving view.
/// </summary>
public partial class VehicleCameraArrayWindow : Window
{
    private const int ColumnCount = 4;
    private readonly Dictionary<CameraSensorId, TextureRect> _images = [];
    private VehicleCameraRig? _rig;

    public VehicleCameraArrayWindow()
    {
        Name = "VehicleCameraArrayWindow";
        Title = "CarOrama - Eight-Camera Monitor";
        Visible = false;
        ForceNative = true;
        Size = new Vector2I(1280, 780);
        MinSize = new Vector2I(960, 620);
        Unresizable = false;
        CloseRequested += HandleCloseRequested;

        BuildInterface();
    }

    public VehicleCameraRig? Rig
    {
        get => _rig;
        set
        {
            if (_rig is not null)
            {
                _rig.AllChannelsEnabled = false;
            }

            _rig = value;
            if (_rig is not null)
            {
                _rig.PreviewChannel = null;
                _rig.AllChannelsEnabled = Visible;
            }

            RefreshTextures();
        }
    }

    public void ShowMonitor()
    {
        if (_rig is not null)
        {
            _rig.AllChannelsEnabled = true;
        }

        Show();
        GrabFocus();
    }

    public void HideMonitor()
    {
        if (_rig is not null)
        {
            _rig.AllChannelsEnabled = false;
        }

        Hide();
    }

    public void ToggleMonitor()
    {
        if (Visible)
        {
            HideMonitor();
        }
        else
        {
            ShowMonitor();
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        RefreshTextures();
    }

    private void BuildInterface()
    {
        var background = new ColorRect
        {
            Color = new Color("101820"),
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        background.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(background);

        var margin = new MarginContainer
        {
            Name = "Content",
        };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        outer.AddThemeConstantOverride("separation", 8);
        margin.AddChild(outer);

        var title = new Label
        {
            Text = "EIGHT-CAMERA SENSOR MONITOR",
            CustomMinimumSize = new Vector2(0, 26),
        };
        title.AddThemeColorOverride("font_color", new Color("d9f7fb"));
        title.AddThemeFontSizeOverride("font_size", 18);
        outer.AddChild(title);

        var details = new Label
        {
            Text = "Live engineering view only - these feeds are the same sensor channels used by the vehicle rig.",
            CustomMinimumSize = new Vector2(0, 20),
        };
        details.AddThemeColorOverride("font_color", new Color("8fb3bd"));
        details.AddThemeFontSizeOverride("font_size", 12);
        outer.AddChild(details);

        var grid = new GridContainer
        {
            Name = "CameraGrid",
            Columns = ColumnCount,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        outer.AddChild(grid);

        foreach (var id in Enum.GetValues<CameraSensorId>())
        {
            var tile = new PanelContainer
            {
                Name = $"{id}Tile",
                CustomMinimumSize = new Vector2(300, 245),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            tile.AddThemeStyleboxOverride("panel", CreateTileStyle());
            grid.AddChild(tile);

            var stack = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            stack.AddThemeConstantOverride("separation", 4);
            tile.AddChild(stack);

            var label = new Label
            {
                Text = FriendlyName(id).ToUpperInvariant(),
                CustomMinimumSize = new Vector2(0, 22),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.AddThemeColorOverride("font_color", new Color("d9f7fb"));
            label.AddThemeFontSizeOverride("font_size", 13);
            stack.AddChild(label);

            var image = new TextureRect
            {
                Name = $"{id}Image",
                CustomMinimumSize = new Vector2(300, 205),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Texture = null,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            stack.AddChild(image);
            _images.Add(id, image);
        }
    }

    private void RefreshTextures()
    {
        foreach (var (id, image) in _images)
        {
            image.Texture = _rig is not null && IsInstanceValid(_rig)
                ? _rig.GetTexture(id)
                : null;
        }
    }

    private void HandleCloseRequested() => HideMonitor();

    private static StyleBoxFlat CreateTileStyle() => new()
    {
        BgColor = new Color("182630"),
        BorderColor = new Color("3b6472"),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
    };

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

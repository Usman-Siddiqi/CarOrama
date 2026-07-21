using Godot;

namespace CarOrama.Game.UI;

/// <summary>
/// Renders a second camera from the active vehicle's front bumper into a
/// shared-world subviewport without changing the main follow camera.
/// </summary>
public partial class FrontBumperCameraDisplay : CanvasLayer
{
    private static readonly Transform3D BumperMount = new(
        new Basis(Vector3.Right, Mathf.DegToRad(-2.0f)),
        new Vector3(0.0f, 0.12f, -2.38f));

    private readonly Camera3D _camera;
    private Node3D? _target;

    public FrontBumperCameraDisplay()
    {
        Name = "FrontBumperCameraDisplay";
        Layer = 2;

        var panel = new Panel
        {
            Name = "Frame",
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -556.0f,
            OffsetRight = -20.0f,
            OffsetTop = 20.0f,
            OffsetBottom = 354.0f,
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

        var title = new Label
        {
            Name = "Title",
            Text = "FRONT BUMPER  •  LIVE",
            Position = new Vector2(12.0f, 7.0f),
            Size = new Vector2(512.0f, 26.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", new Color("d9f7fb"));
        title.AddThemeFontSizeOverride("font_size", 17);
        panel.AddChild(title);

        var viewportContainer = new SubViewportContainer
        {
            Name = "ViewportContainer",
            Position = new Vector2(12.0f, 36.0f),
            Size = new Vector2(512.0f, 288.0f),
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddChild(viewportContainer);

        var viewport = new SubViewport
        {
            Name = "FrontBumperViewport",
            Size = new Vector2I(768, 432),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = false,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        viewportContainer.AddChild(viewport);

        _camera = new Camera3D
        {
            Name = "FrontBumperCamera",
            Current = true,
            Fov = 78.0f,
            Near = 0.05f,
            Far = 800.0f,
        };
        viewport.AddChild(_camera);
    }

    public Node3D? Target
    {
        get => _target;
        set
        {
            _target = value;
            UpdateCameraTransform();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        if (_target is null || !IsInstanceValid(_target))
        {
            return;
        }

        _camera.GlobalTransform = _target.GlobalTransform * BumperMount;
    }
}


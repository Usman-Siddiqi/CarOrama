using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using CarOrama.Game.Vehicle;
using Godot;

namespace CarOrama.Game.Environment;

/// <summary>
/// Human-only route preview drawn on the main 3D viewport. It uses render layer
/// two; the vehicle sensor cameras are restricted to layer one, so this geometry
/// cannot leak into recorded observations or training images.
/// </summary>
public partial class FuturePathVisualizer : Node3D
{
    private const int DebugRenderLayer = 2;
    private const double PreviewDistanceMeters = 45.0;
    private const double SampleSpacingMeters = 1.0;
    private readonly MeshInstance3D _corridor;
    private readonly MeshInstance3D _leftBoundary;
    private readonly MeshInstance3D _rightBoundary;
    private readonly StandardMaterial3D _corridorMaterial;
    private readonly StandardMaterial3D _boundaryMaterial;
    private DrivingRoute? _route;
    private Node3D? _target;
    private double _progressMeters;

    public FuturePathVisualizer()
    {
        Name = "FuturePathVisualizer_DebugOnly";
        ProcessMode = ProcessModeEnum.Always;

        _corridorMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.02f, 0.38f, 0.9f, 0.40f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 1,
        };
        _boundaryMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.82f, 0.08f, 0.95f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 2,
        };

        _corridor = CreateMeshInstance("BlueFutureCorridor");
        _leftBoundary = CreateMeshInstance("YellowFutureBoundaryLeft");
        _rightBoundary = CreateMeshInstance("YellowFutureBoundaryRight");
        AddChild(_corridor);
        AddChild(_leftBoundary);
        AddChild(_rightBoundary);
    }

    public DrivingRoute? Route
    {
        get => _route;
        set
        {
            _route = value;
            _progressMeters = 0.0;
            Rebuild();
        }
    }

    public Node3D? Target
    {
        get => _target;
        set => _target = value;
    }

    /// <summary>
    /// Set by the structured simulation environment when a teacher or agent
    /// advances. Manual mode may leave this unset and use target projection.
    /// </summary>
    public double ProgressMeters
    {
        get => _progressMeters;
        set
        {
            _progressMeters = Math.Clamp(value, 0.0, _route?.TotalLengthMeters ?? 0.0);
            Rebuild();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (_route is null || _target is null || !IsInstanceValid(_target))
        {
            SetPreviewVisible(false);
            return;
        }

        // Project the manual vehicle onto the route while allowing only a small
        // amount of backtracking, which keeps the preview stable during resets.
        var worldPosition = new Vector2D(_target.GlobalPosition.X, _target.GlobalPosition.Z);
        var projection = _route.ProjectProgress(
            worldPosition,
            Math.Max(0.0, _progressMeters - 3.0),
            Math.Min(_route.TotalLengthMeters, _progressMeters + 20.0));
        _progressMeters = Math.Max(_progressMeters, projection);
        Rebuild();
    }

    private MeshInstance3D CreateMeshInstance(string nodeName)
    {
        return new MeshInstance3D
        {
            Name = nodeName,
            Layers = 1u << (DebugRenderLayer - 1),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void Rebuild()
    {
        if (_route is null || _route.TotalLengthMeters <= _progressMeters + 0.1)
        {
            SetPreviewVisible(false);
            return;
        }

        var centers = new List<Vector3>();
        var left = new List<Vector3>();
        var right = new List<Vector3>();
        const double corridorHalfWidthMeters = 1.30;
        for (var distance = _progressMeters + 0.5;
             distance <= Math.Min(_route.TotalLengthMeters, _progressMeters + PreviewDistanceMeters);
             distance += SampleSpacingMeters)
        {
            var point = _route.GetPointAtDistance(distance);
            var direction = _route.GetDirectionAtDistance(distance);
            var lateral = new Vector2D(-direction.Y, direction.X);
            var center = new Vector3((float)point.X, 0.17f, (float)point.Y);
            centers.Add(center);
            left.Add(ToWorld(point + (lateral * corridorHalfWidthMeters), 0.18f));
            right.Add(ToWorld(point - (lateral * corridorHalfWidthMeters), 0.18f));
        }

        if (centers.Count < 2)
        {
            SetPreviewVisible(false);
            return;
        }

        _corridor.Mesh = BuildCorridorMesh(left, right);
        _leftBoundary.Mesh = BuildBoundaryMesh(left, 0.10f);
        _rightBoundary.Mesh = BuildBoundaryMesh(right, 0.10f);
        SetPreviewVisible(true);
    }

    private ImmediateMesh BuildCorridorMesh(IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _corridorMaterial);
        for (var index = 0; index < left.Count; index++)
        {
            mesh.SurfaceAddVertex(left[index]);
            mesh.SurfaceAddVertex(right[index]);
        }

        mesh.SurfaceEnd();
        return mesh;
    }

    private ImmediateMesh BuildBoundaryMesh(IReadOnlyList<Vector3> path, float width)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _boundaryMaterial);
        for (var index = 0; index < path.Count; index++)
        {
            var previous = path[Math.Max(0, index - 1)];
            var next = path[Math.Min(path.Count - 1, index + 1)];
            var tangent = new Vector3(next.X - previous.X, 0.0f, next.Z - previous.Z).Normalized();
            var normal = new Vector3(-tangent.Z, 0.0f, tangent.X) * (width * 0.5f);
            mesh.SurfaceAddVertex(path[index] - normal);
            mesh.SurfaceAddVertex(path[index] + normal);
        }

        mesh.SurfaceEnd();
        return mesh;
    }

    private void SetPreviewVisible(bool visible)
    {
        _corridor.Visible = visible;
        _leftBoundary.Visible = visible;
        _rightBoundary.Visible = visible;
    }

    private static Vector3 ToWorld(Vector2D point, float height)
    {
        return new Vector3((float)point.X, height, (float)point.Y);
    }
}
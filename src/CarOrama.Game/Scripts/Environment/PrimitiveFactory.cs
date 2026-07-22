using Godot;

namespace CarOrama.Game.Environment;

internal static class PrimitiveFactory
{
    public static StandardMaterial3D Material(Color color, float roughness = 0.88f, bool emissive = false)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            EmissionEnabled = emissive,
            Emission = emissive ? color : Colors.Black,
            EmissionEnergyMultiplier = emissive ? 2.4f : 0.0f,
        };
    }

    public static Node3D Box(
        string name,
        Vector3 size,
        Transform3D transform,
        Material material,
        bool collision = false,
        bool castShadow = true)
    {
        Node3D root;
        if (collision)
        {
            var body = new StaticBody3D { Name = name };
            body.AddChild(new CollisionShape3D
            {
                Name = "Collision",
                Shape = new BoxShape3D { Size = size },
            });
            root = body;
        }
        else
        {
            root = new Node3D { Name = name };
        }

        root.Transform = transform;
        root.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            CastShadow = castShadow
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    public static MeshInstance3D Cylinder(
        string name,
        float radius,
        float height,
        Material material,
        int radialSegments = 16)
    {
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = radialSegments,
            },
            MaterialOverride = material,
        };
    }

    public static MeshInstance3D Ribbon(
        string name,
        IReadOnlyList<Vector3> firstEdge,
        IReadOnlyList<Vector3> secondEdge,
        float elevation,
        Material material)
    {
        if (firstEdge.Count != secondEdge.Count || firstEdge.Count < 2)
        {
            throw new ArgumentException("Ribbon edges must contain the same number of usable points.");
        }

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index + 1 < firstEdge.Count; index++)
        {
            var first = firstEdge[index] + (Vector3.Up * elevation);
            var second = secondEdge[index] + (Vector3.Up * elevation);
            var nextFirst = firstEdge[index + 1] + (Vector3.Up * elevation);
            var nextSecond = secondEdge[index + 1] + (Vector3.Up * elevation);

            surface.SetNormal(Vector3.Up);
            surface.AddVertex(first);
            surface.AddVertex(second);
            surface.AddVertex(nextFirst);
            surface.AddVertex(second);
            surface.AddVertex(nextSecond);
            surface.AddVertex(nextFirst);
        }

        var ribbonMaterial = (Material)material.Duplicate();
        if (ribbonMaterial is BaseMaterial3D baseMaterial)
        {
            baseMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        return new MeshInstance3D
        {
            Name = name,
            Mesh = surface.Commit(),
            MaterialOverride = ribbonMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
    }

    public static Transform3D OrientedBoxTransform(Vector3 start, Vector3 end, float elevation)
    {
        var direction = (end - start).Normalized();
        var yaw = Mathf.Atan2(direction.X, direction.Z);
        var basis = Basis.Identity.Rotated(Vector3.Up, yaw);
        var position = ((start + end) * 0.5f) + (Vector3.Up * elevation);
        return new Transform3D(basis, position);
    }
}

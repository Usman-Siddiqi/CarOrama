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
        bool collision = false)
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
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
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

    public static Transform3D OrientedBoxTransform(Vector3 start, Vector3 end, float elevation)
    {
        var direction = (end - start).Normalized();
        var yaw = Mathf.Atan2(direction.X, direction.Z);
        var basis = Basis.Identity.Rotated(Vector3.Up, yaw);
        var position = ((start + end) * 0.5f) + (Vector3.Up * elevation);
        return new Transform3D(basis, position);
    }
}


using Godot;

namespace CarOrama.Game.Vehicle;

public partial class FollowVehicleCamera : Camera3D
{
    public FollowVehicleCamera()
    {
        Name = "FollowCamera";
        Current = true;
        Far = 800.0f;
        Fov = 68.0f;
    }

    public Node3D? Target { get; set; }

    public override void _PhysicsProcess(double delta)
    {
        if (Target is null || !IsInstanceValid(Target))
        {
            return;
        }

        var forward = -Target.GlobalTransform.Basis.Z.Normalized();
        var desiredPosition = Target.GlobalPosition - (forward * 8.2f) + (Vector3.Up * 3.25f);
        var blend = 1.0f - Mathf.Exp((float)(-7.5 * delta));
        GlobalPosition = GlobalPosition.Lerp(desiredPosition, blend);
        LookAt(Target.GlobalPosition + (forward * 4.8f) + (Vector3.Up * 0.55f), Vector3.Up);
    }
}

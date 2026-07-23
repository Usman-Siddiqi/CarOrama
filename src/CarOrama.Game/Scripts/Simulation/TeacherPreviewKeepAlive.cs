using Godot;

namespace CarOrama.Game.Simulation;

/// <summary>
/// Keeps the Godot presentation loop alive for the optional visible teacher
/// preview. Dataset recording does not create this node and remains paused
/// between exact control steps.
/// </summary>
public partial class TeacherPreviewKeepAlive : Node
{
    public TeacherPreviewKeepAlive()
    {
        Name = "TeacherPreviewKeepAlive";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (GetTree().Paused)
        {
            GetTree().Paused = false;
        }

        var window = GetWindow();
        if (window is not null && !window.Visible)
        {
            window.Mode = Window.ModeEnum.Windowed;
            window.Show();
            window.GrabFocus();
        }
    }
}
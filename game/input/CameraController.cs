using Godot;

namespace Wunderwaffe.Game.Input;

/// <summary>Keyboard / middle-drag pan and wheel zoom toward the cursor.</summary>
public partial class CameraController : Camera2D
{
    [Export] public float PanSpeed = 900f;
    [Export] public float MinZoom = 0.25f;
    [Export] public float MaxZoom = 4f;
    [Export] public float ZoomStep = 1.15f;

    public override void _Process(double delta)
    {
        var dir = Godot.Input.GetVector(InputActions.CamLeft, InputActions.CamRight, InputActions.CamUp, InputActions.CamDown);
        Position += dir * PanSpeed * (float)delta / Zoom.X;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Middle) != 0:
                Position -= m.Relative / Zoom.X;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomAtCursor(ZoomStep);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomAtCursor(1 / ZoomStep);
                break;
        }
    }

    void ZoomAtCursor(float factor)
    {
        var before = GetGlobalMousePosition();
        float z = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(z, z);
        ForceUpdateScroll();
        Position += before - GetGlobalMousePosition();
    }
}

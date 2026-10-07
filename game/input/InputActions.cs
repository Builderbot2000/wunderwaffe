using Godot;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Default key bindings, registered as named actions so they can be rebound later (UI-005).
/// Registered in code rather than project.godot to keep the defaults readable.
/// </summary>
public static class InputActions
{
    public const string CamLeft = "cam_left";
    public const string CamRight = "cam_right";
    public const string CamUp = "cam_up";
    public const string CamDown = "cam_down";
    public const string Rotate = "build_rotate";
    public const string Cancel = "build_cancel";
    public const string Stress = "debug_stress";
    public const int Slots = 9;

    public static string Slot(int i) => $"build_slot_{i + 1}";

    public static void Register()
    {
        Add(CamLeft, Key.A, Key.Left);
        Add(CamRight, Key.D, Key.Right);
        Add(CamUp, Key.W, Key.Up);
        Add(CamDown, Key.S, Key.Down);
        Add(Rotate, Key.R);
        Add(Cancel, Key.Escape);
        Add(Stress, Key.F3);
        for (int i = 0; i < Slots; i++)
            Add(Slot(i), Key.Key1 + i);
    }

    static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}

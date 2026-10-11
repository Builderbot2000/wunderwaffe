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
    /// <summary>Esc: drops the picked building, then clears the unit selection, then opens the pause menu.</summary>
    public const string Cancel = "build_cancel";
    public const string Stress = "debug_stress";
    public const string DebugCheat = "debug_cheat";
    public const string DebugStats = "debug_stats";
    public const string Help = "ui_help";
    public const string WorldToggle = "world_toggle";
    public const string WorldEnter = "world_enter";
    public const string WorldClaim = "world_claim";
    public const string UnitToggle = "unit_toggle";
    /// <summary>B: opens or closes the blueprint library (FB-009).</summary>
    public const string BlueprintLibrary = "blueprint_library";
    /// <summary>F: mirrors the blueprint being placed.</summary>
    public const string BlueprintFlip = "blueprint_flip";
    /// <summary>K: records the design at the hovered unit core into the library (UD-001).</summary>
    public const string BlueprintRecord = "blueprint_record";
    public const int Slots = 10;

    /// <summary>Slots 1-9 are the number keys; the tenth is 0.</summary>
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
        Add(DebugCheat, Key.F4);
        Add(DebugStats, Key.F2);
        Add(Help, Key.F1);
        Add(WorldToggle, Key.Tab);
        Add(WorldEnter, Key.Enter, Key.KpEnter);
        Add(WorldClaim, Key.C);
        Add(UnitToggle, Key.G);
        Add(BlueprintLibrary, Key.B);
        Add(BlueprintFlip, Key.F);
        Add(BlueprintRecord, Key.K);
        for (int i = 0; i < Slots; i++)
            Add(Slot(i), i == 9 ? Key.Key0 : Key.Key1 + i);
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

using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Default key bindings, registered as named actions so they can be rebound (UI-005: <see cref="Rebind"/>, saved by
/// <c>GameSettings</c>). Registered in code rather than project.godot to keep the defaults readable. A binding is a
/// physical key plus modifier bits, as <see cref="InputEventKey.GetPhysicalKeycodeWithModifiers"/> gives it.
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
        for (int i = 0; i < Slots; i++)
            Add(Slot(i), i == 9 ? Key.Key0 : Key.Key1 + i);
    }

    static readonly List<string> _order = [];
    static readonly Dictionary<string, Key[]> _defaults = [];

    /// <summary>Names the rebinding screen shows; actions not listed show their id.</summary>
    static readonly Dictionary<string, string> Names = new()
    {
        [CamLeft] = "Pan left", [CamRight] = "Pan right", [CamUp] = "Pan up", [CamDown] = "Pan down",
        [Rotate] = "Rotate building", [Cancel] = "Cancel / menu", [Stress] = "Debug: stress test", [DebugCheat] = "Debug: add items",
        [DebugStats] = "Debug: performance", [Help] = "Help", [WorldToggle] = "World map / tile", [WorldEnter] = "Open tile",
        [WorldClaim] = "Claim tile", [UnitToggle] = "Lift off / land",
    };

    /// <summary>Every action registered here, in registration order.</summary>
    public static IReadOnlyList<string> All => _order;

    public static string Name(string action) =>
        Names.TryGetValue(action, out var name) ? name
        : action.StartsWith("build_slot_") ? $"Build slot {action["build_slot_".Length..]}"
        : action.Replace('_', ' ');

    /// <summary>The action's keys, as physical key codes with modifier bits.</summary>
    public static IReadOnlyList<Key> Keys(string action) =>
        InputMap.ActionGetEvents(action).OfType<InputEventKey>().Select(k => k.GetPhysicalKeycodeWithModifiers()).ToList();

    /// <summary>"Ctrl+W" and the like.</summary>
    public static string KeyText(Key key) => OS.GetKeycodeString(key);

    /// <summary>Makes <paramref name="key"/> the action's only key.</summary>
    public static void Rebind(string action, Key key) => Set(action, [key]);

    /// <summary>Puts every action back to its default keys.</summary>
    public static void ResetAll()
    {
        foreach (var action in _order)
            Set(action, _defaults[action]);
    }

    /// <summary>True if the action's keys differ from its defaults (what settings save).</summary>
    public static bool Changed(string action) => !Keys(action).SequenceEqual(_defaults[action]);

    public static void Set(string action, IEnumerable<Key> keys)
    {
        InputMap.ActionEraseEvents(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, Event(key));
    }

    static InputEventKey Event(Key key)
    {
        long mods = (long)key & (long)KeyModifierMask.ModifierMask;
        return new InputEventKey
        {
            PhysicalKeycode = (Key)((long)key & (long)KeyModifierMask.CodeMask),
            CtrlPressed = (mods & (long)KeyModifierMask.MaskCtrl) != 0,
            ShiftPressed = (mods & (long)KeyModifierMask.MaskShift) != 0,
            AltPressed = (mods & (long)KeyModifierMask.MaskAlt) != 0,
            MetaPressed = (mods & (long)KeyModifierMask.MaskMeta) != 0,
        };
    }

    static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        _order.Add(action);
        _defaults[action] = keys;
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, Event(key));
    }
}

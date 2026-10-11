using System;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;

namespace Wunderwaffe.Game.Screens;

/// <summary>Which alert groups the HUD lists (UI-003).</summary>
[Flags]
public enum AlertFilter
{
    Combat = 1,
    Production = 2,
    Units = 4,
    Territory = 8,
    All = Combat | Production | Units | Territory,
}

/// <summary>Player settings, kept in <c>user://settings.cfg</c> and applied at startup.</summary>
public static class GameSettings
{
    const string Path = "user://settings.cfg";
    const string Section = "display";
    const string MapSection = "map";
    const string HudSection = "hud";
    const string KeySection = "keys";

    public const int MinUiScale = 75, MaxUiScale = 150;

    public const int MaxSnapshotEvery = 20;

    public static bool Fullscreen { get; private set; }
    public static bool VSync { get; private set; } = true;

    /// <summary>The world map redraws its blurred pictures of the tiles every this many switches between views.</summary>
    public static int SnapshotEvery { get; private set; } = 3;

    /// <summary>
    /// Size of menus and HUD, in percent (UI-005): the window's content scale, so the map's default zoom grows with it
    /// (the mouse wheel still zooms it freely).
    /// </summary>
    public static int UiScale { get; private set; } = 100;

    /// <summary>Player and signal colours (UI-005): normal or one of the colour-blind sets.</summary>
    public static ColourMode Colours { get; private set; }

    /// <summary>Alert groups shown in the HUD's alert list.</summary>
    public static AlertFilter Alerts { get; private set; } = AlertFilter.All;

    /// <summary>Raised after any setting changes, so open settings panels can refresh.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            Fullscreen = (bool)cfg.GetValue(Section, "fullscreen", false);
            VSync = (bool)cfg.GetValue(Section, "vsync", true);
            SnapshotEvery = Math.Clamp((int)cfg.GetValue(MapSection, "snapshot_every", 3), 1, MaxSnapshotEvery);
            Alerts = (AlertFilter)(int)cfg.GetValue(HudSection, "alerts", (int)AlertFilter.All) & AlertFilter.All;
            UiScale = Math.Clamp((int)cfg.GetValue(Section, "ui_scale", 100), MinUiScale, MaxUiScale);
            Colours = (ColourMode)Math.Clamp((int)cfg.GetValue(Section, "colours", 0), 0, (int)ColourMode.BlueYellow);
            if (cfg.HasSection(KeySection))
            {
                foreach (string action in cfg.GetSectionKeys(KeySection).Where(a => InputMap.HasAction(a)))
                {
                    var keys = ((string)cfg.GetValue(KeySection, action, "")).Split(',', StringSplitOptions.RemoveEmptyEntries);
                    InputActions.Set(action, keys.Select(k => (Key)long.Parse(k)));
                }
            }
        }
        Apply();
    }

    public static void SetFullscreen(bool on)
    {
        Fullscreen = on;
        Apply();
        Save();
    }

    public static void SetVSync(bool on)
    {
        VSync = on;
        Apply();
        Save();
    }

    public static void SetSnapshotEvery(int n)
    {
        SnapshotEvery = Math.Clamp(n, 1, MaxSnapshotEvery);
        Save();
        Changed?.Invoke();
    }

    public static void SetUiScale(int percent)
    {
        UiScale = Math.Clamp(percent, MinUiScale, MaxUiScale);
        Apply();
        Save();
    }

    public static void SetColours(ColourMode mode)
    {
        Colours = mode;
        Apply();
        Save();
    }

    /// <summary>Saves the key bindings after a rebind (they live in the input map, not here).</summary>
    public static void SaveKeys() => Save();

    public static void SetAlert(AlertFilter filter, bool on)
    {
        Alerts = on ? Alerts | filter : Alerts & ~filter;
        Save();
    }

    static void Apply()
    {
        Palette.Mode = Colours;
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Root.ContentScaleFactor = UiScale / 100f;
        // Headless runs have no window to change.
        if (DisplayServer.GetName() == "headless")
            return;
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Changed?.Invoke();
    }

    static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "fullscreen", Fullscreen);
        cfg.SetValue(Section, "vsync", VSync);
        cfg.SetValue(MapSection, "snapshot_every", SnapshotEvery);
        cfg.SetValue(HudSection, "alerts", (int)Alerts);
        cfg.SetValue(Section, "ui_scale", UiScale);
        cfg.SetValue(Section, "colours", (int)Colours);
        foreach (string action in InputActions.All.Where(InputActions.Changed))
            cfg.SetValue(KeySection, action, string.Join(",", InputActions.Keys(action).Select(k => ((long)k).ToString())));
        cfg.Save(Path);
    }

    /// <summary>The settings controls, for the title screen and the pause menu.</summary>
    public static Control BuildPanel(Action onBack)
    {
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading("Settings"));
        var full = new CheckButton { Text = "Fullscreen", ButtonPressed = Fullscreen, FocusMode = Control.FocusModeEnum.None };
        full.Toggled += SetFullscreen;
        var vsync = new CheckButton { Text = "V-Sync", ButtonPressed = VSync, FocusMode = Control.FocusModeEnum.None };
        vsync.Toggled += SetVSync;
        col.AddChild(full);
        col.AddChild(vsync);

        var every = Ui.Row(8);
        every.AddChild(Ui.Label("Refresh world map pictures every", Ui.SmallFont, Ui.Text));
        var spin = new SpinBox
        {
            MinValue = 1, MaxValue = MaxSnapshotEvery, Step = 1, Value = SnapshotEvery,
            FocusMode = Control.FocusModeEnum.None, TooltipText = "Tab presses between the world map and a tile",
        };
        spin.ValueChanged += v => SetSnapshotEvery((int)v);
        every.AddChild(spin);
        every.AddChild(Ui.Label("view switches", Ui.SmallFont, Ui.Text));
        col.AddChild(every);
        var scale = Ui.Row(8);
        scale.AddChild(Ui.Label("Interface size", Ui.SmallFont, Ui.Text));
        var scaleSpin = new SpinBox
        {
            MinValue = MinUiScale, MaxValue = MaxUiScale, Step = 5, Value = UiScale, Suffix = " %", FocusMode = Control.FocusModeEnum.None,
        };
        scaleSpin.ValueChanged += v => SetUiScale((int)v);
        scale.AddChild(scaleSpin);
        col.AddChild(scale);

        var colours = Ui.Row(8);
        colours.AddChild(Ui.Label("Colours", Ui.SmallFont, Ui.Text));
        var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        pick.AddItem("Normal");
        pick.AddItem("Red-green safe");
        pick.AddItem("Blue-yellow safe");
        pick.Selected = (int)Colours;
        pick.ItemSelected += i => SetColours((ColourMode)(int)i);
        colours.AddChild(pick);
        col.AddChild(colours);

        // The bindings screen replaces this one in the same spot.
        var holder = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var panel = Ui.Panel(col, 360);
        col.AddChild(Ui.Button("Key bindings…", () =>
        {
            panel.Visible = false;
            KeyBindingsPanel? keys = null;
            keys = KeyBindingsPanel.Build(() => { keys!.QueueFree(); panel.Visible = true; });
            holder.AddChild(keys);
        }));
        col.AddChild(Ui.Label("Audio comes later.", Ui.SmallFont, Ui.Muted));
        col.AddChild(Ui.Button("Back", onBack));
        holder.AddChild(panel);
        return holder;
    }
}

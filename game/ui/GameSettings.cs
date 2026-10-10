using System;
using Godot;

namespace Wunderwaffe.Game.Screens;

/// <summary>Player settings, kept in <c>user://settings.cfg</c> and applied at startup.</summary>
public static class GameSettings
{
    const string Path = "user://settings.cfg";
    const string Section = "display";
    const string MapSection = "map";

    public const int MaxSnapshotEvery = 20;

    public static bool Fullscreen { get; private set; }
    public static bool VSync { get; private set; } = true;

    /// <summary>The world map redraws its blurred pictures of the tiles every this many switches between views.</summary>
    public static int SnapshotEvery { get; private set; } = 3;

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

    static void Apply()
    {
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
        col.AddChild(Ui.Label("Key bindings and audio come later (UI-005).", Ui.SmallFont, Ui.Muted));
        col.AddChild(Ui.Button("Back", onBack));
        return Ui.Panel(col, 360);
    }
}

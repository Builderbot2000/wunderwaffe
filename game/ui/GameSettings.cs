using System;
using Godot;

namespace Wunderwaffe.Game.Screens;

/// <summary>Player settings, kept in <c>user://settings.cfg</c> and applied at startup.</summary>
public static class GameSettings
{
    const string Path = "user://settings.cfg";
    const string Section = "display";

    public static bool Fullscreen { get; private set; }
    public static bool VSync { get; private set; } = true;

    /// <summary>Raised after any setting changes, so open settings panels can refresh.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            Fullscreen = (bool)cfg.GetValue(Section, "fullscreen", false);
            VSync = (bool)cfg.GetValue(Section, "vsync", true);
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
        col.AddChild(Ui.Label("Key bindings and audio come later (UI-005).", Ui.SmallFont, Ui.Muted));
        col.AddChild(Ui.Button("Back", onBack));
        return Ui.Panel(col, 360);
    }
}

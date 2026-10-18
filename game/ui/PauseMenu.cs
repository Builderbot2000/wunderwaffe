using System;
using Godot;
using Wunderwaffe.Game.Input;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Esc menu: pauses the match (the sim stops with the scene tree) and offers save, load, settings and quitting. In
/// multiplayer nothing pauses (OQ-32): the menu opens over a running match and loading is off.
/// Placed first in the scene so it only sees Esc after the build and unit tools have had their turn to use it.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    SimHost _host = null!;
    Control _root = null!;
    Control _main = null!;
    Control? _sub;
    Label _status = null!;
    Label _heading = null!;
    Button _load = null!;

    public bool IsOpen => _root.Visible;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;

        _root = Ui.Overlay();
        _root.MouseFilter = Control.MouseFilterEnum.Stop;
        _root.Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);
        AddChild(_root);

        var col = Ui.Column(10);
        _heading = Ui.Label("Paused", 30, Ui.Accent);
        col.AddChild(_heading);
        col.AddChild(Ui.Button("Resume", Close, 280));
        col.AddChild(Ui.Button("Save game", Save));
        var scenario = Ui.Button("Save as scenario", SaveScenario);
        scenario.TooltipText = "Saves this map and everything on it as a scenario to start new games from (New game · Map)";
        col.AddChild(scenario);
        _load = Ui.Button("Load game", () => Show(LoadMenu.Build(Load, Back)));
        col.AddChild(_load);
        col.AddChild(Ui.Button("Settings", () => Show(GameSettings.BuildPanel(Back))));
        col.AddChild(Ui.Button("Quit to title", () => _host.QuitToTitle()));
        col.AddChild(Ui.Button("Quit to desktop", () => GetTree().Quit()));
        _status = Ui.Label("", Ui.SmallFont, Ui.Good, 280);
        col.AddChild(_status);
        _main = Ui.Pin(Ui.Panel(col), Control.LayoutPreset.Center);
        _root.AddChild(_main);
    }

    public void Open()
    {
        _status.Text = "";
        _root.Visible = true;
        bool online = _host.IsMultiplayer;
        _heading.Text = online ? "Menu · the match goes on" : "Paused";
        _load.Disabled = online;
        GetTree().Paused = !online;
    }

    public void Close()
    {
        Back();
        _root.Visible = false;
        GetTree().Paused = false;
    }

    void Show(Control panel)
    {
        Back();
        _sub = Ui.Pin(panel, Control.LayoutPreset.Center);
        _main.Visible = false;
        _root.AddChild(_sub);
    }

    void Back()
    {
        _sub?.QueueFree();
        _sub = null;
        _main.Visible = true;
    }

    void Save()
    {
        string? path = _host.Save();
        _status.Text = path == null ? "Couldn't save the game." : $"Saved as {path.GetFile()}";
        _status.AddThemeColorOverride("font_color", path == null ? Ui.Bad : Ui.Good);
    }

    void SaveScenario()
    {
        string? name = _host.SaveScenario();
        _status.Text = name == null ? "Couldn't save the scenario." : $"Scenario \"{name}\" saved";
        _status.AddThemeColorOverride("font_color", name == null ? Ui.Bad : Ui.Good);
    }

    /// <summary>The title screen does the loading, with its progress bar.</summary>
    void Load(string path)
    {
        _host.PendingLoad = path;
        _host.QuitToTitle();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed(InputActions.Cancel))
            return;
        if (!IsOpen)
            Open();
        else if (_sub != null)
            Back();
        else
            Close();
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>The list of saves with load and delete buttons, shared by the title screen and the pause menu.</summary>
public static class LoadMenu
{
    public static Control Build(Action<string> onLoad, Action onBack)
    {
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading("Load game"));
        var list = Ui.Column(6);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(560, 360),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(list);
        col.AddChild(scroll);
        Fill(list, onLoad);
        col.AddChild(Ui.Button("Back", onBack));
        return Ui.Panel(col);
    }

    static void Fill(VBoxContainer list, Action<string> onLoad)
    {
        Ui.Clear(list);
        var saves = SaveSlots.List();
        if (saves.Count == 0)
            list.AddChild(Ui.Label("No saves yet. Save from the Esc menu during a game.", Ui.FontSize, Ui.Muted));
        foreach (var s in saves)
        {
            var row = Ui.Row(6);
            var load = Ui.Button($"{s.SavedAt:ddd d MMM, HH:mm}   {s.Label}", () => onLoad(s.Path));
            load.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            load.Alignment = HorizontalAlignment.Left;
            load.ClipText = true;
            row.AddChild(load);
            row.AddChild(Ui.Button("Delete", () =>
            {
                SaveSlots.Delete(s.Path);
                Fill(list, onLoad);
            }));
            list.AddChild(row);
        }
    }
}

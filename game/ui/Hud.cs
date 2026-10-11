using System.Collections.Generic;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The in-game HUD (UI-001, UI-002), built in code. This file has the layout, overlays and per-frame refresh; each panel's
/// contents live in a partial next to it:
/// <list type="bullet">
/// <item><c>Hud.Objectives.cs</c>: current goal and toasts (top right).</item>
/// <item><c>Hud.SidePanel.cs</c>: the viewed tile's inventory, or the selected world tile with claim/open (left).</item>
/// <item><c>Hud.BuildBar.cs</c>: build bar and what the picked building would do (bottom centre).</item>
/// <item><c>Hud.Hover.cs</c>: what's under the cursor (bottom left).</item>
/// </list>
/// F1 opens the controls, F2 the performance line.
/// </summary>
public partial class Hud : Control
{
    const double SlowRefresh = 0.2;
    const int SideWidth = 400;
    const int RightWidth = 420;

    [Export] public BuildTool BuildTool = null!;
    [Export] public WorldMapView WorldMap = null!;
    [Export] public UnitTool UnitTool = null!;
    [Export] public PauseMenu PauseMenu = null!;

    SimHost _host = null!;
    ContentDb Content => _host.Content;
    Simulation Sim => _host.Sim;
    int Me => _host.LocalPlayer;
    PlayerState? MyState => Sim.PlayerOrNull(Me);

    Label _title = null!, _clock = null!, _stats = null!;
    VBoxContainer _sideInfo = null!;
    Button _claim = null!, _enter = null!;
    Label _goalStep = null!, _goalName = null!, _goalText = null!, _goalCount = null!;
    ProgressBar _goalBar = null!;
    VBoxContainer _toasts = null!;
    Control _buildArea = null!;
    readonly List<Button> _toolButtons = [];
    PanelContainer _placePanel = null!;
    Label _placeLine = null!;
    PanelContainer _hoverPanel = null!;
    VBoxContainer _hover = null!;
    Label _hints = null!;
    Control _help = null!, _complete = null!;

    int _seenGoals;
    Control? _welcome;
    bool _showStats;
    double _slowTimer;
    int _shownTileId = -1, _shownSelection;
    string _sideKey = "";

    public override void _Ready()
    {
        _host = SimHost.Instance;
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        BuildLeft();
        BuildRight();
        BuildBottom();
        BuildOverlays();
        BuildBlueprints();
        UnitTool.Notice += text => Toast(text, Ui.Warn);

        _seenGoals = MyState?.GoalsDone ?? 0;
        _host.WatchView(this, OnViewChanged);

        if (_host.LoadMessage is { } msg)
        {
            Toast(msg, Ui.Warn, 8);
            _host.LoadMessage = null;
        }
        else if (Sim.Tick == 0)
        {
            _welcome = Toast("Your home tile is outlined on the map. Double-click it, or press Enter, to start building.", Ui.Accent, 30);
        }
    }

    void OnViewChanged()
    {
        bool tile = _host.View == ViewMode.Tile;
        if (tile && IsInstanceValid(_welcome))
            _welcome!.QueueFree();
        _buildArea.Visible = tile;
        _claim.GetParent<Control>().Visible = !tile;
        _sideKey = "";
        RefreshSlow();
    }

    // ---- Layout ----

    void BuildLeft()
    {
        var col = Ui.Pin(Ui.Column(8), LayoutPreset.TopLeft);
        col.CustomMinimumSize = new Vector2(SideWidth, 0);
        AddChild(col);

        var top = Ui.Column(2);
        var row = Ui.Row();
        _title = Ui.Label("", 20, Ui.Text);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _clock = Ui.Label("", Ui.FontSize, Ui.Muted);
        row.AddChild(_title);
        row.AddChild(_clock);
        top.AddChild(row);
        _stats = Ui.Label("", Ui.SmallFont, Ui.Muted);
        _stats.Visible = false;
        top.AddChild(_stats);
        col.AddChild(Ui.Panel(top));

        var side = Ui.Column(8);
        _sideInfo = Ui.Column(4);
        side.AddChild(_sideInfo);
        var buttons = Ui.Row();
        _claim = Ui.Button("Send units (C)", SendToCircle);
        _enter = Ui.Button("Open tile (Enter)", () => _host.EnterTile(SelectedWorldTile().Id));
        buttons.AddChild(_claim);
        buttons.AddChild(_enter);
        side.AddChild(buttons);
        col.AddChild(Ui.Panel(side));
    }

    void BuildRight()
    {
        var col = Ui.Pin(Ui.Column(10), LayoutPreset.TopRight);
        col.CustomMinimumSize = new Vector2(RightWidth, 0);
        AddChild(col);

        var goal = Ui.Column(6);
        _goalStep = Ui.Heading("");
        _goalName = Ui.Label("", 22, Ui.Text);
        _goalText = Ui.Label("", Ui.FontSize, Ui.Muted, RightWidth - 24);
        var bar = Ui.Row();
        _goalBar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _goalCount = Ui.Label("", Ui.SmallFont, Ui.Text);
        bar.AddChild(_goalBar);
        bar.AddChild(_goalCount);
        goal.AddChild(_goalStep);
        goal.AddChild(_goalName);
        goal.AddChild(_goalText);
        goal.AddChild(bar);
        col.AddChild(Ui.Panel(goal));

        _toasts = Ui.Column(6);
        col.AddChild(_toasts);
    }

    void BuildBottom()
    {
        // Build bar and placement line, bottom centre.
        var area = Ui.Pin(Ui.Column(6), LayoutPreset.CenterBottom);
        area.Alignment = BoxContainer.AlignmentMode.End;
        _buildArea = area;
        AddChild(area);

        _placeLine = Ui.Label("", Ui.FontSize);
        _placeLine.HorizontalAlignment = HorizontalAlignment.Center;
        _placePanel = Ui.Panel(_placeLine);
        _placePanel.MouseFilter = MouseFilterEnum.Ignore;
        _placePanel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        area.AddChild(_placePanel);

        var barPanel = Ui.Panel(BuildToolButtons());
        barPanel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        area.AddChild(barPanel);

        // Hover info, bottom left.
        _hover = Ui.Column(4);
        _hoverPanel = Ui.Pin(Ui.Panel(_hover, SideWidth), LayoutPreset.BottomLeft);
        _hoverPanel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_hoverPanel);

        // Hints and menu buttons, bottom right.
        var right = Ui.Pin(Ui.Column(6), LayoutPreset.BottomRight);
        right.Alignment = BoxContainer.AlignmentMode.End;
        _hints = Ui.Label("", Ui.SmallFont, Ui.Muted);
        _hints.HorizontalAlignment = HorizontalAlignment.Right;
        right.AddChild(_hints);
        var buttons = Ui.Row(6);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        buttons.AddChild(Ui.Button("Blueprints  B", ToggleLibrary));
        buttons.AddChild(Ui.Button("Help  F1", () => Toggle(_help)));
        buttons.AddChild(Ui.Button("Menu  Esc", PauseMenu.Open));
        right.AddChild(buttons);
        AddChild(right);
    }

    void BuildOverlays()
    {
        _help = Modal("Controls", HelpText());

        var done = Ui.Column(10);
        done.AddChild(Ui.Label("You found coal, linked your tiles, made steel and flew your first unit. " +
                               "That's the whole demo loop. Keep building, or save and come back later.",
            Ui.FontSize, Ui.Muted, 460));
        var row = Ui.Row();
        row.AddChild(Ui.Button("Keep playing", () => _complete.Visible = false));
        row.AddChild(Ui.Button("Save game", () => { SaveNow(); _complete.Visible = false; }));
        row.AddChild(Ui.Button("Quit to title", _host.QuitToTitle));
        done.AddChild(row);
        _complete = Modal("Demo complete", null, done, closable: false);
    }

    /// <summary>A centred panel, hidden until opened.</summary>
    Control Modal(string title, string? text, Control? body = null, bool closable = true)
    {
        var col = Ui.Column(10);
        var head = Ui.Row();
        var heading = Ui.Label(title, 26, Ui.Accent);
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(heading);
        col.AddChild(head);
        if (text != null)
            col.AddChild(Ui.Label(text, Ui.FontSize, Ui.Text, 640));
        if (body != null)
            col.AddChild(body);
        var panel = Ui.Pin(Ui.Panel(col, 520), LayoutPreset.Center);
        if (closable)
            head.AddChild(Ui.Button("Close", () => panel.Visible = false));
        panel.Visible = false;
        AddChild(panel);
        return panel;
    }

    void Toggle(Control c)
    {
        bool show = !c.Visible;
        _help.Visible = false;
        c.Visible = show;
    }

    static string HelpText() => string.Join("\n", [
        "World map",
        "    Click selects a tile · double-click or Enter opens one of yours · C claims the selected tile",
        "    Tab toggles between the world map and the last tile you viewed",
        "    The map is a blurred picture of each tile, refreshed every few toggles (Settings)",
        "",
        "Tile",
        "    1-9, 0 or the build bar picks a building · R rotates · Esc drops it",
        "    Left-click / drag places · right-click / drag removes (full refund)",
        "    Drills sit on ore. Belts carry items the way they face; machines push output into anything next to them.",
        "    Ports go in the I/O zones along the tile's borders: one zone per neighbour, on the side facing it.",
        "    An export port sends to the import ports in the neighbour's zone facing back; items flow both ways.",
        "    Water and peaks can't be built on.",
        "",
        "Units",
        "    Lay frame (0) under a unit core and thrusters, hover the core with no building picked and press G",
        "    Left-click / drag selects units · right-click moves them · past the border goes to the next tile",
        "    G with units selected lands them",
        "    K over a unit core records its design · B opens the blueprint library · R turns and F mirrors a picked blueprint",
        "    Click a unit factory to select it, then pick a blueprint for it to build within its radius",
        "    H hold · P then right-click patrols · X retreats · Q makes a squad · T formation · V stance · E escorts the unit under the cursor",
        "    Right-click an enemy unit or building attacks it · Z then right-click attack-moves (fights on the way)",
        "    Ctrl+1-9 stores a control group, Alt+1-9 recalls it · Y over a unit core, then right-click: its rally point",
        "",
        "Camera: WASD or arrows pan · wheel zooms · middle-drag pans",
        "Debug: F2 performance · F3 stress test · F4 add 1000 of each item",
    ]);

    // ---- Per frame ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(InputActions.Help))
            Toggle(_help);
        else if (e.IsActionPressed(InputActions.DebugStats))
            _stats.Visible = _showStats = !_showStats;
        else if (e.IsActionPressed(InputActions.Cancel) && _help.Visible)
            _help.Visible = false;
        else if (e.IsActionPressed(InputActions.BlueprintLibrary) && _host.View == ViewMode.Tile)
            ToggleLibrary();
        else if (e.IsActionPressed(InputActions.Cancel) && _library.Visible)
            _library.Visible = false;
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _clock.Text = Ui.Clock(Sim.Tick);
        if (_showStats)
            _stats.Text = $"{Engine.GetFramesPerSecond()} fps · sim {_host.StepMs:F3} ms/tick · tick {Sim.Tick}";
        UpdateBuildBar();
        CheckProgress();

        // A new tile picked on the world map, or a changed unit selection, shows at once rather than at the next slow refresh.
        int selected = _host.View == ViewMode.World ? WorldMap.SelectedTileId : -1;
        _slowTimer -= delta;
        if (_slowTimer <= 0 || selected != _shownTileId || _host.Selected.Count != _shownSelection)
        {
            _shownTileId = selected;
            _shownSelection = _host.Selected.Count;
            _slowTimer = SlowRefresh;
            RefreshSlow();
        }
    }

    /// <summary>Everything that reads a lot of sim state, a few times a second rather than every frame.</summary>
    void RefreshSlow()
    {
        RefreshGoal();
        RefreshBlueprints();
        if (_host.View == ViewMode.Tile)
        {
            _title.Text = _host.ViewedTile.Name;
            RefreshTileSide();
            RefreshHover();
            _hints.Text = BuildTool.HasTool
                ? "R rotate · drag to place a line · right-click removes · Esc drops the building"
                : "Tab world map · 1-9 pick a building · drag to select units";
        }
        else
        {
            _title.Text = "World map";
            RefreshWorldSide();
            _hoverPanel.Visible = _host.Selected.Count > 0 && SetHover(UnitTool.Status);
            _hints.Text = "Double-click your tile to open it · C claims · Tab back";
        }
    }

    void SaveNow()
    {
        if (_host.Save() is { } path)
            Toast($"Saved ({path.GetFile()})", Ui.Good);
        else
            Toast("Couldn't save the game", Ui.Bad);
    }
}

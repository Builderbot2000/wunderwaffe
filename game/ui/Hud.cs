using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The in-game HUD (UI-001, UI-002): where you are and the clock (top left); the viewed tile's inventory, or on the
/// world map the selected tile with claim/open buttons (left); the current objective and notifications (top right);
/// the build bar and what the picked building would do (bottom centre); what's under the cursor (bottom left).
/// F1 opens the controls, T the tech tree, F2 the performance line.
/// </summary>
public partial class Hud : Control
{
    const double SlowRefresh = 0.2;
    const int SideWidth = 400;
    const int RightWidth = 420;

    [Export] public BuildTool? BuildTool;
    [Export] public WorldMapView? WorldMap;
    [Export] public UnitTool? UnitTool;
    [Export] public PauseMenu? PauseMenu;

    SimHost _host = null!;
    ContentDb Content => _host.Content;
    Simulation Sim => _host.Sim;
    PlayerState? Me => Sim.PlayerOrNull(_host.LocalPlayer);

    Label _title = null!, _clock = null!, _stats = null!;
    VBoxContainer _sideInfo = null!;
    Button _claim = null!, _enter = null!;
    Label _goalStep = null!, _goalName = null!, _goalText = null!, _goalCount = null!, _techLine = null!;
    ProgressBar _goalBar = null!;
    VBoxContainer _toasts = null!;
    Control _buildArea = null!;
    readonly List<Button> _toolButtons = [];
    PanelContainer _placePanel = null!;
    Label _placeLine = null!;
    PanelContainer _hoverPanel = null!;
    VBoxContainer _hover = null!;
    Label _hints = null!;
    Control _help = null!, _tech = null!, _complete = null!;
    VBoxContainer _techList = null!;

    int _seenUnlocks, _seenGoals;
    Control? _welcome;
    bool _showStats;
    double _slowTimer;
    int _shownTileId = -1;
    string _sideKey = "";

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        BuildLeft();
        BuildRight();
        BuildBottom();
        BuildOverlays();

        _seenUnlocks = Me?.UnlockLog.Count ?? 0;
        _seenGoals = Me?.GoalsDone ?? 0;
        _host.ViewChanged += OnViewChanged;
        OnViewChanged();
        RefreshSlow();

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

    public override void _ExitTree() => _host.ViewChanged -= OnViewChanged;

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
        _claim = Ui.Button("Claim (C)", ClaimSelected);
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
        _techLine = Ui.Label("", Ui.SmallFont, Ui.Muted, RightWidth - 24);
        goal.AddChild(_goalStep);
        goal.AddChild(_goalName);
        goal.AddChild(_goalText);
        goal.AddChild(bar);
        goal.AddChild(_techLine);
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

        var bar = Ui.Row(6);
        if (BuildTool != null)
        {
            for (int i = 0; i < BuildTool.Tools.Count; i++)
            {
                var tool = BuildTool.Tools[i];
                int index = i;
                var b = new Button
                {
                    Text = i < InputActions.Slots ? ((i + 1) % 10).ToString() : "",
                    Icon = tool.IconId == null ? null : Sprites.Get(tool.IconId),
                    ExpandIcon = true,
                    IconAlignment = HorizontalAlignment.Center,
                    VerticalIconAlignment = VerticalAlignment.Top,
                    ToggleMode = true,
                    FocusMode = FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(66, 74),
                };
                b.AddThemeFontSizeOverride("font_size", Ui.SmallFont);
                b.AddThemeConstantOverride("icon_max_width", 40);
                b.Pressed += () => BuildTool.Select(index);
                _toolButtons.Add(b);
                bar.AddChild(b);
            }
        }
        var barPanel = Ui.Panel(bar);
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
        buttons.AddChild(Ui.Button("Help  F1", () => Toggle(_help)));
        buttons.AddChild(Ui.Button("Tech  T", () => Toggle(_tech)));
        buttons.AddChild(Ui.Button("Menu  Esc", () => PauseMenu?.Open()));
        right.AddChild(buttons);
        AddChild(right);
    }

    void BuildOverlays()
    {
        _help = Modal("Controls", HelpText());
        _techList = Ui.Column(10);
        _tech = Modal("Tech tree", null, _techList);

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
        _help.Visible = _tech.Visible = false;
        c.Visible = show;
        if (show && c == _tech)
            RefreshTech();
    }

    static string HelpText() => string.Join("\n", [
        "World map",
        "    Click selects a tile · double-click or Enter opens one of yours · C claims the selected tile",
        "    Tab goes back to the tile you were on",
        "",
        "Tile",
        "    1-9, 0 or the build bar picks a building · R rotates · Esc drops it",
        "    Left-click / drag places · right-click / drag removes (full refund)",
        "    Drills sit on ore. Belts carry items the way they face; machines push output into anything next to them.",
        "    Ports sit in the shaded strip by the edge they face. An export port feeds the import port in the same",
        "    lane on the neighbouring tile's facing edge.",
        "",
        "Units",
        "    Lay frame (0) under a unit core and thrusters, hover the core with no building picked and press G",
        "    Left-click / drag selects units · right-click moves them · past the edge goes to the next tile",
        "    G with units selected lands them",
        "",
        "Camera: WASD or arrows pan · wheel zooms · middle-drag pans",
        "Debug: F2 performance · F3 stress test · F4 unlock everything and add 1000 of each item",
    ]);

    // ---- Per frame ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(InputActions.Help))
            Toggle(_help);
        else if (e.IsActionPressed(InputActions.TechTree))
            Toggle(_tech);
        else if (e.IsActionPressed(InputActions.DebugStats))
            _stats.Visible = _showStats = !_showStats;
        else if (e.IsActionPressed(InputActions.Cancel) && (_help.Visible || _tech.Visible))
            _help.Visible = _tech.Visible = false;
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

        // A new tile picked on the world map shows at once rather than at the next slow refresh.
        int selected = _host.View == ViewMode.World ? WorldMap?.SelectedTileId ?? -1 : -1;
        _slowTimer -= delta;
        if (_slowTimer <= 0 || selected != _shownTileId)
        {
            _shownTileId = selected;
            _slowTimer = SlowRefresh;
            RefreshSlow();
        }
    }

    void RefreshSlow()
    {
        RefreshGoal();
        if (_host.View == ViewMode.Tile)
        {
            var t = _host.ViewedTile;
            _title.Text = $"Tile ({t.X},{t.Y})";
            RefreshTileSide();
            RefreshHover();
            _hints.Text = BuildTool is { HasTool: true }
                ? "R rotate · drag to place a line · right-click removes · Esc drops the building"
                : "Tab world map · 1-9 pick a building · drag to select units";
        }
        else
        {
            _title.Text = "World map";
            RefreshWorldSide();
            _hoverPanel.Visible = UnitTool is { } u && _host.Selected.Count > 0 && SetHover(u.Status);
            _hints.Text = "Double-click your tile to open it · C claims · Tab back";
        }
        if (_tech.Visible)
            RefreshTech();
    }

    // ---- Objective, unlocks, toasts ----

    void RefreshGoal()
    {
        var goals = Content.Goals;
        var goal = Progression.CurrentGoal(Sim, _host.LocalPlayer);
        int done = Me?.GoalsDone ?? 0;
        if (goal == null)
        {
            _goalStep.Text = "OBJECTIVES COMPLETE";
            _goalName.Text = "Demo complete";
            _goalText.Text = "Every objective is done. Keep expanding, or save from the menu.";
            _goalBar.Value = _goalBar.MaxValue;
            _goalCount.Text = "";
        }
        else
        {
            long have = Progression.Progress(Sim, _host.LocalPlayer, goal.Condition);
            _goalStep.Text = $"OBJECTIVE {done + 1} OF {goals.Count}";
            _goalName.Text = goal.Name;
            _goalText.Text = goal.Description;
            _goalBar.MaxValue = goal.Condition.Amount;
            _goalBar.Value = System.Math.Min(have, goal.Condition.Amount);
            _goalCount.Text = $"{System.Math.Min(have, goal.Condition.Amount)} / {goal.Condition.Amount} {ConditionUnit(goal.Condition)}";
        }

        var next = Content.TechList.FirstOrDefault(t => Me?.HasTech(t.Id) != true);
        _techLine.Text = next == null
            ? "All tech unlocked."
            : $"Next unlock: {next.Name} ({ConditionText(next.Condition)}) → {UnlockNames(next)}";
    }

    void CheckProgress()
    {
        if (Me is not { } me)
            return;
        while (_seenUnlocks < me.UnlockLog.Count)
        {
            var tech = Content.Tech[me.UnlockLog[_seenUnlocks++].TechId];
            Toast($"Unlocked {tech.Name}: {UnlockNames(tech)}", Ui.Good, 8, tech.Unlocks.FirstOrDefault(id => Content.Buildings.ContainsKey(id)));
        }
        while (_seenGoals < me.GoalsDone)
        {
            var goal = Content.Goals[_seenGoals++];
            Toast($"Objective complete: {goal.Name}", Ui.Accent, 6);
            if (_seenGoals == Content.Goals.Count)
                _complete.Visible = true;
            RefreshGoal();
        }
    }

    public Control Toast(string text, Color color, double seconds = 5, string? iconId = null)
    {
        var row = Ui.Row(10);
        if (iconId != null)
            row.AddChild(Ui.Icon(iconId, 32));
        row.AddChild(Ui.Label(text, Ui.FontSize, color, RightWidth - 70));
        var panel = Ui.Panel(row);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        _toasts.AddChild(panel);
        while (_toasts.GetChildCount() > 4)
            _toasts.GetChild(0).Free();
        var tween = panel.CreateTween();
        tween.TweenInterval(seconds);
        tween.TweenProperty(panel, "modulate:a", 0.0, 0.6);
        tween.TweenCallback(Callable.From(panel.QueueFree));
        return panel;
    }

    void SaveNow()
    {
        if (_host.Save() is { } path)
            Toast($"Saved ({path.GetFile()})", Ui.Good);
        else
            Toast("Couldn't save the game", Ui.Bad);
    }

    string ConditionText(UnlockCondition c)
    {
        long have = System.Math.Min(Progression.Progress(Sim, _host.LocalPlayer, c), c.Amount);
        return $"{have}/{c.Amount} {ConditionUnit(c)}";
    }

    string ConditionUnit(UnlockCondition c) => c.Type switch
    {
        "produce" => Ui.ItemName(Content, c.Item!),
        "claim" => "tiles owned",
        "ship" => "items shipped",
        "launch" => c.Amount == 1 ? "unit launched" : "units launched",
        _ => c.Type,
    };

    string UnlockNames(TechDef tech) => string.Join(", ", tech.Unlocks
        .Where(Content.Buildings.ContainsKey)
        .Select(id => Content.Buildings[id].Name));

    void RefreshTech()
    {
        Ui.Clear(_techList);
        foreach (var tech in Content.TechList.OrderBy(t => Me?.HasTech(t.Id) == true ? 0 : 1))
        {
            bool have = Me?.HasTech(tech.Id) == true;
            var col = Ui.Column(4);
            var head = Ui.Row();
            head.AddChild(Ui.Label(have ? "✔" : "○", 20, have ? Ui.Good : Ui.Muted));
            head.AddChild(Ui.Label(tech.Name, 20, have ? Ui.Good : Ui.Text));
            col.AddChild(head);
            if (!have)
            {
                long n = System.Math.Min(Progression.Progress(Sim, _host.LocalPlayer, tech.Condition), tech.Condition.Amount);
                var bar = Ui.Row();
                bar.AddChild(new ProgressBar
                {
                    ShowPercentage = false, MaxValue = tech.Condition.Amount, Value = n,
                    CustomMinimumSize = new Vector2(300, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                });
                bar.AddChild(Ui.Label(ConditionText(tech.Condition), Ui.SmallFont, Ui.Muted));
                col.AddChild(bar);
            }
            var unlocks = Ui.Row(6);
            unlocks.AddChild(Ui.Label("Unlocks", Ui.SmallFont, Ui.Muted));
            foreach (var id in tech.Unlocks)
            {
                if (Content.Buildings.TryGetValue(id, out var def))
                {
                    unlocks.AddChild(Ui.Icon(id, 24));
                    unlocks.AddChild(Ui.Label(def.Name, Ui.SmallFont));
                }
                else if (Content.Recipes.TryGetValue(id, out var r))
                {
                    unlocks.AddChild(Ui.Label($"recipe: {Ui.ItemName(Content, r.Outputs.Keys.First())}", Ui.SmallFont));
                }
            }
            col.AddChild(unlocks);
            _techList.AddChild(col);
        }
        _techList.AddChild(Ui.Label("Tech unlocks by making things, not by spending points. Unlocks apply on every tile you own.",
            Ui.SmallFont, Ui.Muted, 600));
    }

    // ---- Left panel ----

    TileState SelectedWorldTile() => Sim.TryGetTile(WorldMap?.SelectedTileId ?? _host.ViewedTile.Id) ?? _host.ViewedTile;

    void ClaimSelected() => Sim.Enqueue(new ClaimTileCommand(_host.LocalPlayer, SelectedWorldTile().Id));

    void RefreshTileSide()
    {
        var tile = _host.ViewedTile;
        string key = "t" + tile.Id + string.Join(",", tile.Inventory.Entries.Select(kv => kv.Key + kv.Value));
        if (key == _sideKey)
            return;
        _sideKey = key;
        Ui.Clear(_sideInfo);
        _sideInfo.AddChild(Ui.Heading($"Inventory · {Buildings(tile)}"));
        AddInventory(_sideInfo, tile);
    }

    static string Buildings(TileState tile) =>
        tile.Grid.BuildingCount == 1 ? "1 building" : $"{tile.Grid.BuildingCount} buildings";

    void AddInventory(VBoxContainer into, TileState tile)
    {
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 24);
        foreach (var (id, n) in tile.Inventory.Entries)
        {
            var name = Ui.Row(6);
            name.AddChild(Ui.Icon(id, 24));
            name.AddChild(Ui.Label(Ui.ItemName(Content, id)));
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(name);
            var count = Ui.Label(n.ToString("N0"));
            count.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(count);
        }
        if (grid.GetChildCount() == 0)
            into.AddChild(Ui.Label("Empty. Belt items into the core to stock it.", Ui.SmallFont, Ui.Muted));
        else
            into.AddChild(grid);
    }

    void RefreshWorldSide()
    {
        var tile = SelectedWorldTile();
        var claim = Expansion.Check(Sim, _host.LocalPlayer, tile.Id, out var payer);
        bool mine = tile.Owner == _host.LocalPlayer;
        _claim.Visible = tile.Owner == TileState.Unowned;
        _claim.Disabled = claim != ClaimResult.Ok;
        _enter.Visible = mine;

        string key = $"w{tile.Id}/{claim}/{tile.Owner}/{tile.Grid.BuildingCount}/" +
                     (mine ? string.Join(",", tile.Inventory.Entries.Select(kv => kv.Key + kv.Value)) : "");
        if (key == _sideKey)
            return;
        _sideKey = key;
        Ui.Clear(_sideInfo);

        string owner = tile.Ownership switch
        {
            TileOwnership.Contested => "Contested",
            TileOwnership.Unclaimed => "Unclaimed",
            _ when mine => "Yours",
            _ => $"Player {tile.Owner}",
        };
        var head = Ui.Row();
        var name = Ui.Label($"Tile ({tile.X},{tile.Y})", 20);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        head.AddChild(Ui.Label(owner, Ui.FontSize, mine ? Ui.Good : tile.Owner == TileState.Unowned ? Ui.Muted : Ui.Bad));
        _sideInfo.AddChild(head);

        _sideInfo.AddChild(Ui.Heading("Deposits"));
        foreach (var (item, cells) in tile.Grid.DepositCounts())
        {
            var row = Ui.Row(6);
            row.AddChild(Ui.Icon(item, 24));
            var label = Ui.Label(Ui.ItemName(Content, item));
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            if (Content.Items[item].Regional)
                row.AddChild(Ui.Label("only on some tiles", Ui.SmallFont, Ui.Accent));
            row.AddChild(Ui.Label($"{cells} cells", Ui.SmallFont, Ui.Muted));
            _sideInfo.AddChild(row);
        }
        var regionalHere = tile.Grid.DepositCounts().Keys.Where(i => Content.Items[i].Regional).ToHashSet();
        foreach (var item in Content.Items.Values.Where(i => i.Regional && !regionalHere.Contains(i.Id)))
            _sideInfo.AddChild(Ui.Label($"No {item.Name.ToLowerInvariant()} here.", Ui.SmallFont, Ui.Muted));

        if (mine)
        {
            _sideInfo.AddChild(Ui.Heading($"Inventory · {Buildings(tile)}"));
            AddInventory(_sideInfo, tile);
            AddPorts(tile);
        }
        else if (tile.Owner == TileState.Unowned)
        {
            _sideInfo.AddChild(Ui.Heading("Claim"));
            var cost = Ui.Row(6);
            cost.AddChild(Ui.Label("Cost", Ui.SmallFont, Ui.Muted));
            cost.AddChild(Ui.Items(Simulation.ClaimCost));
            _sideInfo.AddChild(cost);
            _sideInfo.AddChild(Ui.Label(claim switch
            {
                ClaimResult.Ok => $"Paid from tile ({payer!.X},{payer.Y}).",
                ClaimResult.CannotAfford => "A neighbouring tile of yours needs this much in its core.",
                ClaimResult.NotAdjacent => "Only tiles next to one of yours can be claimed.",
                ClaimResult.Contested => "Contested tiles can't be claimed.",
                _ => "",
            }, Ui.SmallFont, claim == ClaimResult.Ok ? Ui.Good : Ui.Warn, SideWidth - 24));
        }
    }

    void AddPorts(TileState tile)
    {
        var lines = new List<string>();
        foreach (var side in Directions.All)
        {
            int exports = tile.Grid.ExportPorts.Count(p => PortRules.SideOf(p.Rotation) == side);
            int imports = tile.Grid.ImportPorts.Count(p => PortRules.SideOf(p.Rotation) == side);
            if (exports + imports == 0)
                continue;
            string to = Sim.Neighbor(tile, side) is { } n ? $"tile ({n.X},{n.Y})" : "the world's edge";
            lines.Add($"{side} to {to}: {exports} out, {imports} in");
        }
        if (lines.Count == 0)
            return;
        _sideInfo.AddChild(Ui.Heading("Ports"));
        _sideInfo.AddChild(Ui.Label(string.Join("\n", lines), Ui.SmallFont, Ui.Text));
    }

    // ---- Build bar ----

    void UpdateBuildBar()
    {
        if (BuildTool == null || _host.View != ViewMode.Tile)
            return;
        var tile = _host.ViewedTile;
        for (int i = 0; i < _toolButtons.Count; i++)
        {
            var tool = BuildTool.Tools[i];
            var b = _toolButtons[i];
            b.SetPressedNoSignal(i == BuildTool.SelectedIndex);
            bool locked = !tool.IsFrame && !IsToolUnlocked(tool);
            bool afford = tile.Inventory.Has(ToolCost(tool));
            b.Modulate = locked ? new Color(1, 1, 1, 0.3f) : afford ? Colors.White : new Color(1f, 0.7f, 0.7f, 0.75f);
            b.TooltipText = ToolTooltip(tool, locked);
        }

        if (BuildTool.Selected is not { } picked)
        {
            _placePanel.Visible = false;
            return;
        }
        _placePanel.Visible = true;
        string facing = BuildTool.SelectedIsPortTool()
            ? $"faces {PortRules.SideOf(BuildTool.Rotation)}"
            : $"facing {"→↓←↑"[BuildTool.Rotation]}";
        var result = BuildTool.Preview;
        _placeLine.Text = $"{picked.Label} · {facing} · {PlaceMessage(picked, result)}";
        _placeLine.AddThemeColorOverride("font_color", result == PlaceResult.Ok ? Ui.Text : Ui.Bad);
    }

    bool IsToolUnlocked(BuildTool.Tool tool) =>
        Progression.IsUnlocked(Sim, _host.LocalPlayer, tool.BuildingId)
        && (tool.RecipeId == null || Progression.IsUnlocked(Sim, _host.LocalPlayer, tool.RecipeId));

    IReadOnlyDictionary<string, int> ToolCost(BuildTool.Tool tool) =>
        tool.IsFrame ? UnitAssembly.FrameCost : Content.Buildings[tool.BuildingId].Cost;

    string ToolTooltip(BuildTool.Tool tool, bool locked)
    {
        var sb = new StringBuilder(tool.Label).Append('\n');
        sb.Append("Cost: ").Append(Ui.Cost(Content, ToolCost(tool)));
        if (tool.RecipeId is { } rid)
            sb.Append('\n').Append(RecipeText(Content.Recipes[rid]));
        else if (!tool.IsFrame && Content.RecipesByBuilding.GetValueOrDefault(tool.BuildingId) is { Count: > 0 } rs && rs.All(r => r.Inputs.Count == 0))
            sb.Append("\nMines whatever deposit it sits on");
        if (locked && LockingTech(tool) is { } tech)
            sb.Append($"\nLocked: unlocks with {tech.Name} ({ConditionText(tech.Condition)})");
        return sb.ToString();
    }

    TechDef? LockingTech(BuildTool.Tool tool) =>
        (Progression.IsUnlocked(Sim, _host.LocalPlayer, tool.BuildingId) ? null : Progression.UnlockingTech(Content, tool.BuildingId))
        ?? (tool.RecipeId == null ? null : Progression.UnlockingTech(Content, tool.RecipeId));

    string RecipeText(RecipeDef r)
    {
        string Items(Dictionary<string, int> items) => string.Join(" + ", items.Select(kv => $"{kv.Value} {Ui.ItemName(Content, kv.Key)}"));
        string time = $"{r.Ticks / (double)Simulation.TicksPerSecond:0.#} s";
        return r.Inputs.Count == 0 ? $"Mines {Items(r.Outputs)} every {time}" : $"{Items(r.Inputs)} → {Items(r.Outputs)} in {time}";
    }

    string PlaceMessage(BuildTool.Tool tool, PlaceResult result)
    {
        var tile = _host.ViewedTile;
        switch (result)
        {
            case PlaceResult.Ok:
                return "click or drag to place";
            case PlaceResult.CannotAfford:
                var missing = ToolCost(tool).Where(kv => tile.Inventory.Get(kv.Key) < kv.Value)
                    .Select(kv => $"{kv.Value - tile.Inventory.Get(kv.Key)} more {Ui.ItemName(Content, kv.Key)}");
                return "needs " + string.Join(", ", missing);
            case PlaceResult.Locked:
                return LockingTech(tool) is { } tech ? $"locked until {tech.Name} ({ConditionText(tech.Condition)})" : "locked";
            case PlaceResult.NeedsDeposit:
                return "must sit on an ore deposit";
            case PlaceResult.Blocked:
                return tool.IsFrame ? "already framed" : "something is in the way";
            case PlaceResult.OutOfBounds:
                return "off the edge of the tile";
            case PlaceResult.NotInPortZone:
                return $"ports go within {PortRules.ZoneDepth} cells of the edge they face";
            case PlaceResult.NotOwner:
                return "not your tile";
            default:
                return result.ToString();
        }
    }

    // ---- Hover ----

    void RefreshHover()
    {
        if (BuildTool == null)
            return;
        var tile = _host.ViewedTile;
        var cell = BuildTool.HoveredCell();
        bool show = GetViewport().GuiGetHoveredControl() == null && tile.Grid.InBounds(cell.X, cell.Y) && (
            tile.Grid.BuildingAt(cell.X, cell.Y) is { } b ? DescribeBuilding(tile, b)
            : tile.Grid.DepositAt(cell.X, cell.Y) is { } ore ? DescribeDeposit(ore)
            : false);
        if (!show && !BuildTool.HasTool && UnitTool is { } u && (_host.Selected.Count > 0 || u.Status.Contains('\n')))
            show = SetHover(u.Status);
        _hoverPanel.Visible = show;
    }

    bool SetHover(string text)
    {
        Ui.Clear(_hover);
        _hover.AddChild(Ui.Label(text, Ui.SmallFont, Ui.Text, SideWidth - 24));
        return true;
    }

    bool DescribeDeposit(string item)
    {
        Ui.Clear(_hover);
        var row = Ui.Row(8);
        row.AddChild(Ui.Icon(item, 28));
        row.AddChild(Ui.Label($"{Ui.ItemName(Content, item)} deposit", 20));
        _hover.AddChild(row);
        _hover.AddChild(Ui.Label("A drill placed on it mines it.", Ui.SmallFont, Ui.Muted));
        return true;
    }

    bool DescribeBuilding(TileState tile, PlacedBuilding b)
    {
        Ui.Clear(_hover);
        var def = Content.Buildings[b.DefId];
        var head = Ui.Row(8);
        head.AddChild(Ui.Icon(b.DefId, 28));
        head.AddChild(Ui.Label(def.Name, 20));
        _hover.AddChild(head);

        void Line(string text, Color? color = null) => _hover.AddChild(Ui.Label(text, Ui.SmallFont, color ?? Ui.Muted, SideWidth - 24));

        switch (b.Kind)
        {
            case BuildingKind.Core:
                Line("The tile's core. Anything delivered here goes into the tile inventory, which pays for building.");
                break;
            case BuildingKind.Conveyor:
                Line($"Carrying {b.Belt!.Count} of {BeltState.Capacity} items, heading {Directions.FromRotation(b.Rotation)}.");
                break;
            case BuildingKind.ExportPort:
                Line(ExportStatus(tile, b), ExportOk(tile, b) ? Ui.Good : Ui.Warn);
                Line($"{b.Export!.InFlight.Count} items in transit.");
                break;
            case BuildingKind.ImportPort:
                var from = Sim.Neighbor(tile, PortRules.SideOf(b.Rotation));
                Line(from == null ? "Faces the edge of the world: nothing can arrive." : $"Receives from tile ({from.X},{from.Y}), lane {PortRules.Lane(b)}.");
                Line($"Holding {b.Machine!.OutputCount} items for the belt next to it.");
                break;
            case BuildingKind.UnitCore:
            case BuildingKind.Thruster:
                Line("A unit part. Lay frame under it, then hover the unit core with no building picked and press G to lift off.");
                break;
            case BuildingKind.Machine when b.Machine is { Recipe: { } recipe } m:
                Line(RecipeText(recipe), Ui.Text);
                var status = m.Status switch
                {
                    MachineStatus.Working => ($"Working · {m.Progress * 100 / recipe.Ticks}%", Ui.Good),
                    MachineStatus.Starved => ($"Waiting for {Ui.ItemName(Content, m.MissingInput ?? recipe.Inputs.Keys.First())}", Ui.Warn),
                    MachineStatus.OutputFull => ($"Output full: put a belt or building next to it that takes {Ui.ItemName(Content, recipe.Outputs.Keys.First())}", Ui.Bad),
                    _ => ("Idle", Ui.Muted),
                };
                Line(status.Item1, status.Item2);
                if (m.Input.Count > 0)
                    AddBuffer("In", m.Input);
                if (m.Output.Count > 0)
                    AddBuffer("Out", m.Output);
                break;
        }
        return true;
    }

    void AddBuffer(string label, IReadOnlyDictionary<string, int> items)
    {
        var row = Ui.Row(6);
        row.AddChild(Ui.Label(label, Ui.SmallFont, Ui.Muted));
        row.AddChild(Ui.Items(items));
        _hover.AddChild(row);
    }

    bool ExportOk(TileState tile, PlacedBuilding port) => ExportProblem(tile, port) == null;

    string? ExportProblem(TileState tile, PlacedBuilding port)
    {
        var side = PortRules.SideOf(port.Rotation);
        if (Sim.Neighbor(tile, side) is not { } n)
            return "Faces the edge of the world: nowhere to send items.";
        if (n.Owner != tile.Owner)
            return $"Tile ({n.X},{n.Y}) isn't yours. Claim it to send items there.";
        int lane = PortRules.Lane(port);
        if (!n.Grid.ImportPorts.Any(p => PortRules.SideOf(p.Rotation) == side.Opposite() && PortRules.Lane(p) == lane))
            return $"No import port on tile ({n.X},{n.Y})'s {side.Opposite()} edge in lane {lane}. Items wait here until there is.";
        return null;
    }

    string ExportStatus(TileState tile, PlacedBuilding port)
    {
        if (ExportProblem(tile, port) is { } problem)
            return problem;
        var n = Sim.Neighbor(tile, PortRules.SideOf(port.Rotation))!;
        return $"Linked to tile ({n.X},{n.Y}), lane {PortRules.Lane(port)}.";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Game.Screens;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Debug;

/// <summary>
/// Debug (`-- --tour out_dir`): clicks through the menus and HUD the way a player would (title, new game, world map,
/// tile, pause, save, quit, continue), logs what happened and saves a screenshot per step, so a UI change
/// can be checked without someone at the keyboard. Needs a window.
/// </summary>
public partial class UiTour : Node
{
    SimHost _host = null!;
    string _dir = "";
    readonly List<(int Frames, string Name, Action Do)> _steps = [];
    int _step, _wait = 30;
    long _pausedTick, _untilTick = -1;

    // Where Build put the smelter. Tiles are irregular, so it goes on the first open ground near the middle.
    (int X, int Y) _smelter = (44, 40);
    HashSet<string> _savesBefore = [];
    string? _blueprintId;

    public static void Attach(SimHost host, string dir)
    {
        Directory.CreateDirectory(dir);
        var tour = new UiTour { _host = host, _dir = Path.GetFullPath(dir), ProcessMode = ProcessModeEnum.Always };
        tour.Plan();
        host.AddChild(tour);
    }

    void Plan()
    {
        Step(0, "title", () =>
        {
            _savesBefore = SaveSlots.List().Select(s => s.Path).ToHashSet();
            Shot("01_title");
        });
        Step(1, "multiplayer", () => Press("Multiplayer"));
        Step(10, "multiplayer page", () => { Shot("01b_multiplayer"); Press("Back"); });
        Step(5, "new game", () => Press("New game"));
        // WM-017, MP-008: pick a scenario and a head start for its AI seat, then back to a generated map for the tour.
        Step(10, "pick scenario", () => PickScenario("ai_head_start"));
        Step(5, "pick head start", () => PickHeadStart(0, "stockpile"));
        Step(5, "scenario setup shot", () =>
        {
            var setup = Form().Read();
            Log(setup is { Scenario: "ai_head_start", AiPlayers: 1 } && setup.HeadStartFor(1) == "stockpile"
                ? "new game form: scenario with one AI seat on the Stockpile head start"
                : $"FAIL: the form reads {setup}");
            Shot("02a_new_game_scenario");
            PickScenario(null);
        });
        Step(5, "generated again", () =>
        {
            Log(Form().Read() is { Scenario: null, AiPlayers: 0 } ? "back to a generated map" : "FAIL: the map pick didn't reset");
        });
        Step(10, "setup", () => { Shot("02_new_game"); Press("Start"); });
        Step(40, "world", () => Shot("03_world"));
        // WM-007: the wheel over the home circle zooms through the cross-fade into the tile.
        Step(1, "zoom to fade", () => WheelUntil(c => c.CellScale >= c.Threshold * 0.8f));
        Step(5, "fade shot", () =>
        {
            Log(_host.View == ViewMode.World && _host.TileOpacity is > 0 and < 1
                ? $"world map side of the fade, tile opacity {_host.TileOpacity:0.00}"
                : $"FAIL: expected the fade on the world map side, got {_host.View} at opacity {_host.TileOpacity:0.00}");
            Shot("03b_zoom_fade");
            WheelUntil(c => c.CellScale >= c.Threshold * 1.1f);
        });
        Step(5, "tile side shot", () =>
        {
            Log(_host.View == ViewMode.Tile && _host.ViewedTile.Owner == _host.LocalPlayer
                ? $"wheel opened the home tile, tile opacity {_host.TileOpacity:0.00}"
                : $"FAIL: the wheel didn't open the home tile ({_host.View})");
            Shot("03c_zoom_tile_side");
            WheelUntil(c => c.CellScale >= 12);
        });
        Step(20, "tile", () => Shot("04_tile"));
        Step(1, "build", () => { Build(); WaitTicks(14 * 60); });
        Step(1, "built", () => Shot("05_building"));
        Step(1, "hover machine", HoverSmelter);
        Step(10, "hover shot", () => Shot("05b_hover"));
        Step(1, "move mode", CutSmelter);
        Step(3, "cut", AimPaste);
        Step(5, "move ghost shot", () => { Shot("05b2_move_ghost"); Mouse(true); });
        Step(3, "paste", () => Mouse(false));
        Step(10, "pasted", CheckPasted);
        Step(1, "port zones", ShowPortZones);
        Step(10, "port zones shot", () => { Shot("05c_port_zones"); Key(Godot.Key.Escape); });
        Step(1, "combat", ShowCombat);
        Step(10, "combat shot", () => Shot("05d_combat"));
        Step(5, "blueprint library", () => { RecordBlueprint(); Press("Blueprints"); });
        Step(10, "library shot", () => { Shot("05d_blueprints"); Press("Place"); HoverBelow(); });
        Step(10, "ghost shot", () => { Shot("05e_blueprint_ghost"); Key(Godot.Key.Escape); });
        Step(5, "production stats", () => Key(Godot.Key.O));
        Step(10, "production stats shot", () =>
        {
            var panel = Find(GetTree().Root, "Close  O");
            Log(panel != null ? "production stats open" : "FAIL: O didn't open the production stats");
            Shot("05d_production");
            Key(Godot.Key.O);
        });
        Step(5, "world again", () => { Key(Godot.Key.Tab); });
        Step(14, "zooming out", () => Shot("06_zoom_out"));
        Step(40, "select coal tile", () =>
        {
            Log(_host.View == ViewMode.World ? "Tab zoomed out to the world map" : "FAIL: Tab didn't reach the world map");
            SelectCoalNeighbour();
        });
        Step(1, "lift off scout", LiftOffScout);
        Step(10, "select scout", SelectScout);
        Step(10, "world tile panel", () => { Shot("07_world_coal"); Press("Send units"); });
        Step(30, "scout sent", () =>
        {
            // WM-006: the scout flies to the coal tile's victory circle; landing there and holding it claims the tile.
            var scout = _host.Sim.Units.LastOrDefault(u => u.Owner == _host.LocalPlayer); // the skiff (WM-005) came first
            Log(scout is { Moving: true } ? "scout on its way to the coal tile's circle" : "FAIL: the scout didn't take the order");
            Shot("07b_scout_sent");
        });
        Step(1, "logistics overlay", () => Key(Godot.Key.L));
        Step(5, "logistics overlay off", () =>
        {
            var map = GetTree().Root.FindChild("WorldMapView", true, false) as WorldMapView ?? throw new InvalidOperationException("no WorldMapView");
            Log(map.ShowLogistics ? "FAIL: L didn't hide the logistics overlay" : "logistics overlay toggles");
            Shot("07c_logistics_off");
            Key(Godot.Key.L);
        });
        Step(1, "back to tile", () => Key(Godot.Key.Tab));
        Step(40, "back on tile", () =>
        {
            // WM-007: the selection survives the zoom.
            Log(_host.View == ViewMode.Tile && _host.Selected.Count > 0
                ? "Tab zoomed back into the tile with the scout still selected"
                : $"FAIL: after Tab: {_host.View}, {_host.Selected.Count} selected");
            Shot("07d_back_on_tile");
            _host.Selected.Clear();
        });
        Step(5, "pause", () => { Key(Godot.Key.Escape); });
        Step(5, "paused", () => { _pausedTick = _host.Sim.Tick; Shot("08_pause"); });
        Step(20, "still paused", () =>
        {
            Log(_host.Sim.Tick == _pausedTick ? "pause holds the sim" : $"FAIL: sim ran while paused ({_pausedTick} -> {_host.Sim.Tick})");
            Press("Save game");
        });
        Step(5, "saved", () => { Shot("09_saved"); Log($"saved at tick {_host.Sim.Tick}, hash {_host.Sim.StateHash():X}"); Press("Quit to title"); });
        Step(30, "title again", () => { Shot("10_title_continue"); Press("Continue"); });
        Step(2, "loading", () => Shot("11_loading"));
        Step(60, "loaded", () =>
        {
            Log(_host.HasGame ? $"loaded: tick {_host.Sim.Tick}, {_host.Sim.Tiles.Count(t => t.Owner == 0)} tiles, " +
                                $"{_host.ViewedTile.Grid.BuildingCount} buildings on home" : "FAIL: no game after Continue");
            Shot("12_loaded");
        });
        // WM-017: save the loaded match as a scenario from the Esc menu, then start the shipped scenario with an AI head start.
        Step(1, "scenario pause", () => Key(Godot.Key.Escape));
        Step(5, "save scenario", () =>
        {
            _scenariosBefore = _host.Content.Scenarios.Keys.ToHashSet();
            Press("Save as scenario");
        });
        Step(5, "scenario saved", () =>
        {
            var added = _host.Content.Scenarios.Keys.Where(k => !_scenariosBefore.Contains(k)).ToList();
            Log(added.Count == 1 ? $"saved scenario {added[0]}" : "FAIL: Save as scenario added nothing");
            _savedScenario = added.FirstOrDefault();
            Shot("13_scenario_saved");
            Press("Quit to title");
        });
        Step(30, "scenario new game", () => Press("New game"));
        Step(10, "scenario picks", () =>
        {
            Log(_savedScenario != null
                && Form().Root.FindChild(MatchSettings.MapPickName, true, false) is OptionButton map
                && Enumerable.Range(0, map.ItemCount).Any(i => map.GetItemText(i).Contains(_host.Content.Scenarios[_savedScenario].Name))
                ? "the saved scenario is in the map list" : "FAIL: the saved scenario isn't offered");
            PickScenario("ai_head_start");
        });
        Step(5, "scenario head start", () => PickHeadStart(0, "second_scout"));
        Step(5, "scenario start", () => Press("Start"));
        Step(40, "scenario match", () =>
        {
            // The AI's cores, standing or lifted off: the scenario's plus the second scout's.
            var sim = _host.Sim;
            var def = _host.Content.Scenarios["ai_head_start"];
            int expected = def.Seats[1].Buildings!.Count(b => _host.Content.Buildings[b.DefId].Kind == Sim.Content.BuildingKind.Core)
                           + (def.Seats[1].Units?.Count ?? 0) + 1;
            int aiCores = sim.CoreCount(1);
            Log(sim.Setup is { Scenario: "ai_head_start" } && aiCores >= expected
                ? $"scenario match started: {sim.Tiles.Count} tiles, the AI has {aiCores} cores with its head start"
                : $"FAIL: scenario match: {sim.Setup}, AI cores {aiCores}, expected {expected}");
            Shot("14_scenario_world");
        });
        Step(1, "scenario cleanup", () =>
        {
            if (_savedScenario != null)
                DirAccess.RemoveAbsolute($"{SimHost.ScenarioDir}/{_savedScenario}.json");
        });
        Step(1, "done", () =>
        {
            // Leave the player's saves folder as it was.
            foreach (var save in SaveSlots.List().Where(s => !_savesBefore.Contains(s.Path)))
                SaveSlots.Delete(save.Path);
            if (_blueprintId != null)
                BlueprintLibrary.Delete(_blueprintId);
            GetTree().Quit();
        });
    }

    void Step(int frames, string name, Action action) => _steps.Add((frames, name, action));

    HashSet<string> _scenariosBefore = [];
    string? _savedScenario;

    /// <summary>The new-game form on screen.</summary>
    MatchSettings Form() => MatchSettings.Shown ?? throw new InvalidOperationException("no match settings on screen");

    /// <summary>Picks a scenario (by id; null for a generated map) in the new-game form's map list, as a click would.</summary>
    void PickScenario(string? id)
    {
        var map = Form().Root.FindChild(MatchSettings.MapPickName, true, false) as OptionButton ?? throw new InvalidOperationException("no map pick");
        int index = id == null ? 0 : Enumerable.Range(1, map.ItemCount - 1)
            .First(i => map.GetItemText(i).Contains(_host.Content.Scenarios[id].Name));
        map.Select(index);
        map.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    /// <summary>Picks a head-start preset for the AI seat <paramref name="ai"/> (0 = the first AI seat).</summary>
    void PickHeadStart(int ai, string preset)
    {
        var pick = Form().Root.FindChild($"{MatchSettings.HeadStartName}{ai}", true, false) as OptionButton
                   ?? throw new InvalidOperationException($"no head-start pick for AI {ai}");
        int index = _host.Content.HeadStartList.ToList().FindIndex(p => p.Id == preset) + 1;
        pick.Select(index);
        pick.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    public override void _Process(double delta)
    {
        if (_untilTick >= 0)
        {
            if (_host.HasGame && _host.Sim.Tick < _untilTick)
                return;
            _untilTick = -1;
        }
        if (_step >= _steps.Count || --_wait > 0)
            return;
        var (_, name, action) = _steps[_step++];
        try
        {
            action();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[tour] FAIL at '{name}': {e.Message}");
            GetTree().Quit(1);
            return;
        }
        if (_step < _steps.Count)
            _wait = _steps[_step].Frames;
    }

    static void Log(string text) => GD.Print($"[tour] {text}");

    /// <summary>Holds the next step until the sim has run this many more ticks (frame rate varies, ticks don't).</summary>
    void WaitTicks(int ticks) => _untilTick = _host.Sim.Tick + ticks;

    void SelectCoalNeighbour()
    {
        var map = GetTree().Root.FindChild("WorldMapView", true, false) as WorldMapView ?? throw new InvalidOperationException("no WorldMapView");
        var home = _host.ViewedTile;
        var coal = _host.Sim.Neighbors(home).First(n => n.Grid.DepositCounts().ContainsKey("coal"));
        map.SelectedTileId = coal.Id;
        Log($"selected coal tile {coal.Id}");
    }

    /// <summary>Lifts off the starting scout, as G over its unit core would; <see cref="SelectScout"/> then clicks it.</summary>
    void LiftOffScout()
    {
        var home = _host.Sim.Tiles[_host.Sim.StartTileId];
        var core = home.Grid.Buildings.First(b => b.Kind == Sim.Content.BuildingKind.Core);
        _host.Send(new ActivateUnitCommand(_host.LocalPlayer, home.Id, core.X, core.Y));
    }

    void SelectScout()
    {
        _host.Selected.Clear();
        if (_host.Sim.Units.LastOrDefault(u => u.Owner == _host.LocalPlayer) is { } scout)
        {
            _host.Selected.Add(scout.Id);
            Log($"lifted off scout {scout.Id}");
        }
        else
        {
            Log("FAIL: the starting scout didn't lift off");
        }
    }

    /// <summary>Picks the export port, which shows the tile's I/O zones, and hovers the first zone's label.</summary>
    void ShowPortZones()
    {
        var tool = GetTree().Root.FindChild("BuildTool", true, false) as BuildTool ?? throw new InvalidOperationException("no BuildTool");
        int index = tool.Tools.ToList().FindIndex(t => t.BuildingId == "export_port");
        tool.Select(index);
        var zones = TileShapes.Zones(_host.ViewedTile);
        Log($"{zones.Count} I/O zones: {string.Join(", ", zones.Select(z => $"tile {z.Neighbor} ({z.Cells} cells)"))}");
        if (zones.Count > 0)
            GetViewport().WarpMouse(GetViewport().GetCanvasTransform() * (zones[0].Label * Palette.CellPx));
    }

    /// <summary>
    /// Combat overlays with no enemy around: a debug hit damages the smelter (health bar) and destroys a belt cell
    /// (wreck), a laser turret goes next to the smelter, and the cursor hovers the wreck.
    /// </summary>
    void ShowCombat()
    {
        var tile = _host.ViewedTile;
        int me = _host.LocalPlayer;
        _host.Send(new DebugDamageCommand(me, tile.Id, _smelter.X, _smelter.Y, 120));
        _host.Send(new DebugDamageCommand(me, tile.Id, _smelter.X - 4, _smelter.Y + 4, 1000));
        _host.Send(new DebugGrantCommand(me, tile.Id, "copper_plate", 20));
        _host.Send(new DebugGrantCommand(me, tile.Id, "steel", 5));
        _host.Send(new PlaceBuildingCommand(me, tile.Id, "laser_turret", _smelter.X + 2, _smelter.Y));
        var at = GetViewport().GetCanvasTransform() * (new Vector2(_smelter.X - 3.5f, _smelter.Y + 4.5f) * Palette.CellPx);
        GetViewport().WarpMouse(at);
        Log("damaged the smelter, wrecked a belt cell, placed a laser turret");
    }

    /// <summary>Records the smelter and its belt as a blueprint, as Capture area would (removed again at the end).</summary>
    void RecordBlueprint()
    {
        var bp = Blueprints.FromArea(_host.ViewedTile, _smelter.X - 4, _smelter.Y, 7, 5)
                 ?? throw new InvalidOperationException("nothing to record around the smelter");
        _blueprintId = BlueprintLibrary.Add("Tour blueprint", bp, ["tour"]).Id;
        Log($"recorded a blueprint of {bp.Blocks.Length} buildings");
    }

    /// <summary>Moves the mouse below the smelter, where a picked blueprint's ghost then shows.</summary>
    void HoverBelow()
    {
        var at = GetViewport().GetCanvasTransform() * (new Vector2(_smelter.X, _smelter.Y + 9) * Palette.CellPx);
        GetViewport().WarpMouse(at);
        var tool = GetTree().Root.FindChild("BlueprintTool", true, false) as BlueprintTool ?? throw new InvalidOperationException("no BlueprintTool");
        Log(tool.Mode == BlueprintMode.Place ? $"blueprint picked: {tool.Status.Text}" : "FAIL: Place didn't pick the blueprint");
    }

    (int X, int Y) _pasteAt;

    BuildTool Tool() => GetTree().Root.FindChild("BuildTool", true, false) as BuildTool ?? throw new InvalidOperationException("no BuildTool");

    /// <summary>A left button press or release through Input, which BuildTool polls.</summary>
    void Mouse(bool pressed)
    {
        var at = GetViewport().GetMousePosition();
        Godot.Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
        });
    }

    Vector2 CellCentre(int x, int y) => GetViewport().GetCanvasTransform() * (new Vector2(x + 0.5f, y + 0.5f) * Palette.CellPx);

    /// <summary>FB-023: M, then a click on the smelter cuts it.</summary>
    void CutSmelter()
    {
        Key(Godot.Key.M);
        GetViewport().WarpMouse(CellCentre(_smelter.X, _smelter.Y));
        Mouse(true);
    }

    /// <summary>Releases the click and hovers the nearest spot at least three cells off where the paste check passes.</summary>
    void AimPaste()
    {
        Mouse(false);
        var tool = Tool();
        Log(tool is { Moving: true, Cut.DefId: "smelter" } ? "M and a click cut the smelter" : "FAIL: the smelter wasn't cut");
        var sim = _host.Sim;
        var tile = _host.ViewedTile;
        var spot = Enumerable.Range(-8, 17).SelectMany(dy => Enumerable.Range(-8, 17).Select(dx => (X: _smelter.X + dx, Y: _smelter.Y + dy)))
            .Where(p => Math.Max(Math.Abs(p.X - _smelter.X), Math.Abs(p.Y - _smelter.Y)) >= 3)
            .OrderBy(p => Math.Abs(p.X - _smelter.X) + Math.Abs(p.Y - _smelter.Y))
            .FirstOrDefault(p => Relocation.Check(sim, tile, _host.LocalPlayer, _smelter.X, _smelter.Y, tile, p.X, p.Y, 0, out _) == PlaceResult.Ok);
        _pasteAt = spot;
        GetViewport().WarpMouse(CellCentre(spot.X, spot.Y));
    }

    /// <summary>The smelter stands at the paste spot, the same building; then it goes back by command for the later steps.</summary>
    void CheckPasted()
    {
        var tile = _host.ViewedTile;
        var b = tile.Grid.BuildingAt(_pasteAt.X, _pasteAt.Y);
        Log(b is { DefId: "smelter" } && tile.Grid.BuildingAt(_smelter.X, _smelter.Y) == null
            ? $"smelter pasted at {_pasteAt}" : $"FAIL: the paste at {_pasteAt} didn't move the smelter");
        Key(Godot.Key.M);
        if (b is { DefId: "smelter" })
            _host.Send(new MoveBuildingCommand(_host.LocalPlayer, tile.Id, b.X, b.Y, tile.Id, _smelter.X, _smelter.Y));
    }

    void HoverSmelter()
    {
        var at = GetViewport().GetCanvasTransform() * (new Vector2(_smelter.X + 1, _smelter.Y + 1) * Palette.CellPx);
        GetViewport().WarpMouse(at);
        Key(Godot.Key.Escape); // Drop the drill so the hover panel shows what's under the cursor.
    }

    void Shot(string name)
    {
        string path = Path.Combine(_dir, name + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        Log($"screenshot {path}");
    }

    /// <summary>Presses the visible button whose text starts with <paramref name="text"/>, as a click would.</summary>
    void Press(string text)
    {
        var button = Find(GetTree().Root, text) ?? throw new InvalidOperationException($"no visible button '{text}'");
        if (button.Disabled)
            throw new InvalidOperationException($"button '{text}' is disabled");
        Log($"press '{button.Text}'");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    static Button? Find(Node node, string text)
    {
        if (node is Button b && b.IsVisibleInTree() && b.Text.StartsWith(text))
            return b;
        foreach (var child in node.GetChildren())
        {
            if (Find(child, text) is { } hit)
                return hit;
        }
        return null;
    }

    /// <summary>
    /// Warps the mouse over the home circle and turns the wheel up one notch at a time until <paramref name="done"/>
    /// holds for the camera (at most 60 notches).
    /// </summary>
    void WheelUntil(Func<CameraController, bool> done)
    {
        var camera = GetTree().Root.FindChild("Camera", true, false) as CameraController ?? throw new InvalidOperationException("no Camera");
        var home = _host.Sim.Tiles[_host.Sim.StartTileId];
        var cell = new Vector2(home.OriginX + home.CircleX + 0.5f, home.OriginY + home.CircleY + 0.5f);
        var canvas = _host.View == ViewMode.Tile ? (cell - new Vector2(home.OriginX, home.OriginY)) * Palette.CellPx : cell * WorldMapView.CellPx;
        var at = GetViewport().GetCanvasTransform() * canvas;
        GetViewport().WarpMouse(at);
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        for (int i = 0; i < 60 && !done(camera); i++)
        {
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = pressed, Position = at, GlobalPosition = at });
        }
    }

    void Key(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });
    }

    /// <summary>A drill on ore with no belt (it will fill up), a starved smelter, and the drill tool hovering ore.</summary>
    void Build()
    {
        var sim = _host.Sim;
        var tile = _host.ViewedTile;
        int me = _host.LocalPlayer;
        // Ore nearest the middle of the tile (tiles are irregular, so search all of it).
        var spots = new List<(int X, int Y)>();
        int mx = tile.CenterX - tile.OriginX, my = tile.CenterY - tile.OriginY;
        var cells = Enumerable.Range(0, tile.Grid.Width * tile.Grid.Height)
            .Select(i => (X: i % tile.Grid.Width, Y: i / tile.Grid.Width))
            .OrderBy(c => Math.Abs(c.X - mx) + Math.Abs(c.Y - my));
        foreach (var (x, y) in cells)
        {
            if (spots.Count == 2)
                break;
            if (Placement.Check(sim, tile, me, "drill", x, y, 0, null, out _) == PlaceResult.Ok
                && spots.All(s => Math.Abs(s.X - x) > 4 || Math.Abs(s.Y - y) > 4))
                spots.Add((x, y));
        }
        if (spots.Count < 2)
            throw new InvalidOperationException("no ore near the middle of the tile");
        _host.Send(new PlaceBuildingCommand(me, tile.Id, "drill", spots[0].X, spots[0].Y));
        // A 7x5 block of open ground in build range (WM-015) nearest the middle: the smelter on top, a belt along the bottom.
        var g = tile.Grid;
        int cx = tile.CenterX - tile.OriginX, cy = tile.CenterY - tile.OriginY;
        var open = Enumerable.Range(0, g.Width * g.Height)
            .Select(i => (X: i % g.Width, Y: i / g.Width))
            .Where(p => g.InBounds(p.X, p.Y, 7, 5) && g.Buildable(p.X, p.Y, 7, 5) && g.IsFree(p.X, p.Y, 7, 5) && Territory.InBuildRadius(sim, me, tile, p.X, p.Y, 7, 5)
                && spots.All(s => s.X + 2 <= p.X || s.X >= p.X + 7 || s.Y + 2 <= p.Y || s.Y >= p.Y + 5))
            .OrderBy(p => Math.Abs(p.X - cx) + Math.Abs(p.Y - cy))
            .First();
        _smelter = (open.X + 4, open.Y);
        _host.Send(new PlaceBuildingCommand(me, tile.Id, "smelter", _smelter.X, _smelter.Y, RecipeId: "smelt_iron"));
        for (int x = open.X; x < open.X + 7; x++)
            _host.Send(new PlaceBuildingCommand(me, tile.Id, "conveyor", x, open.Y + 4, 0));
        // A short cable run (FB-020) under the scout, which the tile shots frame, so the cable layer shows in them.
        var scout = tile.Grid.Cores[0];
        for (int x = scout.X - 2; x < scout.X + 4; x++)
            _host.Send(new PlaceBuildingCommand(me, tile.Id, "wire", x, scout.Y + 3));
        Log($"placed a drill at {spots[0]}, a smelter, a belt and a cable");

        var tool = GetTree().Root.FindChild("BuildTool", true, false) as BuildTool ?? throw new InvalidOperationException("no BuildTool");
        tool.Select(0);
        var at = GetViewport().GetCanvasTransform() * (new Vector2(spots[1].X + 1, spots[1].Y + 1) * Palette.CellPx);
        GetViewport().WarpMouse(at);
    }
}

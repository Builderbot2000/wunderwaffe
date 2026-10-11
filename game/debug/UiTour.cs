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
        Step(1, "new game", () => Press("New game"));
        Step(10, "setup", () => { Shot("02_new_game"); Press("Start"); });
        Step(40, "world", () => Shot("03_world"));
        Step(1, "enter", () => _host.EnterTile(_host.ViewedTile.Id));
        Step(20, "tile", () => Shot("04_tile"));
        Step(1, "build", () => { Build(); WaitTicks(14 * 60); });
        Step(1, "built", () => Shot("05_building"));
        Step(1, "hover machine", HoverSmelter);
        Step(10, "hover shot", () => Shot("05b_hover"));
        Step(1, "port zones", ShowPortZones);
        Step(10, "port zones shot", () => { Shot("05c_port_zones"); Key(Godot.Key.Escape); });
        Step(5, "world again", () => { Key(Godot.Key.Tab); });
        Step(10, "select coal tile", SelectCoalNeighbour);
        Step(1, "lift off scout", LiftOffScout);
        Step(10, "select scout", SelectScout);
        Step(10, "world tile panel", () => { Shot("07_world_coal"); Press("Send units"); });
        Step(30, "scout sent", () =>
        {
            // WM-006: the scout flies to the coal tile's victory circle; landing there and holding it claims the tile.
            var scout = _host.Sim.Units.FirstOrDefault(u => u.Owner == _host.LocalPlayer);
            Log(scout is { Moving: true } ? "scout on its way to the coal tile's circle" : "FAIL: the scout didn't take the order");
            Shot("07b_scout_sent");
        });
        Step(1, "back to tile", () => Key(Godot.Key.Tab));
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
        Step(1, "done", () =>
        {
            // Leave the player's saves folder as it was.
            foreach (var save in SaveSlots.List().Where(s => !_savesBefore.Contains(s.Path)))
                SaveSlots.Delete(save.Path);
            GetTree().Quit();
        });
    }

    void Step(int frames, string name, Action action) => _steps.Add((frames, name, action));

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
        var core = home.Grid.Buildings.First(b => b.Kind == Sim.Content.BuildingKind.UnitCore);
        _host.Sim.Enqueue(new ActivateUnitCommand(_host.LocalPlayer, home.Id, core.X, core.Y));
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
        sim.Enqueue(new PlaceBuildingCommand(me, tile.Id, "drill", spots[0].X, spots[0].Y));
        // A 7x5 block of open ground nearest the middle: the smelter on top, a belt along the bottom.
        var g = tile.Grid;
        int cx = tile.CenterX - tile.OriginX, cy = tile.CenterY - tile.OriginY;
        var open = Enumerable.Range(0, g.Width * g.Height)
            .Select(i => (X: i % g.Width, Y: i / g.Width))
            .Where(p => g.InBounds(p.X, p.Y, 7, 5) && g.Buildable(p.X, p.Y, 7, 5) && g.IsFree(p.X, p.Y, 7, 5)
                && spots.All(s => s.X + 2 <= p.X || s.X >= p.X + 7 || s.Y + 2 <= p.Y || s.Y >= p.Y + 5))
            .OrderBy(p => Math.Abs(p.X - cx) + Math.Abs(p.Y - cy))
            .First();
        _smelter = (open.X + 4, open.Y);
        sim.Enqueue(new PlaceBuildingCommand(me, tile.Id, "smelter", _smelter.X, _smelter.Y, RecipeId: "smelt_iron"));
        for (int x = open.X; x < open.X + 7; x++)
            sim.Enqueue(new PlaceBuildingCommand(me, tile.Id, "conveyor", x, open.Y + 4, 0));
        Log($"placed a drill at {spots[0]}, a smelter and a belt");

        var tool = GetTree().Root.FindChild("BuildTool", true, false) as BuildTool ?? throw new InvalidOperationException("no BuildTool");
        tool.Select(0);
        var at = GetViewport().GetCanvasTransform() * (new Vector2(spots[1].X + 1, spots[1].Y + 1) * Palette.CellPx);
        GetViewport().WarpMouse(at);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Game.Screens;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>What <see cref="BlueprintTool"/> is doing with the mouse.</summary>
public enum BlueprintMode
{
    None,

    /// <summary>Dragging a box to record the buildings and frame in it.</summary>
    Capture,

    /// <summary>A library blueprint follows the cursor as ghosts; a click places it in one action.</summary>
    Place,

    /// <summary>As Place, but a click sets the selected factory to build it there.</summary>
    Project,
}

/// <summary>
/// Blueprints on the tile view (FB-009, UD-001, UD-006). K over a unit core records its design; the library panel's
/// Capture area button records a dragged box. A picked blueprint shows as ghosts, green or red by the sim's own check:
/// R turns it, F mirrors it, left-click places it (or projects it from the selected factory), right-click or Esc drops
/// it. Clicking a unit factory with nothing picked selects it. Sits after <see cref="UnitTool"/> in the scene so it sees
/// the mouse first.
/// </summary>
public partial class BlueprintTool : Node2D
{
    [Export] public BuildTool BuildTool = null!;

    static readonly Color CaptureFill = new(0.35f, 0.75f, 0.95f, 0.15f);
    static readonly Color CaptureLine = new(0.35f, 0.75f, 0.95f, 0.9f);
    static readonly Color RadiusLine = new(0.98f, 0.78f, 0.3f, 0.7f);
    static readonly Color Offending = new(1f, 0.3f, 0.25f, 0.9f);

    SimHost _host = null!;
    int _rotation;
    bool _flip;
    Vector2I? _captureStart;
    BlueprintPlan? _plan;
    int _factoryTile = -1, _factoryX, _factoryY;

    public BlueprintMode Mode { get; private set; }
    public BlueprintEntry? Picked { get; private set; }

    /// <summary>One line on what a click would do now, for the HUD's placement line; Good is false when it would fail.</summary>
    public (string Text, bool Good) Status { get; private set; } = ("", true);

    /// <summary>Messages for the HUD to toast: text and whether it went well.</summary>
    public event Action<string, bool>? Notice;

    public bool Active => Mode != BlueprintMode.None;

    /// <summary>The selected unit factory, if it is still standing on the viewed tile.</summary>
    public PlacedBuilding? SelectedFactory =>
        _host.View == ViewMode.Tile && _host.ViewedTile.Id == _factoryTile
        && _host.ViewedTile.Grid.BuildingAt(_factoryX, _factoryY) is { Kind: BuildingKind.UnitFactory } f
        && f.X == _factoryX && f.Y == _factoryY ? f : null;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _host.ShowOnlyIn(this, ViewMode.Tile, () =>
        {
            Cancel();
            _factoryTile = -1;
        });
    }

    public void Pick(BlueprintEntry entry)
    {
        BuildTool.Select(-1);
        Picked = entry;
        Mode = BlueprintMode.Place;
    }

    /// <summary>Picks a blueprint to project from the selected factory.</summary>
    public void PickForFactory(BlueprintEntry entry)
    {
        if (SelectedFactory == null)
            return;
        Pick(entry);
        Mode = BlueprintMode.Project;
    }

    public void StartCapture()
    {
        BuildTool.Select(-1);
        Picked = null;
        Mode = BlueprintMode.Capture;
    }

    public void Cancel()
    {
        Mode = BlueprintMode.None;
        Picked = null;
        _captureStart = null;
        _plan = null;
    }

    /// <summary>Stops the selected factory.</summary>
    public void StopFactory()
    {
        if (SelectedFactory is { } f)
            _host.Sim.Enqueue(new SetFactoryBlueprintCommand(_host.LocalPlayer, _host.ViewedTile.Id, f.X, f.Y, null));
    }

    public void SelectFactory(PlacedBuilding? factory)
    {
        _factoryTile = factory == null ? -1 : _host.ViewedTile.Id;
        (_factoryX, _factoryY) = factory == null ? (0, 0) : (factory.X, factory.Y);
    }

    /// <summary>Records the design at the unit core under (x, y). Returns the new entry, or null with a notice saying why not.</summary>
    public BlueprintEntry? RecordUnitAt(int x, int y)
    {
        var tile = _host.ViewedTile;
        var bp = Blueprints.FromUnitCore(_host.Sim, tile, _host.LocalPlayer, x, y, out var result);
        if (bp == null)
        {
            Notice?.Invoke($"Can't record this design: {GameText.Assemble(result)}", false);
            return null;
        }
        var entry = BlueprintLibrary.Add(BlueprintLibrary.NextName("Unit"), bp, ["unit"]);
        Notice?.Invoke($"Recorded {entry.Name} ({GameText.Plural(bp.Frames.Length, "cell", "cells")}) to the blueprint library", true);
        return entry;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_host.View != ViewMode.Tile)
            return;
        var cell = HoveredCell();
        var tile = _host.ViewedTile;

        if (e.IsActionPressed(InputActions.BlueprintRecord))
        {
            if (tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitCore })
                RecordUnitAt(cell.X, cell.Y);
            else
                Notice?.Invoke("Hover a unit core to record its design (K)", false);
            Handled();
            return;
        }
        if (e.IsActionPressed(InputActions.Cancel) && (Active || SelectedFactory != null))
        {
            if (Active)
                Cancel();
            else
                SelectFactory(null);
            Handled();
            return;
        }
        if (Mode is BlueprintMode.Place or BlueprintMode.Project)
        {
            if (e.IsActionPressed(InputActions.Rotate))
                _rotation = (_rotation + 1) & 3;
            else if (e.IsActionPressed(InputActions.BlueprintFlip))
                _flip = !_flip;
        }

        if (e is not InputEventMouseButton mouse || OverUi())
            return;
        switch (Mode)
        {
            case BlueprintMode.Capture when mouse.ButtonIndex == MouseButton.Left:
                if (mouse.Pressed)
                {
                    _captureStart = cell;
                }
                else if (_captureStart is { } start)
                {
                    Capture(start, cell);
                    Cancel();
                }
                Handled();
                break;
            case BlueprintMode.Place or BlueprintMode.Project when mouse is { Pressed: true, ButtonIndex: MouseButton.Right }:
                Cancel();
                Handled();
                break;
            case BlueprintMode.Place when mouse is { Pressed: true, ButtonIndex: MouseButton.Left }:
                if (Ghost() is var (x, y) && Status.Good)
                    _host.Sim.Enqueue(new PlaceBlueprintCommand(_host.LocalPlayer, tile.Id, Picked!.Blueprint, x, y, _rotation, _flip));
                Handled();
                break;
            case BlueprintMode.Project when mouse is { Pressed: true, ButtonIndex: MouseButton.Left }:
                if (Ghost() is var (ox, oy) && Status.Good && SelectedFactory is { } f)
                {
                    _host.Sim.Enqueue(new SetFactoryBlueprintCommand(_host.LocalPlayer, tile.Id, f.X, f.Y, Picked!.Blueprint, ox, oy, _rotation, _flip));
                    Notice?.Invoke($"Factory set to build {Picked.Name}", true);
                    Cancel();
                }
                Handled();
                break;
            case BlueprintMode.None when mouse is { Pressed: true, ButtonIndex: MouseButton.Left } && !BuildTool.HasTool:
                if (tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitFactory } factory && factory.Owner == _host.LocalPlayer)
                {
                    SelectFactory(factory);
                    Handled();
                }
                else
                {
                    SelectFactory(null);
                }
                break;
        }
    }

    void Handled() => GetViewport().SetInputAsHandled();

    bool OverUi() => GetViewport().GuiGetHoveredControl() != null;

    void Capture(Vector2I a, Vector2I b)
    {
        int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        var bp = Blueprints.FromArea(_host.ViewedTile, x, y, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
        if (bp == null)
        {
            Notice?.Invoke("Nothing in that box to record", false);
            return;
        }
        var entry = BlueprintLibrary.Add(BlueprintLibrary.NextName("Blueprint"), bp, bp.IsUnit(_host.Content.Buildings) ? ["unit"] : []);
        Notice?.Invoke($"Recorded {entry.Name} ({GameText.Plural(bp.Blocks.Length, "building", "buildings")})", true);
    }

    public override void _Process(double delta)
    {
        if (BuildTool.HasTool && Active)
            Cancel();
        if (Mode == BlueprintMode.Project && SelectedFactory == null)
            Cancel();
        Status = Mode switch
        {
            BlueprintMode.Capture => ("Drag a box over buildings and frame to record them · Esc cancels", true),
            BlueprintMode.Place or BlueprintMode.Project => Preview(),
            _ => ("", true),
        };
        QueueRedraw();
    }

    /// <summary>Top-left of the ghost: the blueprint centred on the hovered cell.</summary>
    (int X, int Y)? Ghost()
    {
        if (Picked == null)
            return null;
        var bp = Blueprints.Transform(_host.Content.Buildings, Picked.Blueprint, _rotation, _flip);
        var (w, h) = bp.Size(_host.Content.Buildings);
        var cell = HoveredCell();
        return (cell.X - (w - 1) / 2, cell.Y - (h - 1) / 2);
    }

    (string, bool) Preview()
    {
        var (x, y) = Ghost()!.Value;
        var sim = _host.Sim;
        var tile = _host.ViewedTile;
        var bp = Picked!.Blueprint;
        string head = $"{Picked.Name} · {"→↓←↑"[_rotation]}{(_flip ? " mirrored" : "")}";
        if (Mode == BlueprintMode.Project)
        {
            var f = SelectedFactory!;
            var result = UnitFactory.CheckProject(sim, tile, _host.LocalPlayer, f.X, f.Y, bp, x, y, _rotation, _flip, out var plan);
            _plan = plan;
            return result switch
            {
                ProjectResult.Ok => ($"{head} · click to build here from the factory", true),
                ProjectResult.TooLarge => ($"{head} · {UnitFactory.SizeOf(_host.Content.Buildings, bp)} blocks is over this factory's cap of {UnitFactory.BlockCap(sim, tile, f)}: add boosters", false),
                ProjectResult.OutOfRange => ($"{head} · outside the factory's radius of {UnitFactory.Radius(sim, tile, f)}", false),
                _ => ($"{head} · {ProblemText(plan)}", false),
            };
        }
        var check = Blueprints.Check(sim, tile, _host.LocalPlayer, bp, x, y, _rotation, _flip, out var p);
        _plan = p;
        return check switch
        {
            BlueprintResult.Ok => ($"{head} · {GameText.Cost(_host.Content, p.Cost)} · click to place · R turn · F mirror", true),
            BlueprintResult.CannotAfford => ($"{head} · needs {GameText.Cost(_host.Content, p.Cost.Where(kv => tile.Inventory.Get(kv.Key) < kv.Value).Select(kv => new KeyValuePair<string, int>(kv.Key, (int)(kv.Value - tile.Inventory.Get(kv.Key)))))} more", false),
            _ => ($"{head} · {ProblemText(p)}", false),
        };
    }

    static string ProblemText(BlueprintPlan? plan) =>
        plan?.Problems.FirstOrDefault() is { } first
            ? $"{GameText.Plural(plan.Problems.Count, "block", "blocks")} can't go here ({first.Result})"
            : "can't go here";

    public override void _Draw()
    {
        float px = Palette.CellPx;
        var tile = _host.ViewedTile;
        if (SelectedFactory is { } factory)
        {
            DrawFactory(tile, factory, px);
        }
        if (Mode == BlueprintMode.Capture)
        {
            var at = HoveredCell();
            var from = _captureStart ?? at;
            var rect = new Rect2(new Vector2(Math.Min(from.X, at.X), Math.Min(from.Y, at.Y)) * px,
                new Vector2(Math.Abs(from.X - at.X) + 1, Math.Abs(from.Y - at.Y) + 1) * px);
            DrawRect(rect, CaptureFill);
            DrawRect(rect, CaptureLine, false, 2);
        }
        else if (_plan != null && Mode is BlueprintMode.Place or BlueprintMode.Project && !OverUi())
        {
            DrawGhosts(_plan, px, bad: _plan.Problems.Count > 0 || !Status.Good);
        }
        else if (Mode == BlueprintMode.None && !BuildTool.HasTool && HoveredCell() is var cell
                 && tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitCore })
        {
            // UD-003: point at the blocks that stop a lift-off.
            UnitAssembly.CheckActivate(_host.Sim, tile, _host.LocalPlayer, cell.X, cell.Y, UnitAssembly.MaxCells, out _, out var offending);
            foreach (var b in offending)
                DrawRect(new Rect2(b.X * px, b.Y * px, b.Width * px, b.Height * px), Offending, false, 3);
        }
    }

    void DrawFactory(TileState tile, PlacedBuilding f, float px)
    {
        int r = UnitFactory.Radius(_host.Sim, tile, f);
        DrawRect(new Rect2((f.X - r) * px, (f.Y - r) * px, (f.Width + 2 * r) * px, (f.Height + 2 * r) * px), RadiusLine, false, 2);
        DrawRect(new Rect2(f.X * px, f.Y * px, f.Width * px, f.Height * px), RadiusLine, false, 3);
        if (Mode == BlueprintMode.None && f.Factory!.Projected is { } projected)
        {
            // What it is building: blocks still to come, faint.
            var state = f.Factory;
            for (int i = state.Next; i < state.Steps.Count; i++)
            {
                var s = state.Steps[i];
                var (w, h) = s.Block < 0 ? (1, 1) : _host.Content.Buildings[projected.Blocks[s.Block].DefId].Footprint(projected.Blocks[s.Block].Rotation);
                var color = s.Block < 0 ? Palette.Frame : Palette.Building(projected.Blocks[s.Block].DefId);
                DrawRect(new Rect2(s.X * px, s.Y * px, w * px, h * px), color with { A = i == state.Next ? 0.5f : 0.2f });
            }
        }
    }

    void DrawGhosts(BlueprintPlan plan, float px, bool bad)
    {
        var defs = _host.Content.Buildings;
        var badBlocks = plan.Problems.Where(p => p.Block >= 0).Select(p => p.Block).ToHashSet();
        foreach (var f in plan.Placed.Frames)
            DrawRect(new Rect2((plan.X + f.X) * px, (plan.Y + f.Y) * px, px, px), Palette.Frame);
        for (int i = 0; i < plan.Placed.Blocks.Length; i++)
        {
            var b = plan.Placed.Blocks[i];
            if (!defs.TryGetValue(b.DefId, out var def))
                continue;
            var (w, h) = def.Footprint(b.Rotation);
            var rect = new Rect2((plan.X + b.X) * px, (plan.Y + b.Y) * px, w * px, h * px);
            var tint = badBlocks.Contains(i) || (bad && badBlocks.Count == 0) ? Palette.GhostBad : Palette.GhostOk;
            if (Sprites.Get(b.DefId) is { } tex)
            {
                bool turned = (b.Rotation & 1) == 1;
                var size = new Vector2(turned ? h : w, turned ? w : h) * px;
                DrawSetTransform(rect.GetCenter(), b.Rotation * Mathf.Pi / 2);
                DrawTextureRect(tex, new Rect2(-size / 2, size), false, tint with { A = 0.7f });
                DrawSetTransform(Vector2.Zero);
            }
            else
            {
                DrawRect(rect, Palette.Building(b.DefId) with { A = 0.5f });
            }
            DrawRect(rect, tint with { A = 0.3f });
        }
        foreach (var p in plan.Problems.Where(p => p.Block < 0))
            DrawRect(new Rect2(p.X * px, p.Y * px, px, px), Palette.GhostBad);
    }

    Vector2I HoveredCell() => TileView.CellAt(GetGlobalMousePosition());
}

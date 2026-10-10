using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Unit control. Tile view, with no building picked: left-click or drag a box selects your units (Shift adds),
/// right-click sends the selection to that cell, and a cell past the tile's edge sends it into the neighbouring
/// tile. World map: right-click sends the selection to the middle of that tile. G over a unit core lifts the
/// framed blocks around it off as a unit; G with units selected lands them. The selection is shared between both
/// views, in <see cref="SimHost.Selected"/>.
/// </summary>
public partial class UnitTool : Node2D
{
    const float DragThresholdPx = 6;

    static readonly Color BoxFill = new(1f, 1f, 1f, 0.12f);
    static readonly Color BoxLine = new(1f, 1f, 1f, 0.8f);

    [Export] public BuildTool? BuildTool;

    SimHost _host = null!;
    Vector2? _dragStart;
    Vector2 _dragNow;

    public string Status { get; private set; } = "";

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        _host.ViewChanged += OnViewChanged;
    }

    public override void _ExitTree() => _host.ViewChanged -= OnViewChanged;

    void OnViewChanged() => _dragStart = null;

    bool Active => _host.View == ViewMode.World || BuildTool is not { HasTool: true };

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Active)
        {
            _dragStart = null;
            return;
        }

        if (e.IsActionPressed(InputActions.Cancel) && _host.Selected.Count > 0)
        {
            _host.Selected.Clear();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_host.View == ViewMode.World)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                OrderToWorldTile();
            return;
        }

        if (e.IsActionPressed(InputActions.UnitToggle))
            ToggleUnit();

        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    _dragStart = _dragNow = GetGlobalMousePosition();
                }
                else if (_dragStart is { } start)
                {
                    Select(start, GetGlobalMousePosition());
                    _dragStart = null;
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                OrderToCursor();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_dragStart != null)
            _dragNow = GetGlobalMousePosition();
        Status = SelectionStatus();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_dragStart is not { } start || start.DistanceTo(_dragNow) < DragThresholdPx)
            return;
        var rect = new Rect2(start, Vector2.Zero).Expand(_dragNow);
        DrawRect(rect, BoxFill);
        DrawRect(rect, BoxLine, false, 1);
    }

    string SelectionStatus()
    {
        var sim = _host.Sim;
        int mine = sim.Units.Count(u => u.Owner == _host.LocalPlayer);
        string line = $"Units {mine}  selected {_host.Selected.Count}";
        if (_host.View != ViewMode.Tile || BuildTool is not { HasTool: false })
            return line;

        var cell = HoveredCell();
        var tile = _host.ViewedTile;
        if (tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitCore })
        {
            var check = UnitAssembly.CheckActivate(sim, tile, _host.LocalPlayer, cell.X, cell.Y, out var plan);
            line += check == AssembleResult.Ok
                ? $"\nG: lift off {plan!.Cells.Count} cells, {plan.Buildings.Count} blocks"
                : $"\nUnit core: {check}";
        }
        else if (_host.Selected.Count > 0)
        {
            line += "\nG: land selected units";
            int stuck = _host.Selected.Count(id => sim.UnitById(id) is { MaxSpeed: <= 0 });
            if (stuck > 0)
                line += $"\n{stuck} selected can't move: no thruster";
        }
        return line;
    }

    void Select(Vector2 from, Vector2 to)
    {
        bool add = Godot.Input.IsKeyPressed(Key.Shift);
        if (!add)
            _host.Selected.Clear();

        var tile = _host.ViewedTile;
        var own = _host.Sim.Units.Where(u => u.TileId == tile.Id && u.Owner == _host.LocalPlayer);
        if (from.DistanceTo(to) < DragThresholdPx)
        {
            // A click picks the nearest unit under the cursor, if any.
            var best = own
                .Select(u => (Unit: u, Dist: UnitView.PixelPos(u.X, u.Y).DistanceTo(to)))
                .Where(x => x.Dist <= x.Unit.Radius * (float)Palette.CellPx / Unit.CellScale)
                .OrderBy(x => x.Dist).ThenBy(x => x.Unit.Id)
                .Select(x => x.Unit).FirstOrDefault();
            if (best != null)
                _host.Selected.Add(best.Id);
            return;
        }

        var box = new Rect2(from, Vector2.Zero).Expand(to);
        foreach (var u in own.Where(u => box.HasPoint(UnitView.PixelPos(u.X, u.Y))))
            _host.Selected.Add(u.Id);
    }

    void OrderToCursor()
    {
        if (_host.Selected.Count == 0)
            return;
        var p = GetGlobalMousePosition() / Palette.CellPx;
        var tile = _host.ViewedTile;
        // The cell may be past the tile's edge; the sim maps it onto the neighbour it falls in.
        if (_host.Sim.ResolveCell(tile, Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y)) is not { } target)
            return;
        Send(target.Tile.Id, target.X, target.Y);
    }

    void OrderToWorldTile()
    {
        if (_host.Selected.Count == 0)
            return;
        var p = GetGlobalMousePosition() / WorldMapView.TilePx;
        if (_host.Sim.TileAt(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y)) is not { } tile)
            return;
        Send(tile.Id, tile.Grid.Width / 2, tile.Grid.Height / 2);
    }

    void Send(int tileId, int x, int y) =>
        _host.Sim.Enqueue(new MoveUnitsCommand(_host.LocalPlayer, [.. _host.Selected.Order()], tileId, x, y));

    Vector2I HoveredCell()
    {
        var p = GetGlobalMousePosition() / Palette.CellPx;
        return new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y));
    }

    /// <summary>G over a unit core lifts its blocks off; otherwise it lands the selected units.</summary>
    void ToggleUnit()
    {
        var cell = HoveredCell();
        var tile = _host.ViewedTile;
        if (tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitCore })
        {
            _host.Sim.Enqueue(new ActivateUnitCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y));
            return;
        }
        foreach (int id in _host.Selected.Order())
            _host.Sim.Enqueue(new DeployUnitCommand(_host.LocalPlayer, id));
        _host.Selected.Clear();
    }
}

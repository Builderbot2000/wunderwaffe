using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Unit control. Tile view, with no building picked: left-click or drag a box selects your units (Shift adds),
/// right-click sends the selection to that cell, and a cell past the tile's border sends it into the tile there.
/// World map: right-click sends the selection to the clicked spot. G over a unit core lifts the
/// framed blocks around it off as a unit; G with units selected lands them. The selection is shared between both
/// views, in <see cref="SimHost.Selected"/>.
/// </summary>
public partial class UnitTool : Node2D
{
    const float DragThresholdPx = 6;

    static readonly Color BoxFill = new(1f, 1f, 1f, 0.12f);
    static readonly Color BoxLine = new(1f, 1f, 1f, 0.8f);

    [Export] public BuildTool BuildTool = null!;

    SimHost _host = null!;
    Vector2? _dragStart;
    Vector2 _dragNow;

    public string Status { get; private set; } = "";

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _host.WatchView(this, () => _dragStart = null);
    }

    bool Active => _host.View == ViewMode.World || !BuildTool.HasTool;

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
            else if (e.IsActionPressed(InputActions.UnitToggle))
                Land();
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
        if (_host.View != ViewMode.Tile || BuildTool.HasTool)
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
            var landing = UnitAssembly.CheckDeploy(sim, _host.LocalPlayer, _host.Selected.Min(), out _);
            line += landing == DeployResult.Ok ? "\nG: land selected units" : $"\nCan't land here: {landing}";
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
        var own = _host.Sim.Units.Where(u => UnitView.InView(tile, u) && u.Owner == _host.LocalPlayer);
        if (from.DistanceTo(to) < DragThresholdPx)
        {
            // A click picks the nearest unit under the cursor, if any.
            var best = own
                .Select(u => (Unit: u, Dist: UnitView.PixelPos(tile, u.X, u.Y).DistanceTo(to)))
                .Where(x => x.Dist <= x.Unit.Radius * (float)Palette.CellPx / Unit.CellScale)
                .OrderBy(x => x.Dist).ThenBy(x => x.Unit.Id)
                .Select(x => x.Unit).FirstOrDefault();
            if (best != null)
                _host.Selected.Add(best.Id);
            return;
        }

        var box = new Rect2(from, Vector2.Zero).Expand(to);
        foreach (var u in own.Where(u => box.HasPoint(UnitView.PixelPos(tile, u.X, u.Y))))
            _host.Selected.Add(u.Id);
    }

    void OrderToCursor()
    {
        if (_host.Selected.Count == 0)
            return;
        var cell = HoveredCell();
        // The cell may be past the tile's border; the sim maps it onto the tile it falls in.
        if (_host.Sim.ResolveCell(_host.ViewedTile, cell.X, cell.Y) is not { } target)
            return;
        Send(target.Tile.Id, target.X, target.Y);
    }

    void OrderToWorldTile()
    {
        if (_host.Selected.Count == 0)
            return;
        var cell = WorldMapView.CellAt(GetGlobalMousePosition());
        if (_host.Sim.TileAtCell(cell.X, cell.Y) is not { } tile)
            return;
        Send(tile.Id, cell.X - tile.OriginX, cell.Y - tile.OriginY);
    }

    void Send(int tileId, int x, int y) =>
        _host.Sim.Enqueue(new MoveUnitsCommand(_host.LocalPlayer, [.. _host.Selected.Order()], tileId, x, y));

    Vector2I HoveredCell() => TileView.CellAt(GetGlobalMousePosition());

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
        Land();
    }

    /// <summary>Lands the selected units where they are (UD-011); on any tile, so a scout can take a foothold (WM-015).</summary>
    void Land()
    {
        foreach (int id in _host.Selected.Order())
            _host.Sim.Enqueue(new DeployUnitCommand(_host.LocalPlayer, id));
        _host.Selected.Clear();
    }
}

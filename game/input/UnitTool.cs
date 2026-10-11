using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Unit control (UC-001 to UC-006). Tile view, with no building picked: left-click or drag a box selects your units
/// (Shift adds), right-click sends the selection to that cell, and a cell past the tile's border sends it into the tile
/// there. World map: right-click sends the selection to the clicked spot. H holds, P then right-click patrols, X
/// retreats, Q makes the selection a squad, T cycles its formation, V cycles the stance, E escorts the unit under the
/// cursor, Y over a unit core then right-click sets its rally point. Ctrl+1-9 stores the selection as a control group,
/// Alt+1-9 recalls it. G over a unit core lifts the framed blocks around it off as a unit; G with units selected lands
/// them. The selection is shared between both views, in <see cref="SimHost.Selected"/>. Combat (CB-001): right-click on
/// an enemy unit or building attacks it, and Z arms an attack-move for the next right-click.
/// </summary>
public partial class UnitTool : Node2D
{
    const float DragThresholdPx = 6;

    static readonly Color BoxFill = new(1f, 1f, 1f, 0.12f);
    static readonly Color BoxLine = new(1f, 1f, 1f, 0.8f);

    static readonly Formation[] Formations = [Formation.Box, Formation.Line, Formation.Column, Formation.Wedge];
    static readonly Stance[] Stances = [Stance.Aggressive, Stance.Defensive, Stance.HoldFire];

    [Export] public BuildTool BuildTool = null!;

    SimHost _host = null!;
    Vector2? _dragStart;
    Vector2 _dragNow;

    /// <summary>What the next right-click does instead of a move.</summary>
    enum Armed { None, Patrol, Rally, AttackMove }
    Armed _armed;

    /// <summary>The building whose rally point is armed: tile and cell.</summary>
    (int Tile, int X, int Y) _rallyFrom;

    /// <summary>Control groups 1-9: unit ids. Local to this player's UI, not sim state.</summary>
    readonly List<int>[] _groups = [.. Enumerable.Range(0, 9).Select(_ => new List<int>())];

    public string Status { get; private set; } = "";

    /// <summary>A refused order, for the HUD to toast.</summary>
    public event System.Action<string>? Notice;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _host.WatchView(this, () => _dragStart = null);
    }

    bool Active => _host.View == ViewMode.World || !BuildTool.HasTool;

    public override void _UnhandledInput(InputEvent e)
    {
        // Control groups first, so Ctrl/Alt + a number never reaches the build palette's number keys.
        if (e is InputEventKey { Pressed: true, Echo: false } key && (key.CtrlPressed || key.AltPressed)
            && key.PhysicalKeycode is >= Key.Key1 and <= Key.Key9)
        {
            ControlGroup((int)(key.PhysicalKeycode - Key.Key1), key.CtrlPressed);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!Active)
        {
            _dragStart = null;
            return;
        }

        if (e.IsActionPressed(InputActions.Cancel) && (_armed != Armed.None || _host.Selected.Count > 0))
        {
            if (_armed != Armed.None)
                _armed = Armed.None;
            else
                _host.Selected.Clear();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (OrderKeys(e))
            return;

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
                if (!AttackAtCursor())
                    OrderToCursor();
                break;
        }
    }

    /// <summary>Hold, patrol, retreat, squad, formation, stance, escort and rally keys. True if one was handled.</summary>
    bool OrderKeys(InputEvent e)
    {
        var sim = _host.Sim;
        int me = _host.LocalPlayer;
        int[] ids = [.. _host.Selected.Order()];
        if (e.IsActionPressed(InputActions.UnitRally) && _host.View == ViewMode.Tile)
        {
            var cell = HoveredCell();
            if (_host.ViewedTile.Grid.BuildingAt(cell.X, cell.Y) != null)
            {
                _rallyFrom = (_host.ViewedTile.Id, cell.X, cell.Y);
                _armed = Armed.Rally;
            }
            return true;
        }
        if (ids.Length == 0)
            return false;

        if (e.IsActionPressed(InputActions.UnitHold))
            sim.Enqueue(new HoldUnitsCommand(me, ids));
        else if (e.IsActionPressed(InputActions.UnitPatrol))
            _armed = Armed.Patrol;
        else if (e.IsActionPressed(InputActions.AttackMove))
            _armed = Armed.AttackMove;
        else if (e.IsActionPressed(InputActions.UnitRetreat))
            sim.Enqueue(new RetreatUnitsCommand(me, ids));
        else if (e.IsActionPressed(InputActions.UnitSquad))
            sim.Enqueue(new CreateSquadCommand(me, $"Squad {sim.Squads.OwnedBy(me).Count() + 1}", ids));
        else if (e.IsActionPressed(InputActions.UnitFormation) && SelectedSquad() is { } squad)
        {
            int next = (System.Array.IndexOf(Formations, squad.Formation) + 1) % Formations.Length;
            sim.Enqueue(new SetFormationCommand(me, squad.Id, Formations[next]));
        }
        else if (e.IsActionPressed(InputActions.UnitStance))
        {
            var first = sim.UnitById(ids[0]);
            int next = first == null ? 0 : (System.Array.IndexOf(Stances, first.Stance) + 1) % Stances.Length;
            if (SelectedSquad() is { } s)
                sim.Enqueue(new SetSquadStanceCommand(me, s.Id, Stances[next]));
            else
                sim.Enqueue(new SetStanceCommand(me, ids, Stances[next]));
        }
        else if (e.IsActionPressed(InputActions.UnitEscort) && _host.View == ViewMode.Tile && UnitUnderCursor() is { } charge)
            sim.Enqueue(new SetStanceCommand(me, [.. ids.Where(id => id != charge.Id)], Stance.Escort, charge.Id));
        else
            return false;
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>The squad every selected unit is in, if they are all in one.</summary>
    Squad? SelectedSquad()
    {
        var sim = _host.Sim;
        var squads = _host.Selected.Select(id => sim.UnitById(id)?.SquadId ?? 0).Distinct().ToList();
        return squads is [int only and > 0] ? sim.Squads.ById(only) : null;
    }

    void ControlGroup(int index, bool store)
    {
        var group = _groups[index];
        if (store)
        {
            group.Clear();
            group.AddRange(_host.Selected.Order());
            return;
        }
        group.RemoveAll(id => _host.Sim.UnitById(id) == null);
        _host.Selected.Clear();
        foreach (int id in group)
            _host.Selected.Add(id);
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
        if (_armed == Armed.Patrol)
            return line + "\nRight-click: patrol there (Esc cancels)";
        if (_armed == Armed.Rally)
            return line + "\nRight-click: rally point for new units (Esc cancels)";
        if (_armed == Armed.AttackMove)
            return line + "\nRight-click: attack-move there, fighting on the way (Esc cancels)";
        if (_host.Selected.Count > 0)
            line += SelectionDetail();
        if (_host.View != ViewMode.Tile || BuildTool.HasTool)
            return line;

        var cell = HoveredCell();
        var tile = _host.ViewedTile;
        if (tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitCore } core)
        {
            var check = UnitAssembly.CheckActivate(sim, tile, _host.LocalPlayer, cell.X, cell.Y, out var plan);
            line += check == AssembleResult.Ok
                ? $"\nG: lift off {plan!.Cells.Count} cells, {plan.Buildings.Count} blocks"
                : $"\nUnit core: {check}";
            line += sim.Squads.RallyOf(tile.Id, core.Id) is { } rally
                ? $"\nRally: {(rally.RallyTileId >= 0 ? sim.Tiles[rally.RallyTileId].Name : "none")}{(sim.Squads.ById(rally.SquadId) is { } s ? $", joins {s.Name}" : "")} (Y to change)"
                : "\nY: set rally point";
        }
        else if (_host.Selected.Count > 0)
        {
            var landing = UnitAssembly.CheckDeploy(sim, _host.LocalPlayer, _host.Selected.Min(), out _);
            line += landing == DeployResult.Ok ? "\nG: land" : $"\nCan't land here: {landing}";
            line += " · H hold · P patrol · Z attack-move · X retreat · Q squad · T formation · V stance · E escort";
            int stuck = _host.Selected.Count(id => sim.UnitById(id) is { MaxSpeed: <= 0 });
            if (stuck > 0)
                line += $"\n{stuck} selected can't move: no thruster";
        }
        return line;
    }

    /// <summary>Order, stance, locomotion and squad of the selection, when they agree.</summary>
    string SelectionDetail()
    {
        var units = _host.Selected.Select(_host.Sim.UnitById).Where(u => u != null).Select(u => u!).ToList();
        if (units.Count == 0)
            return "";
        static string Same<T>(IEnumerable<T> xs, string mixed) => xs.Distinct().ToList() is [var one] ? $"{one}" : mixed;
        string text = $"\n{Same(units.Select(u => u.Locomotion), "mixed")} · {Same(units.Select(u => u.Order?.Kind.ToString() ?? "idle"), "mixed orders")} · {Same(units.Select(u => u.Stance), "mixed stances")}";
        if (SelectedSquad() is { } squad)
            text += $" · {squad.Name} ({squad.Formation})";
        return text;
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
            if (UnitUnderCursor(to) is { } best && best.Owner == _host.LocalPlayer)
                _host.Selected.Add(best.Id);
            return;
        }

        var box = new Rect2(from, Vector2.Zero).Expand(to);
        foreach (var u in own.Where(u => box.HasPoint(UnitView.PixelPos(tile, u.X, u.Y))))
            _host.Selected.Add(u.Id);
    }

    /// <summary>The nearest unit (anyone's) whose body is under a point of the tile view.</summary>
    Unit? UnitUnderCursor(Vector2? at = null)
    {
        var tile = _host.ViewedTile;
        var pos = at ?? GetGlobalMousePosition();
        return _host.Sim.Units.Where(u => UnitView.InView(tile, u))
            .Select(u => (Unit: u, Dist: UnitView.PixelPos(tile, u.X, u.Y).DistanceTo(pos)))
            .Where(x => x.Dist <= x.Unit.Radius * (float)Palette.CellPx / Unit.CellScale)
            .OrderBy(x => x.Dist).ThenBy(x => x.Unit.Id)
            .Select(x => x.Unit).FirstOrDefault();
    }

    void OrderToCursor()
    {
        var cell = HoveredCell();
        // The cell may be past the tile's border; the sim maps it onto the tile it falls in.
        if (_host.Sim.ResolveCell(_host.ViewedTile, cell.X, cell.Y) is not { } target)
            return;
        Send(target.Tile.Id, target.X, target.Y);
    }

    void OrderToWorldTile()
    {
        var cell = WorldMapView.CellAt(GetGlobalMousePosition());
        if (_host.Sim.TileAtCell(cell.X, cell.Y) is not { } tile)
            return;
        Send(tile.Id, cell.X - tile.OriginX, cell.Y - tile.OriginY);
    }

    /// <summary>
    /// Right-click on an enemy unit or building under the cursor sends the selection to attack it (CB-001). False if
    /// there is nothing hostile there, so the click is a plain move.
    /// </summary>
    bool AttackAtCursor()
    {
        if (_host.Selected.Count == 0 || _armed != Armed.None)
            return false;
        int[] ids = [.. _host.Selected.Order()];
        var tile = _host.ViewedTile;
        var mouse = GetGlobalMousePosition();
        var enemy = _host.Sim.Units
            .Where(u => u.Owner != _host.LocalPlayer && UnitView.InView(tile, u))
            .Select(u => (Unit: u, Dist: UnitView.PixelPos(tile, u.X, u.Y).DistanceTo(mouse)))
            .Where(x => x.Dist <= x.Unit.Radius * (float)Palette.CellPx / Unit.CellScale)
            .OrderBy(x => x.Dist).ThenBy(x => x.Unit.Id)
            .Select(x => x.Unit).FirstOrDefault();
        if (enemy != null)
        {
            _host.Sim.Enqueue(new AttackCommand(_host.LocalPlayer, ids, enemy.Id));
            return true;
        }
        var cell = HoveredCell();
        if (_host.Sim.ResolveCell(tile, cell.X, cell.Y) is { } at
            && at.Tile.Grid.BuildingAt(at.X, at.Y) is { } b && b.Owner != _host.LocalPlayer)
        {
            _host.Sim.Enqueue(new AttackBuildingCommand(_host.LocalPlayer, ids, at.Tile.Id, at.X, at.Y));
            return true;
        }
        return false;
    }

    void Send(int tileId, int x, int y)
    {
        var sim = _host.Sim;
        int me = _host.LocalPlayer;
        var armed = _armed;
        _armed = Armed.None;
        if (armed == Armed.Rally)
        {
            var (ft, fx, fy) = _rallyFrom;
            int squad = SelectedSquad()?.Id ?? 0;
            sim.Enqueue(new SetRallyCommand(me, ft, fx, fy, tileId, x, y, squad));
            return;
        }
        if (_host.Selected.Count == 0)
            return;
        int[] ids = [.. _host.Selected.Order()];
        // The sim refuses a tile no selected unit's locomotion can reach (MoveResult.Unreachable); say so here, since
        // commands return nothing. Every tile has a victory circle, ocean tiles too, and a ground scout can't get there.
        if (!ids.Any(id => sim.UnitById(id) is { MaxSpeed: > 0 } u && UnitNav.Plan(sim, u, tileId, x, y) != null))
        {
            Notice?.Invoke(ids.Any(id => sim.UnitById(id) is { MaxSpeed: > 0 })
                ? $"Can't reach {sim.Tiles[tileId].Name}: the selected units can't cross the terrain on the way"
                : "The selected units can't move: no thruster");
            return;
        }
        sim.Enqueue(armed switch
        {
            Armed.Patrol => new PatrolUnitsCommand(me, ids, tileId, x, y),
            Armed.AttackMove => new AttackMoveCommand(me, ids, tileId, x, y),
            _ => (Command)new MoveUnitsCommand(me, ids, tileId, x, y),
        });
    }

    Vector2I HoveredCell() => TileView.CellAt(GetGlobalMousePosition());

    /// <summary>G over a unit core lifts its blocks off; otherwise it lands the selected units.</summary>
    void ToggleUnit()
    {
        var cell = HoveredCell();
        var tile = _host.ViewedTile;
        if (tile.Grid.BuildingAt(cell.X, cell.Y) is { Kind: BuildingKind.UnitCore })
        {
            _host.Send(new ActivateUnitCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y));
            return;
        }
        Land();
    }

    /// <summary>Lands the selected units where they are (UD-011); on any tile, so a scout can take a foothold (WM-015).</summary>
    void Land()
    {
        foreach (int id in _host.Selected.Order())
            _host.Send(new DeployUnitCommand(_host.LocalPlayer, id));
        _host.Selected.Clear();
    }
}

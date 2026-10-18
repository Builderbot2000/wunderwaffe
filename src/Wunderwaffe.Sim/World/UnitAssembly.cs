using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

public enum AssembleResult
{
    Ok,

    /// <summary>No presence on the tile (WM-015), or the core or a block on the frame is someone else's.</summary>
    NotOwner,

    /// <summary>The cell isn't a core.</summary>
    NoCore,

    /// <summary>The core isn't on a frame.</summary>
    NoFrame,

    /// <summary>The frame joined to the core carries a second core: every unit has exactly one (WM-016).</summary>
    MultipleCores,

    /// <summary>A building sits partly on the frame and partly off it.</summary>
    PartialBuilding,



    /// <summary>Ports can't ride on a unit.</summary>
    InvalidBlock,

    TooLarge,

    /// <summary>Onboard batteries and generators can't cover what the blocks draw (UD-003).</summary>
    NoPower,

    /// <summary>No block gives the design a way to move (UD-003, UD-004).</summary>
    NoLocomotion,

    /// <summary>A hull and wheels together, with no rotor: neither naval nor ground (UD-004).</summary>
    ConflictingLocomotion,
}

public enum DeployResult
{
    Ok,
    NotOwner,
    NoSuchUnit,

    /// <summary>Some cells it would land on are off the tile, unbuildable, built on, or already framed.</summary>
    Blocked,

    /// <summary>It would land in an enemy-owned victory circle while a building not of the player's stands in it (WM-015).</summary>
    EnemyCircle,
}

/// <summary>
/// Turning a framed group of buildings into a unit and back (units are blocks on a frame, lifted off by their core,
/// UD-010). Read-only checks are public so the UI can preview with the same rules the commands apply.
/// </summary>
public static class UnitAssembly
{
    public const int MaxCells = 64;

    /// <summary>
    /// Laying a cell of the content's frame (<see cref="ContentDb.Frame"/>, a building on the frame layer, FB-022): the
    /// ordinary placement check, so it needs presence on the tile (WM-015), stays out of a blocked enemy circle, and is
    /// paid by the player's cores on the tile (<see cref="Funds"/>).
    /// </summary>
    /// <param name="ignoreCost">Skip the cost check, for callers that pay a total themselves.</param>
    public static PlaceResult CheckFrame(Simulation sim, TileState tile, int player, int x, int y, bool ignoreCost = false) =>
        sim.Content.Frame is { } frame
            ? Placement.Check(sim, tile, player, frame.Id, x, y, 0, null, out _, ignoreCost)
            : PlaceResult.NoSuchBuilding;

    internal static void PlaceFrame(Simulation sim, TileState tile, int player, int x, int y)
    {
        if (CheckFrame(sim, tile, player, x, y) != PlaceResult.Ok)
            return;
        Funds.TryPay(tile, player, sim.Content.Frame!.Cost);
        tile.Grid.Lay(sim.Content.Frame, x, y);
    }

    /// <summary>Frames are ownerless: anyone with presence may take one up, even under a building; the refund goes to their nearest core there, if any.</summary>
    internal static void RemoveFrame(Simulation sim, TileState tile, int player, int x, int y) =>
        sim.TryLift(tile, player, tile.Grid.Layers.Of(BuildingKind.Frame), x, y);

    /// <summary>What one cell of frame costs (the content's <see cref="ContentDb.Frame"/>), for unit stats and factories.</summary>
    public static IReadOnlyDictionary<string, int> FrameCost(ContentDb content) =>
        content.Frame?.Cost ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();

    /// <summary>Everything an activation would take, found by walking the frame out from the core.</summary>
    public sealed record Plan(List<(int X, int Y)> Cells, List<PlacedBuilding> Buildings, PlacedBuilding Core);

    public static AssembleResult CheckActivate(Simulation sim, TileState tile, int player, int x, int y, out Plan? plan) =>
        CheckActivate(sim, tile, player, x, y, MaxCells, out plan, out _);

    /// <summary>
    /// Lift-off validation (UD-003): the frame joined to the core is connected and at most <paramref name="cap"/> cells
    /// (<see cref="MaxCells"/> by hand, the factory's block cap from a factory), every building on it lies wholly on it,
    /// there is one core and no port, onboard power covers what the blocks draw, and the design has a
    /// locomotion class. <paramref name="offending"/> lists the blocks behind a refusal, for the UI to point at.
    /// </summary>
    public static AssembleResult CheckActivate(
        Simulation sim, TileState tile, int player, int x, int y, int cap, out Plan? plan, out List<PlacedBuilding> offending)
    {
        plan = null;
        offending = [];
        // Presence, not ownership (WM-015): a landed unit can lift off again from a tile nobody owns.
        if (!Territory.HasPresence(sim, player, tile))
            return AssembleResult.NotOwner;
        var grid = tile.Grid;
        if (grid.BuildingAt(x, y) is not { Kind: BuildingKind.Core } core)
            return AssembleResult.NoCore;
        if (core.Owner != player)
            return AssembleResult.NotOwner;
        if (!grid.Has(BuildingKind.Frame, core.X, core.Y))
            return AssembleResult.NoFrame;

        // Flood fill over frame cells, 4-connected.
        var seen = new HashSet<(int, int)> { (core.X, core.Y) };
        var cells = new List<(int X, int Y)> { (core.X, core.Y) };
        for (int i = 0; i < cells.Count; i++)
        {
            foreach (var d in Directions.All)
            {
                var next = (cells[i].X + d.Dx(), cells[i].Y + d.Dy());
                if (!grid.Has(BuildingKind.Frame, next.Item1, next.Item2) || !seen.Add(next))
                    continue;
                if (cells.Count == cap)
                    return AssembleResult.TooLarge;
                cells.Add(next);
            }
        }
        cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

        // Every bad block is listed, for the first kind of problem found.
        var buildings = new List<PlacedBuilding>();
        var counted = new HashSet<int>();
        var first = AssembleResult.Ok;
        foreach (var (cx, cy) in cells)
        {
            if (grid.BuildingAt(cx, cy) is not { } b || !counted.Add(b.Id))
                continue;
            var problem = BlockProblem(b, core, player, seen);
            if (problem == AssembleResult.Ok)
            {
                buildings.Add(b);
                continue;
            }
            if (first == AssembleResult.Ok)
                first = problem;
            if (problem == first)
                offending.Add(b);
        }
        if (first != AssembleResult.Ok)
            return first;
        buildings.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

        var defs = sim.Content.Buildings;
        var stats = UnitStats.Of(sim.Content, buildings.Select(b => (b.DefId, b.Hp)), cells.Count);
        if (stats.PowerDraw > stats.PowerSupply)
        {
            offending.AddRange(buildings.Where(b => defs[b.DefId].Power > 0));
            return AssembleResult.NoPower;
        }
        var locomotion = Locomotion.Check(defs, buildings.Select(b => b.DefId), out _);
        if (locomotion == LocomotionResult.None)
        {
            offending.Add(core);
            return AssembleResult.NoLocomotion;
        }
        if (locomotion == LocomotionResult.Conflicting)
        {
            offending.AddRange(buildings.Where(b => defs[b.DefId].Locomotion is LocomotionClass.Ground or LocomotionClass.Naval));
            return AssembleResult.ConflictingLocomotion;
        }

        plan = new Plan(cells, buildings, core);
        return AssembleResult.Ok;
    }

    static AssembleResult BlockProblem(PlacedBuilding b, PlacedBuilding core, int player, HashSet<(int, int)> frame)
    {
        if (b.Owner != player)
            return AssembleResult.NotOwner;
        for (int by = b.Y; by < b.Y + b.Height; by++)
        {
            for (int bx = b.X; bx < b.X + b.Width; bx++)
            {
                if (!frame.Contains((bx, by)))
                    return AssembleResult.PartialBuilding;
            }
        }
        if (b.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort)
            return AssembleResult.InvalidBlock;
        if (b.Kind == BuildingKind.Core && b != core)
            return AssembleResult.MultipleCores;
        return AssembleResult.Ok;
    }

    internal static AssembleResult Activate(Simulation sim, TileState tile, int player, int x, int y) =>
        Launch(sim, tile, player, x, y, MaxCells, null, out _);

    /// <summary>
    /// Lifts the design at the core (x, y) off as a unit, if <see cref="CheckActivate(Simulation, TileState, int, int, int, int, out Plan?, out List{PlacedBuilding})"/>
    /// passes with <paramref name="cap"/>. <paramref name="factory"/> is the factory that built it, if any.
    /// </summary>
    internal static AssembleResult Launch(
        Simulation sim, TileState tile, int player, int x, int y, int cap, PlacedBuilding? factory, out Unit? unit)
    {
        unit = null;
        var result = CheckActivate(sim, tile, player, x, y, cap, out var plan, out _);
        if (result != AssembleResult.Ok)
            return result;

        var (cells, buildings, core) = plan!;
        var blocks = buildings.Select(b => new UnitBlock(b.DefId, b.X - core.X, b.Y - core.Y, b.Width, b.Height, b.Rotation, b.RecipeId, b.Hp)).ToList();
        unit = Spawn(sim, tile, player, core.X, core.Y, cells.Select(c => (c.X - core.X, c.Y - core.Y)).ToList(), blocks);
        sim.Player(player).Launched++;

        // The buildings are the unit now: the core's stock and whatever the others held ride along as its cargo (UD-010).
        foreach (var b in buildings)
        {
            tile.Grid.Remove(b);
            TileEntities.Drain(b, unit.Cargo);
        }
        foreach (var (cx, cy) in cells)
            tile.Grid.Clear(BuildingKind.Frame, cx, cy);
        Built(sim, unit, tile, factory ?? core);
        return AssembleResult.Ok;
    }

    /// <summary>
    /// A new unit at rest, heading 0, its core's top-left cell at tile-local (<paramref name="coreX"/>, <paramref name="coreY"/>).
    /// <paramref name="cells"/> (frame) and <paramref name="blocks"/> are relative to that cell. Lift-off and scenario
    /// units (WM-017) both come through here.
    /// </summary>
    internal static Unit Spawn(Simulation sim, TileState tile, int player, int coreX, int coreY,
        IReadOnlyList<(int X, int Y)> cells, IReadOnlyList<UnitBlock> blocks)
    {
        // Mass: each building's cost, spread over its cells, on top of the frame under every cell.
        var mass = new Dictionary<(int, int), int>();
        foreach (var c in cells)
            mass[c] = Unit.FrameMass;
        foreach (var b in blocks)
        {
            int total = Math.Max(1, sim.Content.Buildings[b.DefId].Cost.Values.Sum());
            int n = b.Width * b.Height, i = 0;
            for (int cy = b.GY; cy < b.GY + b.Height; cy++)
            {
                for (int cx = b.GX; cx < b.GX + b.Width; cx++, i++)
                    mass[(cx, cy)] += total / n + (i < total % n ? 1 : 0);
            }
        }

        var body = cells.Select(c => (c.X, c.Y, mass[c])).ToList();
        var (gx, gy) = tile.ToGlobal(coreX, coreY);
        int px = gx * Unit.CellScale + Unit.CellScale / 2, py = gy * Unit.CellScale + Unit.CellScale / 2;
        var unit = sim.SpawnUnit(player, tile.Id, px, py, blocks, body, 0, 0);
        unit.UpkeepDue = sim.Tick + Balance.UpkeepPeriod;
        return unit;
    }

    /// <summary>
    /// Called once for every unit that lifts off, by hand (<paramref name="source"/> is its core, now gone) or from
    /// a factory (<paramref name="source"/> is the factory). The hook for rally and auto-squad (UC-006,
    /// <c>UnitSim.OnUnitBuilt</c>) and the unit-completed alert (UI-003, <c>sim.Raise</c>).
    /// </summary>
    static void Built(Simulation sim, Unit unit, TileState tile, PlacedBuilding source)
    {
        UnitSim.OnUnitBuilt(sim, unit, tile.Id, source.Id);
        sim.Raise(GameEventKind.UnitCompleted, unit.Owner, tile.Id, unit.Id);
    }

    /// <summary>Where a unit's blocks would land: its heading is rounded to the nearest quarter turn.</summary>
    public sealed record DeployPlan(int Quarters, List<(int X, int Y)> Cells, List<(UnitBlock Block, int X, int Y, int W, int H, int Rotation)> Blocks);

    public static DeployResult CheckDeploy(Simulation sim, int player, int unitId, out DeployPlan? plan)
    {
        plan = null;
        if (sim.UnitById(unitId) is not { } unit)
            return DeployResult.NoSuchUnit;
        var tile = sim.Tiles[unit.TileId];
        // The unit is itself presence on the tile (WM-015), so landing needs no claim (UD-011).
        if (unit.Owner != player)
            return DeployResult.NotOwner;

        int q = ((unit.Angle + Trig.Quarter / 2) / Trig.Quarter) & 3;
        var (rx, ry) = Trig.Rotate(unit.CoreOffX, unit.CoreOffY, unit.Angle);
        int coreX = (int)IntMath.FloorDiv(unit.X + rx, Unit.CellScale) - tile.OriginX;
        int coreY = (int)IntMath.FloorDiv(unit.Y + ry, Unit.CellScale) - tile.OriginY;

        var grid = tile.Grid;
        var cells = new List<(int X, int Y)>();
        foreach (var c in unit.Cells)
        {
            var (dx, dy) = Turn(c.GX - unit.CoreGX, c.GY - unit.CoreGY, q);
            int cx = coreX + dx, cy = coreY + dy;
            if (sim.Content.Frame is not { } frame || grid.BuildingAt(cx, cy) != null || grid.WreckAt(cx, cy) != null
                || Placement.Fits(sim.Content, grid, grid.Layers.Of(frame).Layer, cx, cy) != PlaceResult.Ok)
                return DeployResult.Blocked;
            cells.Add((cx, cy));
        }

        var blocks = new List<(UnitBlock, int, int, int, int, int)>();
        foreach (var b in unit.Blocks)
        {
            // The turned footprint's top-left is the smaller corner of two opposite cells.
            var (ax, ay) = Turn(b.GX - unit.CoreGX, b.GY - unit.CoreGY, q);
            var (bx, by) = Turn(b.GX + b.Width - 1 - unit.CoreGX, b.GY + b.Height - 1 - unit.CoreGY, q);
            blocks.Add((b, coreX + Math.Min(ax, bx), coreY + Math.Min(ay, by), Math.Abs(ax - bx) + 1, Math.Abs(ay - by) + 1, (b.Rotation + q) & 3));
        }
        foreach (var (_, x, y, w, h, _) in blocks)
        {
            if (Territory.EnemyCircleBlocks(tile, player, x, y, w, h))
                return DeployResult.EnemyCircle;
        }
        plan = new DeployPlan(q, cells, blocks);
        return DeployResult.Ok;
    }

    internal static DeployResult Deploy(Simulation sim, int player, int unitId)
    {
        var result = CheckDeploy(sim, player, unitId, out var plan);
        if (result != DeployResult.Ok)
            return result;

        var unit = sim.UnitById(unitId)!;
        var tile = sim.Tiles[unit.TileId];
        foreach (var (x, y) in plan!.Cells)
            tile.Grid.Lay(sim.Content.Frame!, x, y);
        for (int i = 0; i < plan.Blocks.Count; i++)
        {
            var (block, x, y, w, h, rotation) = plan.Blocks[i];
            var recipe = block.RecipeId == null ? null : sim.Content.Recipes[block.RecipeId];
            var b = tile.Grid.Add(sim.Content.Buildings[block.DefId], unit.Owner, x, y, w, h, rotation, recipe, block.Hp);
            // The cargo goes back into the core: the tile's funds now (FB-019).
            if (i == unit.CoreBlock)
                b.Stock!.TakeAll(unit.Cargo);
        }
        sim.RemoveUnit(unit);
        return DeployResult.Ok;
    }

    /// <summary>Quarter turns clockwise (y down): east becomes south.</summary>
    internal static (int X, int Y) Turn(int x, int y, int quarters) => (quarters & 3) switch
    {
        0 => (x, y),
        1 => (-y, x),
        2 => (-x, -y),
        _ => (y, -x),
    };

}

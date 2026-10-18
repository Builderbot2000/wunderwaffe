using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

public enum BlueprintResult
{
    Ok,

    /// <summary>No blocks and no frame.</summary>
    Empty,

    /// <summary>Some block or frame cell can't go where it would land; see <see cref="BlueprintPlan.Problems"/>.</summary>
    Blocked,

    /// <summary>Every block fits, but the tile can't pay the total cost.</summary>
    CannotAfford,
}

/// <summary>
/// One block or frame cell of a blueprint that can't be placed. <paramref name="Block"/> indexes the transformed
/// blueprint's blocks, or is -1 for a frame cell. (X, Y) is the tile cell it would cover (its top-left for blocks).
/// </summary>
public readonly record struct BlueprintProblem(int Block, int X, int Y, PlaceResult Result);

/// <summary>
/// Where a blueprint lands and what it costs. Built even when the check fails, so the UI can draw every ghost and mark
/// the bad ones. <see cref="Placed"/> is the blueprint after rotation and flip, still in its own grid; it lands with its
/// top-left on tile cell (<see cref="X"/>, <see cref="Y"/>).
/// </summary>
public sealed record BlueprintPlan(
    Blueprint Placed,
    int X,
    int Y,
    List<PlacementPlan> Blocks,
    List<(int X, int Y)> NewFrames,
    SortedDictionary<string, int> Cost,
    List<BlueprintProblem> Problems);

/// <summary>
/// Recording blueprints from a tile or a unit, turning them, and placing them in one action (FB-009, UD-001). Recording
/// is a read-only query; the library itself lives outside the sim (user data), so a blueprint only enters the sim inside
/// a command.
/// </summary>
public static class Blueprints
{
    // ---- Recording ----

    /// <summary>
    /// Every building lying wholly inside the w x h box at (x, y), and every frame cell in it. Null if the box holds
    /// nothing.
    /// </summary>
    public static Blueprint? FromArea(TileState tile, int x, int y, int w, int h)
    {
        var grid = tile.Grid;
        var blocks = new List<BlueprintBlock>();
        foreach (var b in grid.Buildings)
        {
            if (b.X >= x && b.Y >= y && b.X + b.Width <= x + w && b.Y + b.Height <= y + h)
                blocks.Add(new BlueprintBlock(b.DefId, b.X, b.Y, b.Rotation, b.RecipeId));
        }
        var frames = new List<BlueprintCell>();
        for (int cy = Math.Max(0, y); cy < Math.Min(grid.Height, y + h); cy++)
        {
            for (int cx = Math.Max(0, x); cx < Math.Min(grid.Width, x + w); cx++)
            {
                if (grid.Has(BuildingKind.Frame, cx, cy))
                    frames.Add(new BlueprintCell(cx, cy));
            }
        }
        return Normalise(blocks, frames);
    }

    /// <summary>
    /// The design standing on the frame joined to the core at (x, y): every block with its rotation, and the frame
    /// (UD-001). Only designs that would lift off are recorded, so the library never holds one that can't be built
    /// (UD-003); <paramref name="result"/> says why when it is null.
    /// </summary>
    public static Blueprint? FromUnitCore(Simulation sim, TileState tile, int player, int x, int y, out AssembleResult result)
    {
        result = UnitAssembly.CheckActivate(sim, tile, player, x, y, out var plan);
        if (plan == null)
            return null;
        var blocks = plan.Buildings.Select(b => new BlueprintBlock(b.DefId, b.X, b.Y, b.Rotation, b.RecipeId)).ToList();
        return Normalise(blocks, plan.Cells.Select(c => new BlueprintCell(c.X, c.Y)).ToList());
    }

    /// <summary>A flying unit's design, as it was built (heading ignored).</summary>
    public static Blueprint FromUnit(Unit unit)
    {
        var blocks = unit.Blocks.Select(b => new BlueprintBlock(b.DefId, b.GX, b.GY, b.Rotation, b.RecipeId)).ToList();
        return Normalise(blocks, unit.Cells.Select(c => new BlueprintCell(c.GX, c.GY)).ToList())!;
    }

    /// <summary>Shifts everything so the top-left of what it covers is (0, 0), blocks and frames each in row order.</summary>
    static Blueprint? Normalise(List<BlueprintBlock> blocks, List<BlueprintCell> frames)
    {
        if (blocks.Count == 0 && frames.Count == 0)
            return null;
        int minX = blocks.Select(b => b.X).Concat(frames.Select(f => f.X)).Min();
        int minY = blocks.Select(b => b.Y).Concat(frames.Select(f => f.Y)).Min();
        return new Blueprint(
            blocks.Select(b => b with { X = b.X - minX, Y = b.Y - minY }).OrderBy(b => b.Y).ThenBy(b => b.X).ToArray(),
            frames.Select(f => new BlueprintCell(f.X - minX, f.Y - minY)).Distinct().OrderBy(f => f.Y).ThenBy(f => f.X).ToArray());
    }

    // ---- Transform ----

    /// <summary>
    /// The blueprint mirrored left-right (if <paramref name="flip"/>), then turned <paramref name="quarters"/> quarter
    /// turns clockwise, back at (0, 0). Blocks keep pointing the way they did relative to the rest: a belt running east
    /// runs west after a flip.
    /// </summary>
    public static Blueprint Transform(IReadOnlyDictionary<string, BuildingDef> defs, Blueprint bp, int quarters, bool flip)
    {
        quarters &= 3;
        if (quarters == 0 && !flip)
            return bp;
        (int X, int Y) Map(int x, int y) => UnitAssembly.Turn(flip ? -x : x, y, quarters);

        var blocks = new List<BlueprintBlock>(bp.Blocks.Length);
        foreach (var b in bp.Blocks)
        {
            var (w, h) = defs.TryGetValue(b.DefId, out var def) ? def.Footprint(b.Rotation) : (1, 1);
            var (ax, ay) = Map(b.X, b.Y);
            var (cx, cy) = Map(b.X + w - 1, b.Y + h - 1);
            int r = flip && (b.Rotation & 1) == 0 ? b.Rotation ^ 2 : b.Rotation;
            blocks.Add(b with { X = Math.Min(ax, cx), Y = Math.Min(ay, cy), Rotation = (r + quarters) & 3 });
        }
        var frames = bp.Frames.Select(f => Map(f.X, f.Y)).Select(c => new BlueprintCell(c.X, c.Y)).ToList();
        return Normalise(blocks, frames) ?? bp;
    }

    // ---- Placement ----

    /// <summary>
    /// Whether the blueprint, turned and flipped, can be placed with its top-left at tile cell (x, y): every block
    /// passes <see cref="Placement.Check"/> (cost aside) without overlapping another, every frame cell passes
    /// <see cref="UnitAssembly.CheckFrame"/> or is framed already, and the player's cores on the tile can pay the total
    /// (<see cref="Funds.CanPay"/>). All or nothing.
    /// </summary>
    public static BlueprintResult Check(
        Simulation sim, TileState tile, int player, Blueprint blueprint, int x, int y, int rotation, bool flip, out BlueprintPlan plan)
    {
        var defs = sim.Content.Buildings;
        var bp = Transform(defs, blueprint, rotation, flip);
        var blocks = new List<PlacementPlan>();
        var newFrames = new List<(int X, int Y)>();
        var cost = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var problems = new List<BlueprintProblem>();
        plan = new BlueprintPlan(bp, x, y, blocks, newFrames, cost, problems);
        if (bp.Blocks.Length == 0 && bp.Frames.Length == 0)
            return BlueprintResult.Empty;

        var taken = new HashSet<(int, int)>();
        for (int i = 0; i < bp.Blocks.Length; i++)
        {
            var b = bp.Blocks[i];
            int bx = x + b.X, by = y + b.Y;
            var result = CheckBlock(sim, tile, player, b, bx, by, out var placed, ignoreCost: true);
            if (result == PlaceResult.Ok && !Claim(taken, placed))
                result = PlaceResult.Blocked;
            if (result != PlaceResult.Ok)
            {
                problems.Add(new BlueprintProblem(i, bx, by, result));
                continue;
            }
            blocks.Add(placed);
            Add(cost, placed.Def.Cost);
        }

        foreach (var f in bp.Frames)
        {
            int fx = x + f.X, fy = y + f.Y;
            if (tile.Grid.InBounds(fx, fy) && tile.Grid.Has(BuildingKind.Frame, fx, fy))
                continue;
            var result = UnitAssembly.CheckFrame(sim, tile, player, fx, fy, ignoreCost: true);
            if (result != PlaceResult.Ok)
            {
                problems.Add(new BlueprintProblem(-1, fx, fy, result));
                continue;
            }
            newFrames.Add((fx, fy));
            Add(cost, sim.Content.Frame!.Cost);
        }

        if (problems.Count > 0)
            return BlueprintResult.Blocked;
        return Funds.CanPay(tile, player, cost) ? BlueprintResult.Ok : BlueprintResult.CannotAfford;
    }

    /// <summary>
    /// One blueprint block at tile cell (x, y), through the ordinary placement rules. An extractor's recipe is left to
    /// the deposit under it, so a recorded drill mines whatever it lands on.
    /// </summary>
    internal static PlaceResult CheckBlock(
        Simulation sim, TileState tile, int player, BlueprintBlock b, int x, int y, out PlacementPlan plan, bool ignoreCost)
    {
        string? recipe = b.RecipeId is { } id && sim.Content.Recipes.TryGetValue(id, out var r) && r.Inputs.Count == 0 ? null : b.RecipeId;
        return Placement.Check(sim, tile, player, b.DefId, x, y, b.Rotation, recipe, out plan, ignoreCost);
    }

    static bool Claim(HashSet<(int, int)> taken, PlacementPlan p)
    {
        for (int cy = p.Y; cy < p.Y + p.Height; cy++)
        {
            for (int cx = p.X; cx < p.X + p.Width; cx++)
            {
                if (taken.Contains((cx, cy)))
                    return false;
            }
        }
        for (int cy = p.Y; cy < p.Y + p.Height; cy++)
        {
            for (int cx = p.X; cx < p.X + p.Width; cx++)
                taken.Add((cx, cy));
        }
        return true;
    }

    internal static void Add(SortedDictionary<string, int> total, IReadOnlyDictionary<string, int> items)
    {
        foreach (var (id, n) in items)
            total[id] = total.GetValueOrDefault(id) + n;
    }

    internal static BlueprintResult Place(
        Simulation sim, TileState tile, int player, Blueprint blueprint, int x, int y, int rotation, bool flip)
    {
        var result = Check(sim, tile, player, blueprint, x, y, rotation, flip, out var plan);
        if (result != BlueprintResult.Ok)
            return result;

        Funds.TryPay(tile, player, plan.Cost);
        Build(sim, tile, player, plan);
        return BlueprintResult.Ok;
    }

    /// <summary>Lays a checked plan's frame and buildings, unpaid. Returns the buildings in blueprint order.</summary>
    static List<PlacedBuilding> Build(Simulation sim, TileState tile, int player, BlueprintPlan plan)
    {
        foreach (var (fx, fy) in plan.NewFrames)
            tile.Grid.Lay(sim.Content.Frame!, fx, fy);
        var placed = new List<PlacedBuilding>(plan.Blocks.Count);
        foreach (var p in plan.Blocks)
        {
            var recipe = p.RecipeId == null ? null : sim.Content.Recipes[p.RecipeId];
            placed.Add(tile.Grid.Add(p.Def, player, p.X, p.Y, p.Width, p.Height, p.Rotation, recipe));
        }
        return placed;
    }

    /// <summary>
    /// Match setup (FB-021): lays a prefab with its top-left at (x, y) for free, its first core holding the prefab's
    /// items. The spot must already be free and buildable; anything the placement rules refuse there is a content or
    /// setup error and throws.
    /// </summary>
    internal static void Stamp(Simulation sim, TileState tile, int player, Content.PrefabDef prefab, int x, int y)
    {
        var result = Check(sim, tile, player, prefab.Blueprint, x, y, 0, false, out var plan);
        if (result is not (BlueprintResult.Ok or BlueprintResult.CannotAfford))
            throw new InvalidOperationException(
                $"prefab '{prefab.Id}' doesn't fit at ({x}, {y}) on tile {tile.Id}: {string.Join(", ", plan.Problems.Select(p => p.Result))}");
        var placed = Build(sim, tile, player, plan);
        if (prefab.Items is { Count: > 0 } items)
            placed.First(b => b.Kind == BuildingKind.Core).Stock!.Add(items);
    }
}

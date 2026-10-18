using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

public enum ProjectResult
{
    Ok,

    /// <summary>The factory isn't the player's.</summary>
    NotOwner,

    /// <summary>No unit factory covers the cell.</summary>
    NoFactory,

    /// <summary>The blueprint is empty.</summary>
    Empty,

    /// <summary>Bigger than the factory's block cap (UD-006, WW-002): boosters raise it.</summary>
    TooLarge,

    /// <summary>Some cell lies outside the factory's radius.</summary>
    OutOfRange,

    /// <summary>Some block or frame cell can't go where it would land.</summary>
    Blocked,

    /// <summary>It has tier-3+ or weapon blocks, and the factory has no Precision Jig (FB-016, OQ-25).</summary>
    NeedsPrecision,
}

/// <summary>What a factory is doing, for its panel (UD-006).</summary>
public enum FactoryStatus
{
    /// <summary>No blueprint projected.</summary>
    Idle,

    /// <summary>Waiting for the next block's items to arrive by belt.</summary>
    Waiting,

    /// <summary>Placing a block.</summary>
    Building,

    /// <summary>The next block's cell is taken, for instance by the last building blueprint it finished.</summary>
    Blocked,

    /// <summary>
    /// The finished unit wouldn't lift off (OQ-44); <see cref="FactoryState.LiftOff"/> says why. It takes no items and
    /// does nothing until resumed (<see cref="UnitFactory.Resume"/>), which tries the lift-off again.
    /// </summary>
    Stopped,

    /// <summary>Manual mode: a copy is done and the factory waits for the next trigger (<see cref="UnitFactory.Trigger"/>).</summary>
    AwaitingTrigger,
}

/// <summary>A factory's production setting (UD-006, OQ-43).</summary>
public enum FactoryMode
{
    /// <summary>Starts the next copy as soon as one is done.</summary>
    Continuous,

    /// <summary>Builds one copy per trigger; projecting a blueprint counts as the first.</summary>
    Manual,
}

/// <summary>Why a factory control command (mode, trigger, resume) can't apply.</summary>
public enum FactoryControlResult
{
    Ok,
    NoFactory,
    NotOwner,

    /// <summary>Trigger: the factory isn't waiting for one (not manual, or still building).</summary>
    NotAwaiting,

    /// <summary>Resume: the factory isn't stopped.</summary>
    NotStopped,
}

/// <summary>Assembly stages, in build order (WW-004).</summary>
public enum BuildStage
{
    Frame,
    Systems,
    Armament,
}

/// <summary>
/// One thing a factory places: a frame cell (<paramref name="Block"/> = -1) or a block of the projected blueprint.
/// (X, Y) is the tile cell (a block's top-left).
/// </summary>
public readonly record struct FactoryStep(BuildStage Stage, int Block, int X, int Y, IReadOnlyDictionary<string, int> Cost, int Ticks);

/// <summary>A unit factory's runtime state (UD-006).</summary>
public sealed class FactoryState
{
    /// <summary>The projected blueprint, turned and flipped, with its top-left at tile cell (<see cref="OX"/>, <see cref="OY"/>).</summary>
    public Blueprint? Projected { get; internal set; }
    public int OX { get; internal set; }
    public int OY { get; internal set; }

    /// <summary>Everything to place, frame first, then systems, then armament.</summary>
    public IReadOnlyList<FactoryStep> Steps { get; internal set; } = [];

    /// <summary>Index into <see cref="Steps"/> of the block being placed or waited for.</summary>
    public int Next { get; internal set; }

    /// <summary>The current step's items are paid and it is being placed.</summary>
    public bool Working { get; internal set; }

    /// <summary>Work done on the current step, in hundredths of a tick (boosters add build speed in percent).</summary>
    public int Progress { get; internal set; }

    /// <summary>Items delivered and not yet spent.</summary>
    public SortedDictionary<string, int> Buffer { get; } = new(StringComparer.Ordinal);

    public FactoryStatus Status { get; internal set; }

    /// <summary>Manual or continuous production (OQ-43), set by <c>set_factory_mode</c>.</summary>
    public FactoryMode Mode { get; internal set; }

    /// <summary>Why the last lift-off failed, while <see cref="Status"/> is <see cref="FactoryStatus.Stopped"/>.</summary>
    public AssembleResult LiftOff { get; internal set; }

    /// <summary>Copies finished: units lifted off, or building blueprints completed.</summary>
    public int Built { get; internal set; }

    public FactoryStep? Current => Next < Steps.Count ? Steps[Next] : null;
}

/// <summary>
/// Unit factories (UD-006): a factory projects a library blueprint within its radius and builds it block by block as
/// belts deliver each block's items, frame first, then systems, then armament (WW-004). When the last block of a unit
/// is in place it lifts the unit off and starts the next copy, or in manual mode waits for a trigger (OQ-43); a failed
/// lift-off stops it until resumed (OQ-44). A building blueprint stays where it was built and blocks
/// the factory until it is removed. A factory's radius and block cap are its def's plus its boosters' (FB-016), so a
/// megafactory is just a well-boosted factory (WW-002).
/// </summary>
public static class UnitFactory
{
    public static int Radius(Simulation sim, TileState tile, PlacedBuilding factory) =>
        sim.Content.Buildings[factory.DefId].Radius + BoosterBonus(sim, tile, factory, "radius");

    public static int BlockCap(Simulation sim, TileState tile, PlacedBuilding factory) =>
        sim.Content.Buildings[factory.DefId].BlockCap + BoosterBonus(sim, tile, factory, "block_cap");

    /// <summary>Build speed in percent of normal: boosted, then scaled by the share of its power it gets (FB-005).</summary>
    public static int BuildSpeed(Simulation sim, TileState tile, PlacedBuilding factory) =>
        (100 + BoosterBonus(sim, tile, factory, "build_speed")) * PowerNet.Satisfaction(tile, factory) / 1000;

    static int BoosterBonus(Simulation sim, TileState tile, PlacedBuilding host, string stat) =>
        Boosters.Bonus(sim, tile, host, stat);

    /// <summary>
    /// A block needs a Precision Jig on the factory (FB-016, OQ-25): a weapon, or one whose cost includes an item of
    /// tier 3 or more.
    /// </summary>
    public static bool NeedsPrecision(ContentDb content, BuildingDef def) =>
        IsArmament(def) || def.Cost.Keys.Any(id => content.Items[id].Tier >= 3);

    /// <summary>Weapon blocks (CB-001) are the armament stage, built last.</summary>
    static bool IsArmament(BuildingDef def) => def.Kind == BuildingKind.Weapon;

    /// <summary>
    /// What a blueprint counts against the block cap: frame cells for a unit (its size, as for lift-off), buildings for a
    /// building blueprint.
    /// </summary>
    public static int SizeOf(IReadOnlyDictionary<string, BuildingDef> defs, Blueprint bp) =>
        bp.IsUnit(defs) ? bp.Frames.Length : bp.Blocks.Length;

    /// <summary>
    /// Chebyshev distance, in cells, from a cell to the nearest cell of the factory: 1 for the ring around it.
    /// </summary>
    public static int Distance(PlacedBuilding factory, int x, int y)
    {
        int dx = Math.Max(0, Math.Max(factory.X - x, x - (factory.X + factory.Width - 1)));
        int dy = Math.Max(0, Math.Max(factory.Y - y, y - (factory.Y + factory.Height - 1)));
        return Math.Max(dx, dy);
    }

    /// <summary>
    /// Whether the factory covering (x, y) can project <paramref name="blueprint"/> with its top-left at (ox, oy): within
    /// the block cap, every cell within the radius, and every block and frame cell placeable now (cost aside; the factory
    /// is paid by belt). Frame may not go under buildings here, or the lift-off would carry them. <paramref name="plan"/>
    /// carries the problem cells, for the ghost preview.
    /// </summary>
    public static ProjectResult CheckProject(
        Simulation sim, TileState tile, int player, int x, int y, Blueprint? blueprint, int ox, int oy, int rotation, bool flip,
        out BlueprintPlan? plan)
    {
        plan = null;
        if (tile.Grid.BuildingAt(x, y) is not { Kind: BuildingKind.UnitFactory } factory)
            return ProjectResult.NoFactory;
        if (factory.Owner != player)
            return ProjectResult.NotOwner;
        if (blueprint == null)
            return ProjectResult.Ok;

        var result = Blueprints.Check(sim, tile, player, blueprint, ox, oy, rotation, flip, out var p);
        plan = p;
        if (result == BlueprintResult.Empty)
            return ProjectResult.Empty;
        var defs = sim.Content.Buildings;
        if (SizeOf(defs, p.Placed) > BlockCap(sim, tile, factory))
            return ProjectResult.TooLarge;
        if (BoosterBonus(sim, tile, factory, "precision") == 0
            && p.Placed.Blocks.Any(b => NeedsPrecision(sim.Content, defs[b.DefId])))
            return ProjectResult.NeedsPrecision;

        int radius = Radius(sim, tile, factory);
        bool outside = false;
        foreach (var (cx, cy) in p.Placed.Cells(defs).Distinct())
        {
            if (Distance(factory, ox + cx, oy + cy) > radius)
            {
                p.Problems.Add(new BlueprintProblem(-1, ox + cx, oy + cy, PlaceResult.OutOfBounds));
                outside = true;
            }
        }
        if (outside)
            return ProjectResult.OutOfRange;

        foreach (var f in p.Placed.Frames)
        {
            if (tile.Grid.BuildingAt(ox + f.X, oy + f.Y) != null)
                p.Problems.Add(new BlueprintProblem(-1, ox + f.X, oy + f.Y, PlaceResult.Blocked));
        }
        return p.Problems.Count > 0 ? ProjectResult.Blocked : ProjectResult.Ok;
    }

    internal static ProjectResult Project(
        Simulation sim, TileState tile, int player, int x, int y, Blueprint? blueprint, int ox, int oy, int rotation, bool flip)
    {
        var result = CheckProject(sim, tile, player, x, y, blueprint, ox, oy, rotation, flip, out var plan);
        if (result != ProjectResult.Ok)
            return result;

        var factory = tile.Grid.BuildingAt(x, y)!;
        var f = factory.Factory!;
        // Items already paid for a block in progress go back to the buffer.
        if (f is { Working: true, Current: { } current })
            Blueprints.Add(f.Buffer, current.Cost);
        f.Working = false;
        f.Progress = 0;
        f.Next = 0;
        f.Projected = plan?.Placed;
        f.OX = ox;
        f.OY = oy;
        f.Steps = plan == null ? [] : StepsOf(sim.Content, plan.Placed, ox, oy);
        f.Status = plan == null ? FactoryStatus.Idle : FactoryStatus.Waiting;
        return ProjectResult.Ok;
    }

    /// <summary>The build order for a projection; snapshots rebuild <see cref="FactoryState.Steps"/> with it.</summary>
    internal static List<FactoryStep> StepsOf(ContentDb content, Blueprint bp, int ox, int oy)
    {
        var steps = bp.Frames
            .Select(c => new FactoryStep(BuildStage.Frame, -1, ox + c.X, oy + c.Y, Balance.FrameCost, UnitStats.BuildTicksOf(Balance.FrameCost)))
            .ToList();
        foreach (var stage in new[] { BuildStage.Systems, BuildStage.Armament })
        {
            for (int i = 0; i < bp.Blocks.Length; i++)
            {
                var b = bp.Blocks[i];
                var def = content.Buildings[b.DefId];
                if ((IsArmament(def) ? BuildStage.Armament : BuildStage.Systems) == stage)
                    steps.Add(new FactoryStep(stage, i, ox + b.X, oy + b.Y, def.Cost, UnitStats.BuildTicksOf(def.Cost)));
            }
        }
        return steps;
    }

    /// <summary>Steps done and in total for each stage, in stage order (WW-004).</summary>
    public static IReadOnlyList<(BuildStage Stage, int Done, int Total)> StageProgress(FactoryState f) =>
        [.. Enum.GetValues<BuildStage>().Select(s => (s,
            f.Steps.Take(f.Next).Count(st => st.Stage == s),
            f.Steps.Count(st => st.Stage == s)))];

    /// <summary>Items the current copy still needs delivered: what's left to place, less the buffer.</summary>
    public static SortedDictionary<string, int> Missing(FactoryState f)
    {
        var need = Remaining(f);
        foreach (var (id, n) in f.Buffer)
        {
            if (need.TryGetValue(id, out int left))
            {
                if (left <= n)
                    need.Remove(id);
                else
                    need[id] = left - n;
            }
        }
        return need;
    }

    /// <summary>Items still to pay for this copy: every step from the current one on, unless it is already paid.</summary>
    static SortedDictionary<string, int> Remaining(FactoryState f)
    {
        var need = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = f.Working ? f.Next + 1 : f.Next; i < f.Steps.Count; i++)
            Blueprints.Add(need, f.Steps[i].Cost);
        return need;
    }

    /// <summary>A belt offers an item: the factory takes it if what is left to build still needs more of it.</summary>
    internal static bool TryInsert(PlacedBuilding factory, string item)
    {
        var f = factory.Factory!;
        if (f.Projected == null || !Runs(f))
            return false;
        int need = 0;
        for (int i = f.Working ? f.Next + 1 : f.Next; i < f.Steps.Count; i++)
            need += f.Steps[i].Cost.GetValueOrDefault(item);
        int have = f.Buffer.GetValueOrDefault(item);
        if (have >= need)
            return false;
        f.Buffer[item] = have + 1;
        return true;
    }

    /// <summary>A removed factory's items, the block in progress included.</summary>
    internal static void Drain(FactoryState f, Core.Inventory into)
    {
        foreach (var (id, n) in f.Buffer)
            into.Add(id, n);
        if (f is { Working: true, Current: { } current })
            into.Add(current.Cost);
    }

    /// <summary>Every factory on every tile, in tile then building id order. Runs right after the tile entities.</summary>
    internal static void Step(Simulation sim)
    {
        foreach (var tile in sim.Tiles)
        {
            var factories = tile.Grid.Factories;
            // Lift-off and placement change the grid but never the factory list, so a plain loop is safe.
            for (int i = 0; i < factories.Count; i++)
                StepOne(sim, tile, factories[i]);
        }
    }

    static void StepOne(Simulation sim, TileState tile, PlacedBuilding factory)
    {
        var f = factory.Factory!;
        if (f.Projected is not { } bp || !Runs(f))
            return;

        if (f.Current is not { } step)
        {
            Finish(sim, tile, factory, bp);
            return;
        }

        if (!f.Working)
        {
            if (step.Block < 0 && tile.Grid.HasFrame(step.X, step.Y))
            {
                f.Next++; // already framed: nothing to pay or place
                return;
            }
            if (!CanPlace(sim, tile, factory, bp, step))
            {
                f.Status = FactoryStatus.Blocked;
                return;
            }
            if (!Has(f.Buffer, step.Cost))
            {
                f.Status = FactoryStatus.Waiting;
                return;
            }
            foreach (var (id, n) in step.Cost)
            {
                int left = f.Buffer[id] - n;
                if (left == 0)
                    f.Buffer.Remove(id);
                else
                    f.Buffer[id] = left;
            }
            f.Working = true;
            f.Progress = 0;
        }

        f.Status = FactoryStatus.Building;
        f.Progress = Math.Min(f.Progress + BuildSpeed(sim, tile, factory), step.Ticks * 100);
        if (f.Progress < step.Ticks * 100)
            return;
        // Paid and done: place it as soon as its cell is clear.
        if (!CanPlace(sim, tile, factory, bp, step))
        {
            f.Status = FactoryStatus.Blocked;
            return;
        }
        if (step.Block < 0)
        {
            tile.Grid.SetFrame(step.X, step.Y, true);
        }
        else
        {
            var b = bp.Blocks[step.Block];
            Blueprints.CheckBlock(sim, tile, factory.Owner, b, step.X, step.Y, out var p, ignoreCost: true);
            var recipe = p.RecipeId == null ? null : sim.Content.Recipes[p.RecipeId];
            tile.Grid.Add(p.Def, factory.Owner, p.X, p.Y, p.Width, p.Height, p.Rotation, recipe);
        }
        f.Working = false;
        f.Progress = 0;
        f.Next++;
    }

    static bool CanPlace(Simulation sim, TileState tile, PlacedBuilding factory, Blueprint bp, FactoryStep step) =>
        step.Block < 0
            ? tile.Grid.HasFrame(step.X, step.Y)
              || (tile.Grid.BuildingAt(step.X, step.Y) == null && UnitAssembly.CheckFrame(sim, tile, factory.Owner, step.X, step.Y, ignoreCost: true) == PlaceResult.Ok)
            : Blueprints.CheckBlock(sim, tile, factory.Owner, bp.Blocks[step.Block], step.X, step.Y, out _, ignoreCost: true) == PlaceResult.Ok;

    static bool Has(SortedDictionary<string, int> buffer, IReadOnlyDictionary<string, int> cost) =>
        cost.All(kv => buffer.GetValueOrDefault(kv.Key) >= kv.Value);

    /// <summary>Neither stopped nor waiting for a trigger: it takes items and builds.</summary>
    static bool Runs(FactoryState f) => f.Status is not (FactoryStatus.Stopped or FactoryStatus.AwaitingTrigger);

    /// <summary>
    /// Every block is placed: lift a unit off, or leave a building blueprint standing, then start the next copy
    /// (continuous) or wait for a trigger (manual). A failed lift-off stops the factory and raises
    /// <see cref="Core.GameEventKind.FactoryStopped"/> (item id = the <see cref="AssembleResult"/>) until it is resumed (OQ-44).
    /// </summary>
    static void Finish(Simulation sim, TileState tile, PlacedBuilding factory, Blueprint bp)
    {
        var f = factory.Factory!;
        var defs = sim.Content.Buildings;
        int core = Array.FindIndex(bp.Blocks, b => defs[b.DefId].Kind == BuildingKind.Core);
        if (core >= 0)
        {
            var b = bp.Blocks[core];
            f.LiftOff = UnitAssembly.Launch(sim, tile, factory.Owner, f.OX + b.X, f.OY + b.Y, BlockCap(sim, tile, factory), factory, out _);
            if (f.LiftOff != AssembleResult.Ok)
            {
                f.Status = FactoryStatus.Stopped;
                sim.Raise(Core.GameEventKind.FactoryStopped, factory.Owner, tile.Id, itemId: f.LiftOff.ToString());
                return;
            }
        }
        f.Built++;
        f.Next = 0;
        f.Status = f.Mode == FactoryMode.Manual ? FactoryStatus.AwaitingTrigger : FactoryStatus.Waiting;
    }

    /// <summary>The player's unit factory covering (x, y), or why not.</summary>
    static FactoryControlResult Own(TileState tile, int player, int x, int y, out FactoryState? f)
    {
        f = null;
        if (tile.Grid.BuildingAt(x, y) is not { Kind: BuildingKind.UnitFactory } factory)
            return FactoryControlResult.NoFactory;
        if (factory.Owner != player)
            return FactoryControlResult.NotOwner;
        f = factory.Factory!;
        return FactoryControlResult.Ok;
    }

    /// <summary>Whether the player may set the production setting of the factory covering (x, y): any time (UD-006).</summary>
    public static FactoryControlResult CheckSetMode(TileState tile, int player, int x, int y) => Own(tile, player, x, y, out _);

    /// <summary>
    /// Sets manual or continuous production. A manual factory waiting for a trigger switched to continuous starts the
    /// next copy; a copy in progress always finishes.
    /// </summary>
    internal static void SetMode(TileState tile, int player, int x, int y, FactoryMode mode)
    {
        if (Own(tile, player, x, y, out var f) != FactoryControlResult.Ok)
            return;
        f!.Mode = mode;
        if (mode == FactoryMode.Continuous && f.Status == FactoryStatus.AwaitingTrigger)
            f.Status = FactoryStatus.Waiting;
    }

    /// <summary>Whether the factory covering (x, y) is the player's and waiting for a manual trigger.</summary>
    public static FactoryControlResult CheckTrigger(TileState tile, int player, int x, int y) =>
        Own(tile, player, x, y, out var f) is not FactoryControlResult.Ok and var r ? r
        : f!.Status == FactoryStatus.AwaitingTrigger ? FactoryControlResult.Ok : FactoryControlResult.NotAwaiting;

    /// <summary>Manual mode: starts one more copy of the projected blueprint.</summary>
    internal static void Trigger(TileState tile, int player, int x, int y)
    {
        if (CheckTrigger(tile, player, x, y) == FactoryControlResult.Ok)
            tile.Grid.BuildingAt(x, y)!.Factory!.Status = FactoryStatus.Waiting;
    }

    /// <summary>Whether the factory covering (x, y) is the player's and stopped after a failed lift-off.</summary>
    public static FactoryControlResult CheckResume(TileState tile, int player, int x, int y) =>
        Own(tile, player, x, y, out var f) is not FactoryControlResult.Ok and var r ? r
        : f!.Status == FactoryStatus.Stopped ? FactoryControlResult.Ok : FactoryControlResult.NotStopped;

    /// <summary>Clears a stop: the factory tries the lift-off again on its next step, and stops again if it still fails.</summary>
    internal static void Resume(TileState tile, int player, int x, int y)
    {
        if (CheckResume(tile, player, x, y) != FactoryControlResult.Ok)
            return;
        var f = tile.Grid.BuildingAt(x, y)!.Factory!;
        f.Status = FactoryStatus.Waiting;
        f.LiftOff = AssembleResult.Ok;
    }
}

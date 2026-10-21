using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// What one think observes (MP-006), built fresh from <see cref="GameQuery"/> each time: the seat's tiles, their
/// buildings and machine outlets, the tile goals of the strategic layer, the economy's <see cref="AiPlan"/> and the
/// proposal helpers modules share. It names no item, recipe or building (MP-007): buildings come by role
/// (<see cref="Roles"/>). Nothing in it outlives the think except the layout cache, which only remembers what the state gives.
/// </summary>
public sealed class AiContext
{
    /// <summary>Demand weight per extra line of an item into the base core (<see cref="Want"/>).</summary>
    public const int WeightPerLine = 40;

    /// <summary>Most lines of one item into the base core before the seat's machines all stand (<see cref="Want"/>).</summary>
    public const int MaxLines = 2;

    readonly PlanCache _plans;
    readonly Dictionary<int, TileInfo> _tiles = [];
    AiPlan? _plan;
    bool? _advanced;

    internal AiContext(GameQuery q, PlanCache plans, AiDesign? design)
    {
        Q = q;
        _plans = plans;
        Design = design;
        Player = q.Player;
        Owned = q.OwnedTiles();
        Tiles = q.Tiles();
        foreach (int t in Owned)
        {
            var all = q.Buildings(t);
            Buildings[t] = all.ToDictionary(b => b.Id);
            Outlets[t] = all.Where(b => b.Owner == Player && (IsProducer(b) || Kind(b) == BuildingKind.ImportPort))
                .ToDictionary(b => b.Id, b => q.OutletOf(t, b.Id));
        }
        int start = q.StartTile();
        Home = Owned.Contains(start) ? start : Owned.FirstOrDefault(t => Cores(t).Any(), -1);
        Units = q.Units(Player);
        CoreDef = Roles.Core(q.Content, q.Can)!;
        ExportPort = Roles.Cheapest(q.Content, BuildingKind.ExportPort, q.Can);
        ImportPort = Roles.Cheapest(q.Content, BuildingKind.ImportPort, q.Can);
    }

    /// <summary>A context for <paramref name="player"/> with the strategic layer run: what a think sees, for tests and tools.</summary>
    public static AiContext Observe(Simulation sim, int player)
    {
        var q = new GameQuery(sim, player);
        var c = new AiContext(q, new PlanCache(), AiDesigns.Pick(q));
        if (c.Home >= 0)
            c.Influence = InfluenceMap.Build(c);
        return c;
    }

    public GameQuery Q { get; }
    public int Player { get; }
    public IReadOnlyList<int> Owned { get; }
    public IReadOnlyList<TileSummary> Tiles { get; }

    /// <summary>The start tile while the seat holds it, else its first tile with a core of its own; -1 if it has none.</summary>
    public int Home { get; }

    public IReadOnlyList<UnitInfo> Units { get; }
    public Dictionary<int, Dictionary<int, BuildingView>> Buildings { get; } = [];

    /// <summary>Per tile, the seat's machines with outputs and its import ports, and the building each one's output ends in (-1: none).</summary>
    public Dictionary<int, Dictionary<int, int>> Outlets { get; } = [];

    /// <summary>The core the seat builds (<see cref="ContentDb.DefaultCore"/>).</summary>
    public BuildingDef CoreDef { get; }

    /// <summary>The cheapest export and import ports, or null if the content has none.</summary>
    public BuildingDef? ExportPort { get; }
    public BuildingDef? ImportPort { get; }

    /// <summary>The unit design picked from the prefabs at <see cref="AiPlayer.Init"/>; null if there is none.</summary>
    public AiDesign? Design { get; }

    /// <summary>The strategic layer's map, by tile id. Set before the modules are asked.</summary>
    public IReadOnlyList<TileInfluence> Influence { get; internal set; } = [];

    public TileGoal GoalOf(int tile) => tile >= 0 && tile < Influence.Count ? Influence[tile].Goal : TileGoal.None;

    /// <summary>Tiles with <paramref name="goal"/>, highest priority first, then by id.</summary>
    public IEnumerable<TileInfluence> WithGoal(TileGoal goal) =>
        Influence.Where(i => i.Goal == goal).OrderByDescending(i => i.Priority).ThenBy(i => i.Tile);

    public BuildingKind Kind(BuildingView b) => Q.Content.Buildings[b.DefId].Kind;

    /// <summary>A machine running a recipe that makes something (not a generator burning fuel).</summary>
    public bool IsProducer(BuildingView b) => b.RecipeId is { } r && Q.Content.Recipes[r].Outputs.Count > 0;

    /// <summary>True if the building stands on frame: a core there is a unit waiting to lift off.</summary>
    public bool OnFrame(int tile, BuildingView b) => Q.Cells(tile, b.X, b.Y, 1, 1)[0].Frame;

    /// <summary>The seat's cores on the tile, in id order (the order they pay in, FB-019).</summary>
    public IEnumerable<BuildingView> Cores(int tile) =>
        Buildings[tile].Values.Where(b => b.Owner == Player && Kind(b) == BuildingKind.Core).OrderBy(b => b.Id);

    /// <summary>The core the tile's lines feed: the first of the seat's cores not on frame, so it stays when units lift off.</summary>
    public BuildingView? BaseCore(int tile) => Cores(tile).FirstOrDefault(b => !OnFrame(tile, b));

    /// <summary>Machines on the tile making <paramref name="recipe"/> whose output ends in <paramref name="sink"/>.</summary>
    public int Lines(int tile, string recipe, int sink) =>
        Outlets[tile].Count(kv => Buildings[tile][kv.Key].RecipeId == recipe && kv.Value == sink);

    /// <summary>The seat's machine on the tile making <paramref name="recipe"/> whose output ends in <paramref name="sink"/>.</summary>
    public BuildingView? MachineInto(int tile, string recipe, int sink) =>
        Outlets[tile].Where(kv => kv.Value == sink && Buildings[tile][kv.Key].RecipeId == recipe).Select(kv => Buildings[tile][kv.Key]).FirstOrDefault();

    /// <summary>True if one of the seat's import ports on <paramref name="tile"/>, paired with <paramref name="from"/>, feeds <paramref name="sink"/>.</summary>
    public bool ImportsInto(int tile, int from, int sink) =>
        Outlets[tile].Any(kv => kv.Value == sink && Buildings[tile][kv.Key] is var b && Kind(b) == BuildingKind.ImportPort && b.Partner == from);

    // ---- The economy plan (MP-007) ----

    /// <summary>
    /// The economy plan (<see cref="AiPlan"/>): demand from the design's cost and the current goal's item, chained
    /// through the recipes, with deposits on home and on owned neighbours of home counted as minable.
    /// </summary>
    public AiPlan Plan => _plan ??= AiPlan.Build(Q.Content, Design?.Cost ?? new Dictionary<string, int>(),
        Q.Goal() is { Type: "produce", Item: { } item } ? [item] : [], Available, Q.Can);

    /// <summary>True if home or an owned neighbour of home holds a deposit of <paramref name="item"/>.</summary>
    public bool Available(string item)
    {
        if (!_available.TryGetValue(item, out bool yes))
            _available[item] = yes = Home >= 0 && (HasDeposit(Home, item) || ImportTile(item) >= 0);
        return yes;
    }

    readonly Dictionary<string, bool> _available = [];

    /// <summary>
    /// The owned neighbour of home that ships <paramref name="item"/> from its deposits: one already linked to home by an
    /// import port first, then by id; -1 if none.
    /// </summary>
    public int ImportTile(string item)
    {
        var linked = Buildings[Home].Values.Where(b => b.Owner == Player && Kind(b) == BuildingKind.ImportPort).Select(b => b.Partner).ToHashSet();
        return Owned.Where(t => t != Home && Tiles[Home].Neighbors.Contains(t) && HasDeposit(t, item))
            .OrderBy(t => linked.Contains(t) ? 0 : 1).ThenBy(t => t).DefaultIfEmpty(-1).First();
    }

    /// <summary>A wanted item made at home by one line straight into the base core: the economy module's part.</summary>
    public bool IsHomeLine(Demand d) => d.Chain.IsLinear && HasDeposit(Home, d.Chain.Deposit);

    /// <summary>
    /// Lines of <paramref name="d"/> wanted into the base core: one plus one per <see cref="WeightPerLine"/> of demand, at
    /// most <see cref="MaxLines"/>, and one more once <see cref="Advanced"/>.
    /// </summary>
    public int Want(Demand d) => Math.Clamp(1 + d.Weight / WeightPerLine, 1, MaxLines) + (Advanced ? 1 : 0);

    /// <summary>True once every wanted item that needs a machine of several inputs has one feeding the base core at home.</summary>
    public bool Advanced => _advanced ??= BaseCore(Home) is { } core
        && Plan.Items.Where(d => !d.Chain.IsLinear).All(d => Lines(Home, d.Chain.Recipe.Id, core.Id) > 0);

    // ---- Tile facts ----

    /// <summary>The tile's full view, fetched once per think.</summary>
    public TileInfo Tile(int tile)
    {
        if (!_tiles.TryGetValue(tile, out var view))
            _tiles[tile] = view = Q.Tile(tile)!;
        return view;
    }

    /// <summary>The tile's deposit cells per item and its origin, cached: <see cref="Tile"/> scans the whole grid.</summary>
    public (IReadOnlyDictionary<string, int> Deposits, int OriginX, int OriginY) Facts(int tile) =>
        _plans.Get(Q, tile, "facts", () => { var t = Tile(tile); return (t.Deposits, t.OriginX, t.OriginY); });

    public bool HasDeposit(int tile, string item) => Facts(tile).Deposits.ContainsKey(item);

    /// <summary><see cref="InfluenceMap.Value"/> of the tile, worked out again only when the tile changes.</summary>
    public int Value(int tile) => _plans.Get(Q, tile, "value", () => InfluenceMap.Value(Tile(tile)));

    /// <summary>A cached answer for the tile, kept until its <see cref="GameQuery.Revision"/> changes.</summary>
    public T Cached<T>(int tile, string key, Func<T> compute) => _plans.Get(Q, tile, key, compute);

    /// <summary>Units that can fly and have no order (a unit halted mid-fight is still busy; an unsupplied one takes none).</summary>
    public IEnumerable<UnitInfo> IdleUnits() =>
        Units.Where(u => !u.Moving && u.MaxSpeed > 0 && Q.UnitOrders(u.Id) is { Order: null, Unsupplied: false });

    // ---- Proposal helpers ----

    /// <summary>
    /// A line on <paramref name="tile"/> from a <paramref name="deposit"/> (with none: from the existing building
    /// <paramref name="source"/>, or the first stage placed anywhere, inside the I/O zone facing tile <paramref name="zone"/>
    /// if given) through <paramref name="stages"/> into <paramref name="sink"/>, if the planner finds room; if not, a core
    /// that carries the build radius toward the deposit or zone (<see cref="Reach"/>). Whether one is wanted is the caller's question.
    /// </summary>
    public Proposal? Line(string key, int score, int tile, string? deposit, IReadOnlyList<Stage> stages, BuildingView? sink,
        int source = -1, int zone = -1)
    {
        if (sink == null || (deposit != null && !HasDeposit(tile, deposit)))
            return null;
        string planKey = $"{deposit}/{source}/{zone}/{string.Join(",", stages.Select(s => s.BuildingId + s.RecipeId))}/{sink.Id}";
        if (_plans.Get(Q, tile, "line:" + planKey, () => Q.PlanLine(tile, deposit, source, stages, sink.Id, zone)) is not { } plan)
        {
            var anywhere = _plans.Get(Q, tile, "anywhere:" + planKey, () => Q.PlanLine(tile, deposit, source, stages, sink.Id, zone, anywhere: true));
            return anywhere == null ? null
                : Reach(key, score, tile, anywhere.Select(p => (p.X, p.Y, Q.Content.Buildings[p.BuildingId].Footprint(p.Rotation))),
                    (sink.X + sink.Width / 2, sink.Y + sink.Height / 2));
        }
        var commands = plan.Select(p => (Command)new PlaceBuildingCommand(Player, tile, p.BuildingId, p.X, p.Y, p.Rotation, p.RecipeId)).ToList();
        var cost = Sum(plan.Select(p => Q.Content.Buildings[p.BuildingId].Cost));
        string what = stages.Count > 0 ? stages[^1].RecipeId ?? stages[^1].BuildingId : deposit ?? "";
        return new Proposal($"{key}@{tile}", score, commands, $"{what} line into {sink.DefId}, {plan.Count} buildings", tile, cost, [Proposal.Layout(tile)]);
    }

    /// <summary>
    /// The AI's way to build further out (WM-015): a core that carries the build radius toward the out-of-reach cell of
    /// <paramref name="wanted"/> (footprints planned ignoring reach) nearest cell <paramref name="near"/> (<see cref="GameQuery.ReachSpot"/>).
    /// Null if they are all in reach (the plan fails for something else) or nothing gets nearer.
    /// </summary>
    public Proposal? Reach(string key, int score, int tile, IEnumerable<(int X, int Y, (int W, int H) Size)> wanted, (int X, int Y) near)
    {
        var radii = Q.BuildRadii(tile);
        var (nx, ny) = near;
        (int X, int Y)? target = null;
        long best = long.MaxValue;
        foreach (var (x, y, (w, h)) in wanted)
        {
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    long d = (long)(cx - nx) * (cx - nx) + (long)(cy - ny) * (cy - ny);
                    if (d < best && !radii.Any(r => r.Covers(cx, cy)))
                        (best, target) = (d, (cx, cy));
                }
            }
        }
        return target is (int tx, int ty)
            ? Place($"{key}:reach", score, tile, CoreDef.Id, _plans.Get(Q, tile, $"reach:{tx},{ty}", () => Q.ReachSpot(tile, tx, ty)))
            : null;
    }

    /// <summary>One building at <paramref name="spot"/>, if the sim's placement check passes (affordability is the framework's).</summary>
    public Proposal? Place(string key, int score, int tile, string building, Spot? spot)
    {
        if (spot == null)
            return null;
        var result = Q.CheckPlace(tile, building, spot.X, spot.Y, spot.Rotation, spot.RecipeId);
        if (result is not (PlaceResult.Ok or PlaceResult.CannotAfford))
            return null;
        return new Proposal($"{key}@{tile}", score, [new PlaceBuildingCommand(Player, tile, building, spot.X, spot.Y, spot.Rotation, spot.RecipeId)],
            $"{building} at {spot.X},{spot.Y}", tile, Q.Content.Buildings[building].Cost, [Proposal.Layout(tile)]);
    }

    public static Dictionary<string, int> Sum(IEnumerable<IReadOnlyDictionary<string, int>> costs)
    {
        var total = new Dictionary<string, int>();
        foreach (var cost in costs)
        {
            foreach (var (id, n) in cost)
                total[id] = total.GetValueOrDefault(id) + n;
        }
        return total;
    }

    public static Dictionary<string, int> Scale(IReadOnlyDictionary<string, int> cost, int n) => cost.ToDictionary(kv => kv.Key, kv => kv.Value * n);
}

/// <summary>
/// Costly per-tile answers (layout searches, tile values) by tile and key, with the tile's revision then: the same grid
/// gives the same answer, so it is worked out again only once the tile changes. A cache of what the state gives, never history: dropping it (load,
/// takeover) changes no decision.
/// </summary>
internal sealed class PlanCache
{
    readonly Dictionary<(int Tile, string Key), (int Revision, object? Value)> _cache = [];

    public T Get<T>(GameQuery q, int tile, string key, Func<T> compute)
    {
        int revision = q.Revision(tile);
        if (!_cache.TryGetValue((tile, key), out var cached) || cached.Revision != revision)
            _cache[(tile, key)] = cached = (revision, compute());
        return (T)cached.Value!;
    }

    public void Clear() => _cache.Clear();
}

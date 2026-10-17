using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// What one think observes (MP-006), built fresh from <see cref="GameQuery"/> each time: the seat's tiles, their
/// buildings and machine outlets, the tile goals of the strategic layer, and the proposal helpers modules share.
/// Nothing in it outlives the think except the layout cache, which only remembers what the state gives.
/// </summary>
public sealed class AiContext
{
    public const string Drill = "drill", Smelter = "smelter", Mill = "steel_mill", CoreDef = "core", Conveyor = "conveyor", Wire = "wire";
    public const string ExportPort = "export_port", ImportPort = "import_port";
    public const string CopperOre = "copper_ore", IronOre = "iron_ore", Coal = "coal";

    readonly PlanCache _plans;
    readonly Dictionary<int, TileInfo> _tiles = [];

    internal AiContext(GameQuery q, PlanCache plans, AiDesign design)
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
            Outlets[t] = all.Where(b => b.Owner == Player && b.DefId is Drill or Smelter or Mill)
                .ToDictionary(b => b.Id, b => q.OutletOf(t, b.Id));
        }
        int start = q.StartTile();
        Home = Owned.Contains(start) ? start : Owned.FirstOrDefault(t => Cores(t).Any(), -1);
        Units = q.Units(Player);
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
    public Dictionary<int, Dictionary<int, int>> Outlets { get; } = [];

    /// <summary>The unit design picked at <see cref="AiPlayer.Init"/> (seam for FB-021 prefabs).</summary>
    public AiDesign Design { get; }

    /// <summary>The strategic layer's map, by tile id. Set before the modules are asked.</summary>
    public IReadOnlyList<TileInfluence> Influence { get; internal set; } = [];

    public TileGoal GoalOf(int tile) => tile >= 0 && tile < Influence.Count ? Influence[tile].Goal : TileGoal.None;

    /// <summary>Tiles with <paramref name="goal"/>, highest priority first, then by id.</summary>
    public IEnumerable<TileInfluence> WithGoal(TileGoal goal) =>
        Influence.Where(i => i.Goal == goal).OrderByDescending(i => i.Priority).ThenBy(i => i.Tile);

    public BuildingView? Find(int tile, string defId) => Buildings[tile].Values.FirstOrDefault(b => b.DefId == defId);

    /// <summary>True if the building stands on frame: a core there is a unit waiting to lift off.</summary>
    public bool OnFrame(int tile, BuildingView b) => Q.Cells(tile, b.X, b.Y, 1, 1)[0].Frame;

    /// <summary>The seat's cores on the tile, in id order (the order they pay in, FB-019).</summary>
    public IEnumerable<BuildingView> Cores(int tile) =>
        Buildings[tile].Values.Where(b => b.Owner == Player && Q.Content.Buildings[b.DefId].Kind == BuildingKind.Core).OrderBy(b => b.Id);

    /// <summary>The core the tile's lines feed: the first of the seat's cores not on frame, so it stays when units lift off.</summary>
    public BuildingView? BaseCore(int tile) => Cores(tile).FirstOrDefault(b => !OnFrame(tile, b));

    /// <summary>Machines on the tile making <paramref name="recipe"/> whose output ends in <paramref name="sink"/>.</summary>
    public int Lines(int tile, string recipe, int sink) =>
        Outlets[tile].Count(kv => Buildings[tile][kv.Key].RecipeId == recipe && kv.Value == sink);

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
    /// A line on <paramref name="tile"/> from a <paramref name="deposit"/> through <paramref name="stages"/> into
    /// <paramref name="sink"/>, if fewer than <paramref name="want"/> feed it already and the planner finds room.
    /// </summary>
    public Proposal? Line(string key, int score, int tile, string? deposit, IReadOnlyList<Stage> stages, BuildingView? sink, string recipe,
        int want, int source = -1, int zone = -1)
    {
        if (sink == null || Lines(tile, recipe, sink.Id) >= want)
            return null;
        if (deposit != null && !HasDeposit(tile, deposit))
            return null;
        string planKey = $"{deposit}/{source}/{zone}/{string.Join(",", stages.Select(s => s.BuildingId + s.RecipeId))}/{sink.Id}";
        if (_plans.Get(Q, tile, "line:" + planKey, () => Q.PlanLine(tile, deposit, source, stages, sink.Id, zone)) is not { } plan)
            return null;
        var commands = plan.Select(p => (Command)new PlaceBuildingCommand(Player, tile, p.BuildingId, p.X, p.Y, p.Rotation, p.RecipeId)).ToList();
        var cost = Sum(plan.Select(p => Q.Content.Buildings[p.BuildingId].Cost));
        return new Proposal($"{key}@{tile}", score, commands, $"{recipe} line, {plan.Count} buildings", tile, cost, [Proposal.Layout(tile)]);
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

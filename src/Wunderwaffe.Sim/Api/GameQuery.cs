using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>
/// The read-only half of the shared interface (NF-009): everything a player may observe, as seen by
/// <see cref="Player"/>. The GUI, the game AI and external drivers (<see cref="JsonDriver"/>) all read through it and
/// act only through <see cref="Command"/>s, so none of them holds rules of its own. Results are copies
/// (<c>Views.cs</c>) safe to keep or serialise; checks return the same result enums the commands apply with.
/// Each silo adds its own queries in a partial file next to this one (<c>GameQuery.&lt;Silo&gt;.cs</c>).
/// </summary>
public sealed partial class GameQuery(Simulation sim, int player)
{
    /// <summary>Whose eyes this query sees through: <see cref="TileInfo.Presence"/> and the checks are for them.</summary>
    public int Player { get; } = player;

    public ContentDb Content => sim.Content;

    /// <summary>For the other partial files of this class: primary-constructor parameters are only in scope in this one.</summary>
    internal Simulation Sim => sim;

    public long Tick => sim.Tick;

    // ---- Content: what can be built and made (the build bar) ----

    /// <summary>Every building, in build-menu order: size, cost, kind.</summary>
    public IReadOnlyList<BuildingDef> BuildingTypes() => sim.Content.BuildingList;

    /// <summary>The recipes of one building, or every recipe.</summary>
    public IReadOnlyList<RecipeDef> Recipes(string? buildingId = null) =>
        buildingId == null ? [.. sim.Content.Recipes.Values] : sim.Content.RecipesByBuilding.GetValueOrDefault(buildingId) ?? [];

    // ---- Match and players ----

    public MatchView Match() => new(
        sim.Tick, Simulation.TicksPerSecond, sim.Setup, sim.Tiles.Count, Victory.TilesToWin(sim),
        sim.IsOver, sim.Winner, sim.EndReason, sim.EndTick);

    /// <summary>Every player of the match (from <see cref="GameSetup.Players"/>, or those that exist), in id order.</summary>
    public IReadOnlyList<PlayerView> Players()
    {
        int count = Math.Max(sim.Setup?.Players ?? 0, sim.Players.Count);
        var tiles = new int[count];
        var units = new int[count];
        foreach (var t in sim.Tiles)
        {
            if (t.Owner >= 0 && t.Owner < count)
                tiles[t.Owner]++;
        }
        foreach (var u in sim.Units)
        {
            if (u.Owner >= 0 && u.Owner < count)
                units[u.Owner]++;
        }
        var list = new List<PlayerView>(count);
        for (int id = 0; id < count; id++)
        {
            var p = sim.PlayerOrNull(id);
            list.Add(new PlayerView(
                id, sim.Setup?.IsAi(id) ?? false, p?.EliminatedTick >= 0, tiles[id], units[id], p?.GoalsDone ?? 0,
                p?.Shipped ?? 0, p?.Launched ?? 0, p == null ? new Dictionary<string, long>() : new SortedDictionary<string, long>(
                    p.Produced.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal)));
        }
        return list;
    }

    /// <summary>The goal <see cref="Player"/> is on, or null once the chain is done.</summary>
    public GoalView? Goal()
    {
        var goals = sim.Content.Goals;
        if (Progression.CurrentGoal(sim, Player) is not { } goal)
            return null;
        var c = goal.Condition;
        return new GoalView(goals.ToList().IndexOf(goal), goals.Count, goal.Id, goal.Name, goal.Description, c.Type, c.Item,
            Math.Min(Progression.Progress(sim, Player, c), c.Amount), c.Amount);
    }

    // ---- Tiles ----

    public IReadOnlyList<TileSummary> Tiles() =>
        sim.Tiles.Select(t => new TileSummary(t.Id, t.Name, t.Owner, t.Biome, t.CenterX, t.CenterY, t.Neighbors, t.Grid.BuildingCount)).ToList();

    /// <summary>Ids of the tiles <paramref name="owner"/> holds (default: <see cref="Player"/>), in id order.</summary>
    public IReadOnlyList<int> OwnedTiles(int? owner = null)
    {
        int who = owner ?? Player;
        return sim.Tiles.Where(t => t.Owner == who).Select(t => t.Id).ToList();
    }

    /// <summary>The tile <paramref name="owner"/> (default: <see cref="Player"/>) started on, or -1.</summary>
    public int StartTile(int? owner = null)
    {
        int who = owner ?? Player;
        return who >= 0 && who < sim.StartTileIds.Count ? sim.StartTileIds[who] : -1;
    }

    /// <summary>The tile under a world cell, or -1.</summary>
    public int TileAtCell(int worldX, int worldY) => sim.TileAtCell(worldX, worldY)?.Id ?? -1;

    public TileInfo? Tile(int tileId)
    {
        if (sim.TryGetTile(tileId) is not { } t)
            return null;
        var grid = t.Grid;
        var counts = new int[sim.Content.TerrainList.Count];
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.InBounds(x, y))
                    counts[grid.TerrainAt(x, y)]++;
            }
        }
        int area = Math.Max(1, grid.Area);
        var terrain = counts.Select((n, i) => (n, i)).Where(c => c.n * 100 >= area * 5).OrderByDescending(c => c.n).ThenBy(c => c.i)
            .Select(c => new TerrainShare(sim.Content.TerrainList[c.i].Id, c.n * 100 / area)).ToList();
        var deposits = grid.DepositCounts();
        var missing = sim.Content.Items.Values.Where(i => i.Regional && !deposits.ContainsKey(i.Id)).Select(i => i.Id)
            .Order(StringComparer.Ordinal).ToList();
        var ports = new List<PortLink>();
        foreach (int n in t.Neighbors)
        {
            int exports = grid.ExportPorts.Count(p => PortRules.PartnerOf(grid, p) == n);
            int imports = grid.ImportPorts.Count(p => PortRules.PartnerOf(grid, p) == n);
            if (exports + imports > 0)
                ports.Add(new PortLink(n, exports, imports));
        }
        return new TileInfo(
            t.Id, t.Name, t.Owner, t.Biome, t.OriginX, t.OriginY, grid.Width, grid.Height, grid.Area, grid.BuildableCount,
            t.Neighbors, terrain, deposits, missing, Funds.Total(t, Player), grid.BuildingCount, ports,
            t.CircleX, t.CircleY, Territory.HasPresence(sim, Player, t), Expansion.Progress(t, Player), Expansion.CaptureTicks(sim),
            Territory.IsContested(sim, t));
    }

    /// <summary>
    /// Grows whenever anything on the tile's grid changes (buildings, frames). Drivers can cache layout searches
    /// against it.
    /// </summary>
    public int Revision(int tileId) => sim.TryGetTile(tileId)?.Grid.Revision ?? -1;

    /// <summary>
    /// <see cref="Player"/>'s build funds on a tile, by item: what their cores standing there hold together (FB-019).
    /// </summary>
    public IReadOnlyDictionary<string, long> Inventory(int tileId) =>
        sim.TryGetTile(tileId) is { } t ? Funds.Total(t, Player) : new Dictionary<string, long>();

    /// <summary>
    /// Items summed over every core <see cref="Player"/> has standing, on any tile: the world HUD's resource summary
    /// (UI-001, FB-019). Cores on units don't count.
    /// </summary>
    public IReadOnlyDictionary<string, long> ResourceTotals()
    {
        var totals = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var t in sim.Tiles)
        {
            foreach (var (id, n) in Funds.Total(t, Player))
                totals[id] = totals.GetValueOrDefault(id) + n;
        }
        return totals;
    }

    /// <summary>The cells of a rectangle of a tile's box, row by row; cells past the box are left out.</summary>
    public IReadOnlyList<CellView> Cells(int tileId, int x, int y, int width, int height)
    {
        var list = new List<CellView>();
        if (sim.TryGetTile(tileId) is not { } t)
            return list;
        var g = t.Grid;
        for (int cy = Math.Max(0, y); cy < Math.Min(g.Height, y + height); cy++)
        {
            for (int cx = Math.Max(0, x); cx < Math.Min(g.Width, x + width); cx++)
            {
                list.Add(new CellView(cx, cy, g.InBounds(cx, cy), g.Buildable(cx, cy), sim.Content.TerrainList[g.TerrainAt(cx, cy)].Id,
                    g.DepositAt(cx, cy), g.ZoneAt(cx, cy), g.BuildingAt(cx, cy)?.Id ?? -1, g.Has(BuildingKind.Frame, cx, cy)));
            }
        }
        return list;
    }

    /// <summary>Every deposit cell of <paramref name="itemId"/> on the tile (all items if null), row by row.</summary>
    public IReadOnlyList<DepositCell> DepositCells(int tileId, string? itemId = null)
    {
        var list = new List<DepositCell>();
        if (sim.TryGetTile(tileId) is not { } t)
            return list;
        var g = t.Grid;
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (g.InBounds(x, y) && g.DepositAt(x, y) is { } item && (itemId == null || item == itemId))
                    list.Add(new DepositCell(x, y, item));
            }
        }
        return list;
    }

    // ---- Buildings ----

    /// <summary>Every building on the tile, in id order.</summary>
    public IReadOnlyList<BuildingView> Buildings(int tileId) =>
        sim.TryGetTile(tileId) is { } t ? t.Grid.Buildings.Select(b => View(t, b)).ToList() : [];

    public BuildingView? BuildingAt(int tileId, int x, int y) =>
        sim.TryGetTile(tileId) is { } t && t.Grid.BuildingAt(x, y) is { } b ? View(t, b) : null;

    /// <summary>The first building of a kind of def on the tile (by id), such as the core.</summary>
    public BuildingView? FindBuilding(int tileId, string defId) =>
        sim.TryGetTile(tileId) is { } t && t.Grid.Buildings.FirstOrDefault(b => b.DefId == defId) is { } b ? View(t, b) : null;

    BuildingView View(TileState t, PlacedBuilding b)
    {
        var holding = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (b.Machine is { } m)
        {
            foreach (var (id, n) in m.Input)
                holding[id] = holding.GetValueOrDefault(id) + n;
            foreach (var (id, n) in m.Output)
                holding[id] = holding.GetValueOrDefault(id) + n;
        }
        if (b.Belt is { } belt)
        {
            for (int i = 0; i < belt.Count; i++)
                holding[belt.Items[i]!] = holding.GetValueOrDefault(belt.Items[i]!) + 1;
        }
        foreach (var (id, n) in b.Stock?.Entries ?? [])
            holding[id] = (int)Math.Min(int.MaxValue, n);
        bool port = b.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort;
        return new BuildingView(
            b.Id, b.DefId, b.Owner, b.X, b.Y, b.Width, b.Height, b.Rotation, b.RecipeId, b.Hp, sim.Content.Buildings[b.DefId].Hp,
            b.Machine?.Status, b.Machine?.MissingInput, port ? PortRules.PartnerOf(t.Grid, b) : -1, holding);
    }

    // ---- Units ----

    /// <summary>Units in id order: all of them, or only <paramref name="owner"/>'s.</summary>
    public IReadOnlyList<UnitInfo> Units(int? owner = null) =>
        sim.Units.Where(u => owner == null || u.Owner == owner).Select(View).ToList();

    public UnitInfo? Unit(int unitId) => sim.UnitById(unitId) is { } u ? View(u) : null;

    UnitInfo View(Unit u)
    {
        int hp = 0, max = 0;
        foreach (var b in u.Blocks)
        {
            hp += b.Hp;
            max += sim.Content.Buildings[b.DefId].Hp;
        }
        return new UnitInfo(
            u.Id, u.Owner, u.TileId, (int)IntMath.FloorDiv(u.X, World.Unit.CellScale), (int)IntMath.FloorDiv(u.Y, World.Unit.CellScale),
            u.Blocks.Count, hp, max, u.Mass, u.MaxSpeed, u.Moving, u.Moving ? u.DestTileId : -1, u.DestX, u.DestY);
    }

    // ---- Alerts ----

    /// <summary><see cref="Player"/>'s events after <paramref name="sinceSeq"/>, oldest first (UI-003).</summary>
    public IReadOnlyList<GameEvent> Events(long sinceSeq = 0) =>
        sim.EventsSince(sinceSeq).Where(e => e.Player == Player).ToList();

    /// <summary>Every player's events after <paramref name="sinceSeq"/>: eliminations and victories concern everyone.</summary>
    public IReadOnlyList<GameEvent> AllEvents(long sinceSeq = 0) => sim.EventsSince(sinceSeq).ToList();

    public long LastEventSeq => sim.LastEventSeq;

    // ---- Checks: what a command would do, with the rules the command applies ----

    public PlaceResult CheckPlace(int tileId, string buildingId, int x, int y, int rotation = 0, string? recipeId = null) =>
        sim.TryGetTile(tileId) is { } t ? Placement.Check(sim, t, Player, buildingId, x, y, rotation, recipeId, out _) : PlaceResult.OutOfBounds;

    /// <summary>The recipe a placement would get: extractors pick theirs from the deposit underneath. Null if it can't be placed.</summary>
    public string? PlacedRecipe(int tileId, string buildingId, int x, int y, int rotation = 0, string? recipeId = null)
    {
        if (sim.TryGetTile(tileId) is not { } t)
            return null;
        var r = Placement.Check(sim, t, Player, buildingId, x, y, rotation, recipeId, out var plan);
        return r is PlaceResult.Ok or PlaceResult.CannotAfford ? plan.RecipeId : null;
    }

    /// <summary>Whether cutting the building at (fromX, fromY) and pasting it at (x, y) on <paramref name="toTileId"/> would go through (FB-023).</summary>
    public PlaceResult CheckMove(int tileId, int fromX, int fromY, int toTileId, int x, int y, int rotation = 0) =>
        sim.TryGetTile(tileId) is { } t && sim.TryGetTile(toTileId) is { } to
            ? Relocation.Check(sim, t, Player, fromX, fromY, to, x, y, rotation, out _) : PlaceResult.OutOfBounds;


    public PlaceResult CheckFrame(int tileId, int x, int y) =>
        sim.TryGetTile(tileId) is { } t ? UnitAssembly.CheckFrame(Sim, t, Player, x, y) : PlaceResult.OutOfBounds;

    public AssembleResult CheckActivate(int tileId, int x, int y) =>
        sim.TryGetTile(tileId) is { } t ? UnitAssembly.CheckActivate(sim, t, Player, x, y, out _) : AssembleResult.NoCore;

    public DeployResult CheckDeploy(int unitId) => UnitAssembly.CheckDeploy(sim, Player, unitId, out _);

    /// <summary>True if <see cref="Player"/>'s cores standing on the tile can pay <paramref name="cost"/> (FB-019).</summary>
    public bool CanAfford(int tileId, IReadOnlyDictionary<string, int> cost) =>
        sim.TryGetTile(tileId) is { } t && Funds.CanPay(t, Player, cost);

    // ---- Planning helpers: read-only searches drivers would otherwise have to write themselves ----

    /// <summary>See <see cref="Planner.FindSpot"/>.</summary>
    public Spot? FindSpot(int tileId, string buildingId, string? recipeId = null, string? onDeposit = null, int touching = -1,
        int nearX = -1, int nearY = -1, int zone = -1, bool anywhere = false) =>
        sim.TryGetTile(tileId) is { } t ? Planner.FindSpot(sim, t, Player, buildingId, recipeId, onDeposit, touching, nearX, nearY, zone, anywhere) : null;

    /// <summary>Where a core carries the build radius toward cell (x, y) (WM-015); see <see cref="Planner.Reach"/>.</summary>
    public Spot? ReachSpot(int tileId, int x, int y) => sim.TryGetTile(tileId) is { } t ? Planner.Reach(sim, t, Player, x, y) : null;

    /// <summary>See <see cref="Planner.RouteBelt"/>.</summary>
    public IReadOnlyList<BeltStep>? RouteBelt(int tileId, int fromBuilding, int toBuilding) =>
        sim.TryGetTile(tileId) is { } t ? Planner.RouteBelt(sim, t, Player, fromBuilding, toBuilding) : null;

    /// <summary>See <see cref="Planner.FindFrame"/>.</summary>
    public Spot? FindFrame(int tileId, int width, int height) =>
        sim.TryGetTile(tileId) is { } t ? Planner.FindFrame(sim, t, Player, width, height) : null;

    /// <summary>See <see cref="Planner.PlanLine"/>.</summary>
    public IReadOnlyList<PlannedBuilding>? PlanLine(int tileId, string? depositItem, int source, IReadOnlyList<Stage> stages, int sink,
        int zone = -1, bool anywhere = false) =>
        sim.TryGetTile(tileId) is { } t ? Planner.PlanLine(sim, t, Player, depositItem, source, stages, sink, zone, anywhere) : null;

    /// <summary>The building whose output ends up in (see <see cref="Planner.OutletOf"/>), or -1.</summary>
    public int OutletOf(int tileId, int buildingId) =>
        sim.TryGetTile(tileId) is { } t && t.Grid.Buildings.FirstOrDefault(b => b.Id == buildingId) is { } b
            ? Planner.OutletOf(t, b)?.Id ?? -1 : -1;
}

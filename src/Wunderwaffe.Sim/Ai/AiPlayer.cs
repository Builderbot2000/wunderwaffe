using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// A game AI opponent (MP-002). It plays through the shared interface only (NF-009, OQ-33): it reads the match through
/// <see cref="GameQuery"/> and acts by returning ordinary <see cref="Command"/>s, which the host enqueues like any
/// player's. So its moves land in the command log, replay from saves and travel through lockstep like a human's.
/// It keeps no memory between calls: every decision comes from the current state, so it is deterministic, survives a
/// save and load, and never waits on a plan that can no longer happen. Each <see cref="Think"/> does the first thing
/// on its list it can do and afford, so an impossible step never blocks the ones after it.
/// </summary>
public sealed class AiPlayer(int player)
{
    /// <summary>Ticks between decisions. Seats are staggered so AIs don't all think on one tick.</summary>
    public const int ThinkEvery = Simulation.TicksPerSecond / 2;

    /// <summary>Units held at home before they go out together.</summary>
    public const int AttackGroup = 3;

    /// <summary>Units the AI keeps building at most.</summary>
    public const int MaxUnits = 12;

    const string Drill = "drill", Smelter = "smelter", Mill = "steel_mill", Core = "core", Conveyor = "conveyor";
    const string ExportPort = "export_port", ImportPort = "import_port", Thruster = "thruster";
    const string CopperOre = "copper_ore", IronOre = "iron_ore", Coal = "coal";

    /// <summary>
    /// The AI's unit design, on a <see cref="DesignWidth"/> x <see cref="DesignHeight"/> block of frame: a thruster, a
    /// core and a thruster along the top, rotation 0 (facing east). The starting unit's layout. Placeholder until the
    /// blueprint library (UD-001) can supply designs.
    /// </summary>
    public static readonly IReadOnlyList<(string Building, int Dx)> UnitDesign = [(Thruster, 0), (Core, 1), (Thruster, 3)];

    public const int DesignWidth = 4, DesignHeight = 2;

    /// <summary>
    /// What the first unit's core should hold before it lifts off: it pays for building where it lands (FB-019), the
    /// coal tile's drill, belt and export port.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Carry = new Dictionary<string, int> { [CopperOre] = 60, [IronOre] = 30 };

    public int Player { get; } = player;

    /// <summary>
    /// Layout searches by tile and line, with the tile's revision then: the same grid gives the same layout, so they
    /// are redone only once the tile changes. Purely a cache of what the state gives: it never makes the AI's choices
    /// depend on history.
    /// </summary>
    readonly Dictionary<(int Tile, string Line), (int Revision, IReadOnlyList<PlannedBuilding>? Plan)> _plans = [];

    /// <summary>True on the ticks this AI decides on.</summary>
    public bool Due(long tick) => tick % ThinkEvery == Player % ThinkEvery;

    /// <summary>True if <see cref="Due"/> on any of the <paramref name="ticks"/> ticks from <paramref name="tick"/>.</summary>
    public bool DueWithin(long tick, int ticks)
    {
        for (long t = tick; t < tick + ticks; t++)
        {
            if (Due(t))
                return true;
        }
        return false;
    }

    /// <summary>What to do now: zero or more commands to enqueue before the next step.</summary>
    public IReadOnlyList<Command> Think(Simulation sim)
    {
        var q = new GameQuery(sim, Player);
        if (q.Match().Over || q.Players().FirstOrDefault(p => p.Id == Player) is not { Eliminated: false })
            return [];
        var ctx = new Context(q, Player);
        if (ctx.Home < 0)
            return [];
        return Clean(ctx) ?? Base(ctx) ?? Economy(ctx) ?? Steel(ctx) ?? Units(ctx) ?? Expand(ctx) ?? Outposts(ctx) ?? Attack(ctx) ?? [];
    }

    /// <summary>What one decision looks at: the home tile and every line on the AI's tiles.</summary>
    sealed class Context
    {
        public Context(GameQuery q, int player)
        {
            Q = q;
            Owned = q.OwnedTiles();
            int start = q.StartTile();
            Home = Owned.Contains(start) ? start : Owned.FirstOrDefault(t => q.FindBuilding(t, Core) != null, -1);
            foreach (int t in Owned)
            {
                var all = q.Buildings(t);
                Buildings[t] = all.ToDictionary(b => b.Id);
                Outlets[t] = all.Where(b => b.Owner == player && b.DefId is Drill or Smelter or Mill)
                    .ToDictionary(b => b.Id, b => q.OutletOf(t, b.Id));
            }
        }

        public GameQuery Q { get; }
        public IReadOnlyList<int> Owned { get; }
        public int Home { get; }
        public Dictionary<int, Dictionary<int, BuildingView>> Buildings { get; } = [];
        public Dictionary<int, Dictionary<int, int>> Outlets { get; } = [];

        public BuildingView? Find(int tile, string defId) => Buildings[tile].Values.FirstOrDefault(b => b.DefId == defId);

        /// <summary>True if the building stands on frame: a core there is a unit waiting to lift off.</summary>
        public bool OnFrame(int tile, BuildingView b) => Q.Cells(tile, b.X, b.Y, 1, 1)[0].Frame;

        /// <summary>Cores of <paramref name="player"/>'s on the tile, in id order.</summary>
        public IEnumerable<BuildingView> Cores(int tile, int player) =>
            Buildings[tile].Values.Where(b => b.Owner == player && Q.Content.Buildings[b.DefId].Kind == Content.BuildingKind.Core).OrderBy(b => b.Id);

        /// <summary>The core the tile's lines feed: the first of the player's cores not on frame, so it stays when units lift off.</summary>
        public BuildingView? BaseCore(int tile, int player) => Cores(tile, player).FirstOrDefault(b => !OnFrame(tile, b));

        /// <summary>Machines on the tile making <paramref name="recipe"/> whose output ends in <paramref name="sink"/>.</summary>
        public int Lines(int tile, string recipe, int sink) =>
            Outlets[tile].Count(kv => Buildings[tile][kv.Key].RecipeId == recipe && kv.Value == sink);

        readonly Dictionary<int, TileInfo> _tiles = [];

        /// <summary>The tile's view, computed once per decision.</summary>
        public TileInfo Tile(int tile)
        {
            if (!_tiles.TryGetValue(tile, out var view))
                _tiles[tile] = view = Q.Tile(tile)!;
            return view;
        }

        public bool HasDeposit(int tile, string item) => Tile(tile).Deposits.ContainsKey(item);
    }

    /// <summary>Takes away drills and smelters whose output goes nowhere (a line cut short): a full refund.</summary>
    IReadOnlyList<Command>? Clean(Context c)
    {
        foreach (int t in c.Owned)
        {
            foreach (var (id, outlet) in c.Outlets[t])
            {
                var b = c.Buildings[t][id];
                if (outlet < 0)
                    return [new RemoveBuildingCommand(Player, t, b.X, b.Y)];
            }
        }
        return null;
    }

    /// <summary>
    /// A home core off the frame, so the home tile keeps its funds when the starting unit lifts off (FB-019); built
    /// first, from the starting items.
    /// </summary>
    IReadOnlyList<Command>? Base(Context c) =>
        c.BaseCore(c.Home, Player) == null ? Place(c, c.Home, Core, c.Q.FindSpot(c.Home, Core)) : null;

    /// <summary>The home economy: ore and plates into the base core, more lines once steel flows.</summary>
    IReadOnlyList<Command>? Economy(Context c)
    {
        int h = c.Home;
        if (c.BaseCore(h, Player) is not { } core)
            return null;
        bool steel = c.Find(h, Mill) != null;
        int extra = steel ? 1 : 0;
        return Line(c, h, CopperOre, [], core, "mine_copper_ore", 2 + extra)
            ?? Line(c, h, IronOre, [new Stage(Smelter, "smelt_iron")], core, "smelt_iron", 2 + extra)
            ?? Line(c, h, IronOre, [], core, "mine_iron_ore", 1 + extra)
            ?? Line(c, h, CopperOre, [new Stage(Smelter, "smelt_copper")], core, "smelt_copper", 1 + extra);
    }

    /// <summary>
    /// Builds a line on <paramref name="tile"/> from a <paramref name="deposit"/> through <paramref name="stages"/> into
    /// <paramref name="sink"/>, if fewer than <paramref name="want"/> feed it and it is affordable.
    /// </summary>
    IReadOnlyList<Command>? Line(Context c, int tile, string? deposit, IReadOnlyList<Stage> stages, BuildingView? sink, string recipe, int want,
        int source = -1, int zone = -1)
    {
        if (sink == null || c.Lines(tile, recipe, sink.Id) >= want)
            return null;
        if (deposit != null && !c.HasDeposit(tile, deposit))
            return null;
        var key = (tile, $"{deposit}/{source}/{zone}/{string.Join(",", stages.Select(s => s.BuildingId + s.RecipeId))}/{sink.Id}");
        int revision = c.Q.Revision(tile);
        if (!_plans.TryGetValue(key, out var cached) || cached.Revision != revision)
            _plans[key] = cached = (revision, c.Q.PlanLine(tile, deposit, source, stages, sink.Id, zone));
        if (cached.Plan is not { } plan || !Affordable(c, tile, plan))
            return null;
        return plan.Select(p => (Command)new PlaceBuildingCommand(Player, tile, p.BuildingId, p.X, p.Y, p.Rotation, p.RecipeId)).ToList();
    }

    bool Affordable(Context c, int tile, IEnumerable<PlannedBuilding> plan) =>
        c.Q.CanAfford(tile, Sum(plan.Select(p => c.Q.Content.Buildings[p.BuildingId].Cost)));

    static Dictionary<string, int> Sum(IEnumerable<IReadOnlyDictionary<string, int>> costs)
    {
        var total = new Dictionary<string, int>();
        foreach (var cost in costs)
        {
            foreach (var (id, n) in cost)
                total[id] = total.GetValueOrDefault(id) + n;
        }
        return total;
    }

    /// <summary>
    /// Steel: coal mined on a neighbouring tile (powered by the unit core of the scout that claimed it) and shipped home through a port pair, a mill beside the import port fed
    /// plates by its own smelter line, and a belt from the mill to the core.
    /// </summary>
    IReadOnlyList<Command>? Steel(Context c)
    {
        int h = c.Home;
        var linked = c.Buildings[h].Values.Where(b => b.DefId == ImportPort).Select(b => b.Partner).ToHashSet();
        int coal = c.Owned.Where(t => t != h && c.Tile(h).Neighbors.Contains(t) && c.HasDeposit(t, Coal))
            .OrderBy(t => linked.Contains(t) ? 0 : 1).ThenBy(t => t).DefaultIfEmpty(-1).First();
        if (coal < 0)
            return null;

        // The coal tile: an export port facing home, a coal drill belted into it.
        var export = c.Buildings[coal].Values.FirstOrDefault(b => b.DefId == ExportPort && b.Partner == h);
        if (export == null)
        {
            var cells = c.Q.DepositCells(coal, Coal);
            var (nx, ny) = cells.Count == 0 ? (-1, -1) : ((int)cells.Average(d => d.X), (int)cells.Average(d => d.Y));
            return Place(c, coal, ExportPort, c.Q.FindSpot(coal, ExportPort, nearX: nx, nearY: ny, zone: h));
        }
        if (Line(c, coal, Coal, [], export, "mine_coal", 1) is { } coalLine)
            return coalLine;

        // Home: an import port facing the coal tile, the mill beside it and a belt from the mill to the core, planned
        // together so they all fit; then a plate line into the mill.
        if (c.Find(h, Mill) is not { } mill)
            return Line(c, h, null, [new Stage(ImportPort), new Stage(Mill)], c.BaseCore(h, Player), "make_steel", 1, zone: coal);
        return Line(c, h, IronOre, [new Stage(Smelter, "smelt_iron")], mill, "smelt_iron", 1);
    }

    IReadOnlyList<Command>? Place(Context c, int tile, string building, Spot? spot) =>
        spot != null && c.Q.CanAfford(tile, c.Q.Content.Buildings[building].Cost)
            ? [new PlaceBuildingCommand(Player, tile, building, spot.X, spot.Y, spot.Rotation, spot.RecipeId)]
            : null;

    /// <summary>
    /// Builds units on the home tile from <see cref="UnitDesign"/>, belts ore into each one's core until it holds
    /// <see cref="Carry"/>, and lifts it off. Only once the home has a base core, so it keeps funds of its own.
    /// </summary>
    IReadOnlyList<Command>? Units(Context c)
    {
        int h = c.Home;
        if (c.BaseCore(h, Player) == null)
            return null;
        if (c.Cores(h, Player).FirstOrDefault(b => c.OnFrame(h, b)) is { } core)
        {
            if (c.Q.CheckActivate(h, core.X, core.Y) != AssembleResult.Ok)
                return [new RemoveBuildingCommand(Player, h, core.X, core.Y)];
            // Each item short of Carry is belted in by an ore line of its own; one that can't be built is gone without.
            // Only the first claim waits for it (the coal tile needs funds); later units go as soon as they can.
            bool filling = false;
            foreach (var (item, n) in c.Owned.Count == 1 ? Carry : new Dictionary<string, int>())
            {
                string recipe = item == CopperOre ? "mine_copper_ore" : "mine_iron_ore";
                if (core.Holding.GetValueOrDefault(item) >= n)
                    continue;
                if (c.Lines(h, recipe, core.Id) > 0)
                    filling = true;
                else if (Line(c, h, item, [], core, recipe, 1) is { } line)
                    return line;
            }
            return filling ? null : [new ActivateUnitCommand(Player, h, core.X, core.Y)];
        }
        if (c.Q.Units(Player).Count >= MaxUnits || c.Find(h, Mill) == null)
            return null;
        int cells = DesignWidth * DesignHeight;
        var cost = Sum(UnitDesign.Select(d => c.Q.Content.Buildings[d.Building].Cost).Append(Scale(Balance.FrameCost, cells)));
        if (!c.Q.CanAfford(h, cost) || c.Q.FindFrame(h, DesignWidth, DesignHeight) is not { } spot)
            return null;
        var commands = new List<Command>();
        for (int y = 0; y < DesignHeight; y++)
        {
            for (int x = 0; x < DesignWidth; x++)
                commands.Add(new PlaceFrameCommand(Player, h, spot.X + x, spot.Y + y));
        }
        foreach (var (building, dx) in UnitDesign)
            commands.Add(new PlaceBuildingCommand(Player, h, building, spot.X + dx, spot.Y));
        return commands;
    }

    static Dictionary<string, int> Scale(IReadOnlyDictionary<string, int> cost, int n) => cost.ToDictionary(kv => kv.Key, kv => kv.Value * n);

    /// <summary>
    /// Claims tiles the way a player does (WM-006): a unit flies to a tile's victory circle and lands there, and its
    /// blocks hold the circle until the timer fills. The coal tile first, then, once steel flows, whichever unclaimed
    /// neighbour of an owned tile is best (buildable ground, deposits). One unit at a time is sent; the rest stay for
    /// <see cref="Attack"/>.
    /// </summary>
    IReadOnlyList<Command>? Expand(Context c)
    {
        var units = c.Q.Units(Player).Where(u => u.MaxSpeed > 0).ToList();
        // A unit standing in the circle of a tile the AI doesn't own lands there.
        foreach (var u in units.Where(u => !u.Moving))
        {
            var tile = c.Tile(u.TileId);
            if (tile.Owner != Player && c.Q.InCircle(u.TileId, u.CellX - tile.OriginX, u.CellY - tile.OriginY)
                && c.Q.CheckDeploy(u.Id) == DeployResult.Ok)
                return [new DeployUnitCommand(Player, u.Id)];
        }

        int h = c.Home;
        bool haveCoal = c.Owned.Any(t => t != h && c.HasDeposit(t, Coal));
        bool steel = c.Find(h, Mill) != null;
        int baseCore = c.BaseCore(h, Player)?.Id ?? -1;
        bool basics = c.Lines(h, "mine_copper_ore", baseCore) >= 2 && c.Lines(h, "smelt_iron", baseCore) >= 1;
        if (!basics || (haveCoal && !steel))
            return null;
        // One claim at a time: a unit on its way to a circle, or a circle being held, waits for the outcome.
        var tiles = c.Q.Tiles();
        if (units.Any(u => u.Moving && u.DestTileId >= 0 && tiles[u.DestTileId].Owner != Player && c.Q.InCircle(u.DestTileId, u.DestX, u.DestY))
            || tiles.Any(t => t.Owner != Player && c.Q.ClaimProgress(t.Id) > 0))
            return null;
        if (units.Where(u => !u.Moving).OrderBy(u => u.Id).FirstOrDefault() is not { } scout)
            return null;

        var homeNeighbors = c.Tile(h).Neighbors;
        var best = tiles
            .Where(t => t.Owner == TileState.Unowned && c.Owned.Any(o => t.Neighbors.Contains(o)))
            .Select(t => (t.Id, View: c.Tile(t.Id)))
            .Where(t => haveCoal || (homeNeighbors.Contains(t.Id) && t.View.Deposits.ContainsKey(Coal)))
            .OrderByDescending(t => (t.View.Deposits.ContainsKey(Coal) && !haveCoal ? 1000 : 0)
                + t.View.Buildable * 100 / Math.Max(1, t.View.Area) + t.View.Deposits.Values.Sum() / 20)
            .ThenBy(t => t.Id)
            .FirstOrDefault(t => c.Q.Travel(scout.Id, t.Id, t.View.CircleX, t.View.CircleY) != null);
        return best.View is not { } target ? null : [new MoveUnitsCommand(Player, [scout.Id], target.Id, target.CircleX, target.CircleY)];
    }

    /// <summary>
    /// Every other tile: a copper and an iron ore line into its first core, paid from what the landed unit's core
    /// brought (FB-019).
    /// </summary>
    IReadOnlyList<Command>? Outposts(Context c)
    {
        foreach (int t in c.Owned)
        {
            if (t == c.Home || c.Cores(t, Player).FirstOrDefault() is not { } core)
                continue;
            if ((Line(c, t, CopperOre, [], core, "mine_copper_ore", 1) ?? Line(c, t, IronOre, [], core, "mine_iron_ore", 1)) is { } line)
                return line;
        }
        return null;
    }

    /// <summary>
    /// Sends idle units, once there are <see cref="AttackGroup"/>, at the nearest enemy core (those not there yet) with an
    /// attack-move (CB-001): they fight whatever they meet on the way.
    /// </summary>
    IReadOnlyList<Command>? Attack(Context c)
    {
        // Idle = no order: a unit halted mid-fight on an attack-move is still busy, and an unsupplied one takes no orders.
        var idle = c.Q.Units(Player)
            .Where(u => !u.Moving && u.MaxSpeed > 0 && c.Q.UnitOrders(u.Id) is { Order: null, Unsupplied: false }).ToList();
        if (idle.Count < AttackGroup)
            return null;
        var enemies = c.Q.Players().Where(p => p.Id != Player && !p.Eliminated).Select(p => p.Id).ToHashSet();
        var (ux, uy) = (idle[0].CellX, idle[0].CellY);
        var target = c.Q.Tiles()
            .Where(t => enemies.Contains(t.Owner))
            .OrderBy(t => (long)(t.CenterX - ux) * (t.CenterX - ux) + (long)(t.CenterY - uy) * (t.CenterY - uy))
            .ThenBy(t => t.Id)
            .Select(t => (t.Id, Core: c.Q.Buildings(t.Id).FirstOrDefault(b => c.Q.Content.Buildings[b.DefId].Kind == Content.BuildingKind.Core)))
            .FirstOrDefault(t => t.Core != null);
        if (target.Core is not { } core)
            return null;
        // Units already standing on the target tile are there: sending them again would only spam the log.
        idle = idle.Where(u => u.TileId != target.Id).ToList();
        if (idle.Count == 0)
            return null;
        return [new AttackMoveCommand(Player, idle.Select(u => u.Id).ToArray(), target.Id, core.X + core.Width / 2, core.Y + core.Height / 2)];
    }
}

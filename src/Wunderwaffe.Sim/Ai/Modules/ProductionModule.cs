using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Ai.AiContext;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Production (MP-006): steel and units. Steel: coal mined on a neighbouring tile and shipped home through a port pair,
/// a mill beside the import port fed plates by its own smelter line, and a belt from the mill to the core. Units: the
/// seat's design (<see cref="AiContext.Design"/>) built on frame at home once steel flows, its core filled with what
/// the first claim needs, then lifted off.
/// </summary>
public sealed class ProductionModule : IAiModule
{
    public const int SteelScore = 600, UnitScore = 500, NewUnitScore = 480;

    /// <summary>Units the AI keeps at most.</summary>
    public const int MaxUnits = 12;

    public string Name => "production";

    public IEnumerable<Proposal> Propose(AiContext c) => [.. Steel(c), .. Units(c)];

    static IEnumerable<Proposal> Steel(AiContext c)
    {
        int h = c.Home;
        var linked = c.Buildings[h].Values.Where(b => b.DefId == ImportPort).Select(b => b.Partner).ToHashSet();
        int coal = c.Owned.Where(t => t != h && c.Tiles[h].Neighbors.Contains(t) && c.HasDeposit(t, Coal))
            .OrderBy(t => linked.Contains(t) ? 0 : 1).ThenBy(t => t).DefaultIfEmpty(-1).First();
        if (coal < 0)
            yield break;

        // The coal tile: an export port facing home, a coal drill belted into it.
        var export = c.Buildings[coal].Values.FirstOrDefault(b => b.DefId == ExportPort && b.Partner == h);
        if (export == null)
        {
            var (_, _, x, y) = CoalRun(c, coal, h);
            if (c.Place("export", SteelScore, coal, ExportPort, c.Q.FindSpot(coal, ExportPort, nearX: x, nearY: y, zone: h)) is { } p)
                yield return p;
            yield break;
        }
        if (c.Line("coal", SteelScore, coal, Coal, [], export, "mine_coal", 1) is { } coalLine)
            yield return coalLine;

        // Home: an import port facing the coal tile, the mill beside it and a belt from the mill to the core, planned
        // together so they all fit; then a plate line into the mill.
        if (c.Find(h, Mill) is not { } mill)
        {
            if (c.Line("mill", SteelScore - 1, h, null, [new Stage(ImportPort), new Stage(Mill)], c.BaseCore(h), "make_steel", 1, zone: coal) is { } m)
                yield return m;
        }
        else if (c.Line("mill_feed", SteelScore - 1, h, IronOre, [new Stage(Smelter, "smelt_iron")], mill, "smelt_iron", 1) is { } feed)
        {
            yield return feed;
        }
    }

    /// <summary>The least the first unit's core holds before it lifts off.</summary>
    public static readonly IReadOnlyDictionary<string, int> MinCarry = new Dictionary<string, int> { [CopperOre] = 60, [IronOre] = 30 };

    /// <summary>
    /// What the first unit's core should hold before it lifts off: it pays for building where it lands (FB-019), so
    /// enough copper for the coal tile's export port and drill, the belt between them (twice the straight run from the
    /// nearest coal to the zone facing home: belts detour round terrain and the drill's footprint) and the wire that
    /// powers the drill from the landed core at the circle (the straight run and a quarter), each with a sixteenth of a
    /// tile to spare; never less than <see cref="MinCarry"/>. The core pays first for everything built at home (lowest
    /// id, OQ-38), so belts of its own fill it until it holds this.
    /// </summary>
    public static Dictionary<string, int> Carry(AiContext c)
    {
        var carry = MinCarry.ToDictionary();
        int target = ExpansionModule.CoalTarget(c);
        if (target < 0)
            return carry;
        var (belt, wire, _, _) = CoalRun(c, target, c.Home);
        const int slack = Simulation.DefaultTileSize / 16;
        var defs = c.Q.Content.Buildings;
        int copper = defs[ExportPort].Cost.GetValueOrDefault(CopperOre) + defs[Drill].Cost.GetValueOrDefault(CopperOre)
            + defs[Conveyor].Cost.GetValueOrDefault(CopperOre) * (2 * belt + slack)
            + defs[Wire].Cost.GetValueOrDefault(CopperOre) * (wire * 5 / 4 + slack);
        carry[CopperOre] = Math.Max(carry[CopperOre], copper);
        return carry;
    }

    /// <summary>
    /// Where the coal tile's export port should go: the buildable cell of <paramref name="tile"/>'s I/O zone facing
    /// <paramref name="home"/> that, with the coal cell nearest it, needs the least copper for the belt between them
    /// (counted twice, as in <see cref="Carry"/>) and the wire from that coal to the victory circle, where the landed
    /// core powers the drill (Manhattan runs). Returns the belt and wire runs and the cell; (0, 0, -1, -1) if the tile
    /// has no such cell or no coal. Cached until the tile changes.
    /// </summary>
    public static (int Belt, int Wire, int X, int Y) CoalRun(AiContext c, int tile, int home) =>
        c.Cached(tile, $"coal_run:{home}", () =>
        {
            var info = c.Tile(tile);
            var zone = c.Q.Cells(tile, 0, 0, info.Width, info.Height).Where(v => v.Zone == home && v.Buildable).ToList();
            var coal = c.Q.DepositCells(tile, Coal);
            (int Belt, int Wire, int X, int Y) best = (0, 0, -1, -1);
            int bestCost = int.MaxValue;
            foreach (var z in zone)
            {
                (int Belt, int Wire) near = (int.MaxValue, 0);
                foreach (var d in coal)
                {
                    int belt = Math.Abs(d.X - z.X) + Math.Abs(d.Y - z.Y);
                    if (belt < near.Belt)
                        near = (belt, Math.Abs(d.X - info.CircleX) + Math.Abs(d.Y - info.CircleY));
                }
                if (near.Belt != int.MaxValue && 2 * near.Belt + near.Wire < bestCost)
                    (bestCost, best) = (2 * near.Belt + near.Wire, (near.Belt, near.Wire, z.X, z.Y));
            }
            return best;
        });

    static IEnumerable<Proposal> Units(AiContext c)
    {
        int h = c.Home;
        if (c.BaseCore(h) == null)
            yield break;
        if (c.Cores(h).FirstOrDefault(b => c.OnFrame(h, b)) is { } core)
        {
            if (c.Q.CheckActivate(h, core.X, core.Y) != AssembleResult.Ok)
            {
                yield return new Proposal($"scrap@{h}:{core.Id}", UnitScore, [new RemoveBuildingCommand(c.Player, h, core.X, core.Y)],
                    "core on frame that can't lift off", Locks: [Proposal.Layout(h)]);
                yield break;
            }
            // Each item short of Carry is belted in by an ore line of its own, which waits until home can pay for it; an
            // item with no room for a line is gone without. Only the first claim waits (the coal tile needs funds);
            // later units go as soon as they can.
            bool filling = false;
            foreach (var (item, n) in c.Owned.Count == 1 ? Carry(c) : [])
            {
                string recipe = item == CopperOre ? "mine_copper_ore" : "mine_iron_ore";
                if (core.Holding.GetValueOrDefault(item) >= n)
                    continue;
                if (c.Lines(h, recipe, core.Id) > 0)
                    filling = true;
                else if (c.Line($"fill_{item}", UnitScore, h, item, [], core, recipe, 1) is { } line)
                {
                    filling = true;
                    yield return line;
                }
            }
            if (!filling)
                yield return new Proposal($"lift@{h}:{core.Id}", UnitScore, [new ActivateUnitCommand(c.Player, h, core.X, core.Y)],
                    $"holding {string.Join(", ", core.Holding.Select(kv => $"{kv.Value} {kv.Key}"))}", Locks: [Proposal.Layout(h)]);
            yield break;
        }
        if (c.Units.Count >= MaxUnits || c.Find(h, Mill) == null || c.Design is not { } design)
            yield break;
        var bp = design.Blueprint;
        var cost = c.Q.BlueprintStats(bp).Cost;
        var (w, hgt) = bp.Size(c.Q.Content.Buildings);
        // The frame search is the costly part: only once the home can pay.
        if (!c.Q.CanAfford(h, cost) || c.Q.FindFrame(h, w, hgt) is not { } spot)
            yield break;
        var result = c.Q.CheckBlueprint(h, bp, spot.X, spot.Y);
        if (result != BlueprintResult.Ok)
            yield break;
        yield return new Proposal($"unit@{h}", NewUnitScore, [new PlaceBlueprintCommand(c.Player, h, bp, spot.X, spot.Y)],
            $"{design.Name} at {spot.X},{spot.Y}", h, cost, [Proposal.Layout(h)]);
    }
}

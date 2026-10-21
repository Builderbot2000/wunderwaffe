using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Production (MP-006, MP-007): the wanted items the economy's single home lines can't make, and units. Each such item
/// (<see cref="AiPlan"/>) is fed into the home core along its chain: a machine of several inputs goes down at home (beside
/// an import port facing the tile one of its inputs comes from, if any), then a line per input into it; an input mined
/// on an owned neighbour comes over a port pair, its line on that tile running into an export port facing home. Units:
/// the seat's design (<see cref="AiContext.Design"/>) built on frame at home once its machines stand (<see cref="AiContext.Advanced"/>)
/// and home can pay for it, its core filled with what the first claim needs, then lifted off.
/// </summary>
public sealed class ProductionModule : IAiModule
{
    public const int ChainScore = 600, UnitScore = 500, NewUnitScore = 480;

    /// <summary>Score steps between wanted items, so one item's whole chain comes before the next one's.</summary>
    const int ItemStep = 10;

    /// <summary>Units the AI keeps at most.</summary>
    public const int MaxUnits = 12;

    public string Name => "production";

    public IEnumerable<Proposal> Propose(AiContext c) => [.. Chains(c), .. Units(c)];

    static IEnumerable<Proposal> Chains(AiContext c)
    {
        if (c.BaseCore(c.Home) is not { } core)
            yield break;
        int score = ChainScore;
        foreach (var d in c.Plan.Items.Where(d => !c.IsHomeLine(d) && d.Chain.Leaves.All(c.Available)))
        {
            foreach (var p in Feed(c, d.Chain, core, d.Item, score))
                yield return p;
            score -= ItemStep;
        }
    }

    /// <summary>Proposals that get <paramref name="chain"/>'s item flowing into <paramref name="sink"/> at home.</summary>
    static IEnumerable<Proposal> Feed(AiContext c, Chain chain, BuildingView sink, string key, int score)
    {
        int h = c.Home;
        if (chain.IsLinear)
        {
            if (c.HasDeposit(h, chain.Deposit))
            {
                if (c.Lines(h, chain.Recipe.Id, sink.Id) == 0 && c.Line(key, score, h, chain.Deposit, chain.Stages, sink) is { } line)
                    yield return line;
                yield break;
            }
            int from = c.ImportTile(chain.Deposit);
            if (from < 0 || c.ImportPort is not { } import)
                yield break;
            if (!c.ImportsInto(h, from, sink.Id) && c.Line($"{key}:import", score, h, null, [new Stage(import.Id)], sink, zone: from) is { } port)
                yield return port;
            foreach (var p in Export(c, chain, from, key, score))
                yield return p;
            yield break;
        }

        // A machine of several inputs: placed with its belt to the sink (beside an import port for an input from a
        // neighbour), then each input fed into it.
        if (c.MachineInto(h, chain.Recipe.Id, sink.Id) is not { } machine)
        {
            var remote = chain.Inputs.FirstOrDefault(i => i.IsLinear && !c.HasDeposit(h, i.Deposit));
            int zone = remote == null ? -1 : c.ImportTile(remote.Deposit);
            Stage stage = new(chain.Building.Id, chain.Recipe.Id);
            IReadOnlyList<Stage> stages = zone >= 0 && c.ImportPort is { } import ? [new Stage(import.Id), stage] : [stage];
            if (c.Line(key, score, h, null, stages, sink, zone: stages.Count > 1 ? zone : -1) is { } line)
                yield return line;
            yield break;
        }
        int step = 1;
        foreach (var input in chain.Inputs)
        {
            foreach (var p in Feed(c, input, machine, $"{key}/{input.Item}", score - step))
                yield return p;
            step++;
        }
    }

    /// <summary>On tile <paramref name="from"/>: an export port facing home, then the chain's line into it.</summary>
    static IEnumerable<Proposal> Export(AiContext c, Chain chain, int from, string key, int score)
    {
        int h = c.Home;
        var export = c.Buildings[from].Values.FirstOrDefault(b => b.Owner == c.Player && c.Kind(b) == Content.BuildingKind.ExportPort && b.Partner == h);
        if (export == null)
        {
            if (c.ExportPort is { } def)
            {
                var (_, _, x, y) = ExportRun(c, from, h, chain.Deposit);
                // Out of reach (WM-015): a core toward where the port would go.
                if ((c.Place($"{key}:export", score, from, def.Id, c.Q.FindSpot(from, def.Id, nearX: x, nearY: y, zone: h))
                     ?? (c.Q.FindSpot(from, def.Id, nearX: x, nearY: y, zone: h, anywhere: true) is { } far
                         ? c.Reach($"{key}:export", score, from, [(far.X, far.Y, def.Footprint(0))], (far.X, far.Y)) : null)) is { } p)
                    yield return p;
            }
            yield break;
        }
        if (c.Lines(from, chain.Recipe.Id, export.Id) == 0 && c.Line($"{key}:ship", score, from, chain.Deposit, chain.Stages, export) is { } line)
            yield return line;
    }

    /// <summary>
    /// The least the first unit's core holds before it lifts off: three times the cost of a core, an export port and the
    /// extractor of the heaviest raw item, for the lines it lays where it lands.
    /// </summary>
    public static Dictionary<string, int> MinCarry(AiContext c)
    {
        var defs = new List<IReadOnlyDictionary<string, int>> { c.CoreDef.Cost };
        if (c.ExportPort is { } export)
            defs.Add(export.Cost);
        if (c.Plan.Raw.FirstOrDefault() is { } raw)
            defs.Add(raw.Chain.Building.Cost);
        return AiContext.Scale(AiContext.Sum(defs), 3);
    }

    /// <summary>
    /// What the first unit's core should hold before it lifts off: it pays for building where it lands (FB-019), so
    /// enough for the target tile's export port and extractor of the missing deposit (<see cref="ExpansionModule.Target"/>),
    /// the belt between them (twice the straight run from the nearest deposit cell to the zone facing home: belts detour
    /// round terrain and the extractor's footprint) and the wire that powers the extractor from the landed core at the
    /// circle (the straight run and a quarter), each with a sixteenth of a tile to spare; never less than
    /// <see cref="MinCarry"/>. The core pays first for everything built at home (lowest id, OQ-38), so belts of its own
    /// fill it until it holds this.
    /// </summary>
    public static Dictionary<string, int> Carry(AiContext c)
    {
        var carry = MinCarry(c);
        if (ExpansionModule.Target(c) is not (>= 0 and var target, { } item) || Roles.Extractor(c.Q.Content, item) is not { } ex)
            return carry;
        var (belt, wire, _, _) = ExportRun(c, target, c.Home, item);
        const int slack = Simulation.DefaultTileSize / 16;
        var parts = new List<IReadOnlyDictionary<string, int>> { ex.Building.Cost };
        if (c.ExportPort is { } export)
            parts.Add(export.Cost);
        if (Roles.Cheapest(c.Q.Content, Content.BuildingKind.Conveyor) is { } conveyor)
            parts.Add(AiContext.Scale(conveyor.Cost, 2 * belt + slack));
        if (Roles.Cheapest(c.Q.Content, Content.BuildingKind.Wire) is { } w)
            parts.Add(AiContext.Scale(w.Cost, wire * 5 / 4 + slack));
        foreach (var (id, n) in AiContext.Sum(parts))
            carry[id] = Math.Max(carry.GetValueOrDefault(id), n);
        return carry;
    }

    /// <summary>
    /// Where <paramref name="tile"/>'s export port for <paramref name="item"/> should go: the buildable cell of its I/O zone
    /// facing <paramref name="home"/> that, with the deposit cell nearest it, needs the least for the belt between them
    /// (counted twice, as in <see cref="Carry"/>) and the wire from that deposit to the victory circle, where the landed
    /// core powers the extractor (Manhattan runs). Returns the belt and wire runs and the cell; (0, 0, -1, -1) if the tile
    /// has no such cell or no deposit. Cached until the tile changes.
    /// </summary>
    public static (int Belt, int Wire, int X, int Y) ExportRun(AiContext c, int tile, int home, string item) =>
        c.Cached(tile, $"export_run:{home}:{item}", () =>
        {
            var info = c.Tile(tile);
            var zone = c.Q.Cells(tile, 0, 0, info.Width, info.Height).Where(v => v.Zone == home && v.Buildable).ToList();
            var deposit = c.Q.DepositCells(tile, item);
            (int Belt, int Wire, int X, int Y) best = (0, 0, -1, -1);
            int bestCost = int.MaxValue;
            foreach (var z in zone)
            {
                (int Belt, int Wire) near = (int.MaxValue, 0);
                foreach (var d in deposit)
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
            // Each item short of Carry is belted in by a line of its own, which waits until home can pay for it; an item
            // the home can't make in one line is gone without. Only the first claim waits (the target tile needs funds);
            // later units go as soon as they can.
            bool filling = false;
            foreach (var (item, n) in c.Owned.Count == 1 ? Carry(c).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList() : [])
            {
                if (core.Holding.GetValueOrDefault(item) >= n || c.Plan.Of(item) is not { } d || !c.IsHomeLine(d))
                    continue;
                if (c.Lines(h, d.Chain.Recipe.Id, core.Id) > 0)
                    filling = true;
                else if (c.Line($"fill_{item}", UnitScore, h, d.Chain.Deposit, d.Chain.Stages, core) is { } line)
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
        if (c.Units.Count >= MaxUnits || !c.Advanced || c.Design is not { } design)
            yield break;
        var bp = design.Blueprint;
        var cost = design.Cost;
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

using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// How one item gets made (MP-007): a recipe on its machine, and a chain per input (by input id). An extraction step has
/// no inputs and needs a deposit of <see cref="Item"/>.
/// </summary>
public sealed record Chain(string Item, RecipeDef Recipe, BuildingDef Building, IReadOnlyList<Chain> Inputs)
{
    public bool IsExtraction => Inputs.Count == 0;

    /// <summary>One input per step down to a deposit: what <see cref="Planner.PlanLine"/> lays as one line.</summary>
    public bool IsLinear => IsExtraction || (Inputs.Count == 1 && Inputs[0].IsLinear);

    /// <summary>The deposit at the bottom of a linear chain.</summary>
    public string Deposit => IsExtraction ? Item : Inputs[0].Deposit;

    /// <summary>The crafting stages of a linear chain, first to last (the extractor is the line's start, not a stage).</summary>
    public IReadOnlyList<Stage> Stages => IsExtraction ? [] : [.. Inputs[0].Stages, new Stage(Building.Id, Recipe.Id)];

    /// <summary>Every deposit the chain needs, by id.</summary>
    public IEnumerable<string> Leaves => IsExtraction ? [Item] : Inputs.SelectMany(i => i.Leaves).Distinct().Order(StringComparer.Ordinal);

    /// <summary>This step and every step under it.</summary>
    public IEnumerable<Chain> Steps => Inputs.SelectMany(i => i.Steps).Prepend(this);
}

/// <summary>An item the seat wants in its base core: how much the wanted things ask for in all, and how it is made.</summary>
public sealed record Demand(string Item, int Weight, Chain Chain);

/// <summary>
/// What the seat's economy is for (MP-007, OQ-50), worked out from content each think with no item, recipe or building
/// named in code. Demand starts from what the AI wants to build (its unit design's cost, the current goal's item) and
/// grows by the cost of every building the plan needs (the core, conveyor, wire, ports, the machines of each chain) until
/// nothing new turns up. Each wanted item is expanded backwards through the recipe graph to deposits
/// (<see cref="Chain"/>): extraction where the seat has the deposit, else the recipe needing fewest missing deposits,
/// then fewest steps, then by id. Deposits a chosen chain needs that neither home nor an owned neighbour of home holds are
/// <see cref="Missing"/>: expansion goes for tiles holding them.
/// </summary>
public sealed class AiPlan
{
    /// <summary>Rounds of growing the demand by building costs before it stops.</summary>
    const int Rounds = 8;

    /// <summary>Wanted items in the base core, heaviest first, ties by id; items nothing can make are left out.</summary>
    public IReadOnlyList<Demand> Items { get; }

    /// <summary>Deposits the chosen chains need that the seat can't reach yet, by id.</summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>The wanted items that are mined straight from a deposit, heaviest first.</summary>
    public IReadOnlyList<Demand> Raw => [.. Items.Where(d => d.Chain.IsExtraction)];

    public Demand? Of(string item) => Items.FirstOrDefault(d => d.Item == item);

    AiPlan(IReadOnlyList<Demand> items, IReadOnlyList<string> missing) => (Items, Missing) = (items, missing);

    /// <summary>
    /// The plan for wanting <paramref name="wanted"/> (item amounts) and <paramref name="goals"/> (items, no weight), with
    /// <paramref name="available"/> telling which deposits the seat can mine.
    /// </summary>
    public static AiPlan Build(ContentDb content, IReadOnlyDictionary<string, int> wanted, IEnumerable<string> goals, Func<string, bool> available)
    {
        var memo = new Dictionary<string, (Chain? Chain, int Missing, int Depth)>();
        BuildingDef?[] always =
        [
            content.DefaultCore, Roles.Cheapest(content, BuildingKind.Conveyor), Roles.Cheapest(content, BuildingKind.Wire),
            Roles.Cheapest(content, BuildingKind.ExportPort), Roles.Cheapest(content, BuildingKind.ImportPort),
        ];
        var items = new SortedSet<string>(wanted.Keys.Concat(goals), StringComparer.Ordinal);
        var chains = new SortedDictionary<string, Chain>(StringComparer.Ordinal);
        var used = new SortedDictionary<string, BuildingDef>(StringComparer.Ordinal);
        for (int round = 0; round < Rounds; round++)
        {
            foreach (string item in items)
            {
                if (!chains.ContainsKey(item) && Best(content, item, available, memo, []).Chain is { } chain)
                    chains[item] = chain;
            }
            foreach (var b in always.OfType<BuildingDef>().Concat(chains.Values.SelectMany(c => c.Steps).Select(s => s.Building)))
                used[b.Id] = b;
            int before = items.Count;
            foreach (var b in used.Values)
                items.UnionWith(b.Cost.Keys);
            if (items.Count == before)
                break;
        }

        var weight = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, n) in wanted.Concat(used.Values.SelectMany(b => b.Cost)))
            weight[id] = weight.GetValueOrDefault(id) + n;
        var demand = chains.Select(kv => new Demand(kv.Key, weight.GetValueOrDefault(kv.Key), kv.Value))
            .OrderByDescending(d => d.Weight).ThenBy(d => d.Item, StringComparer.Ordinal).ToList();
        var missing = demand.SelectMany(d => d.Chain.Leaves).Where(l => !available(l)).Distinct().Order(StringComparer.Ordinal).ToList();
        return new AiPlan(demand, missing);
    }

    /// <summary>The best chain for <paramref name="item"/>, with its count of missing deposits and its depth.</summary>
    static (Chain? Chain, int Missing, int Depth) Best(ContentDb content, string item, Func<string, bool> available,
        Dictionary<string, (Chain?, int, int)> memo, HashSet<string> visiting)
    {
        if (memo.TryGetValue(item, out var known))
            return known;
        if (!visiting.Add(item))
            return (null, 0, 0);
        (Chain? Chain, int Missing, int Depth) best = (null, int.MaxValue, int.MaxValue);
        if (Roles.Extractor(content, item) is { } ex)
            best = (new Chain(item, ex.Recipe, ex.Building, []), available(item) ? 0 : 1, 0);
        foreach (var r in Roles.Crafting(content, item))
        {
            var inputs = new List<Chain>();
            var leaves = new SortedSet<string>(StringComparer.Ordinal);
            int depth = 0;
            foreach (string input in r.Inputs.Keys.Order(StringComparer.Ordinal))
            {
                if (Best(content, input, available, memo, visiting) is not { Chain: { } sub, Depth: var d })
                {
                    inputs.Clear();
                    break;
                }
                inputs.Add(sub);
                leaves.UnionWith(sub.Leaves);
                depth = Math.Max(depth, d + 1);
            }
            if (inputs.Count != r.Inputs.Count)
                continue;
            int missing = leaves.Count(l => !available(l));
            if (missing < best.Missing || (missing == best.Missing && depth < best.Depth))
                best = (new Chain(item, r, content.Buildings[r.Building], inputs), missing, depth);
        }
        visiting.Remove(item);
        if (best.Chain == null)
            best = (null, 0, 0);
        memo[item] = best;
        return best;
    }
}

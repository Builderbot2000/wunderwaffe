using Wunderwaffe.Sim.Ai.Modules;
using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// One AI seat on the AI framework (MP-002, MP-006, OQ-48). It plays through the shared interface only (NF-009,
/// OQ-33): it reads the match through <see cref="GameQuery"/> and acts by returning ordinary <see cref="Command"/>s,
/// which the host enqueues like any player's, so its moves land in the command log, replay from saves and travel
/// through lockstep like a human's.
/// <para>
/// <see cref="Init"/> runs when the seat gets the AI (match start, load, takeover of a leaver) and works from whatever
/// the state is. Each <see cref="Think"/> observes (<see cref="AiContext"/>), runs the strategic layer
/// (<see cref="InfluenceMap"/>: a goal per tile), asks every module for proposals and issues the best within the
/// command budget: highest weighted score first, ties by module order, then by key; a proposal is skipped when one
/// already chosen holds one of its locks or when the funds left on its tile can't pay it. It remembers nothing that the
/// state can't give again, so decisions are deterministic for a state and saving, loading or a takeover change none.
/// </para>
/// </summary>
public sealed class AiPlayer(int player, AiConfig? config = null)
{
    /// <summary>Ticks between decisions. Seats are staggered so AIs don't all think on one tick.</summary>
    public const int ThinkEvery = Simulation.TicksPerSecond / 2;

    readonly PlanCache _plans = new();

    public int Player { get; } = player;

    public AiConfig Config { get; set; } = config ?? AiConfig.Default;

    /// <summary>The modules in tie-break order.</summary>
    public IReadOnlyList<IAiModule> Modules { get; init; } = AiModules.Default();

    /// <summary>The unit design picked from the prefabs at <see cref="Init"/>; null if none fits.</summary>
    public AiDesign? Design { get; private set; }

    /// <summary>True once <see cref="Init"/> has run.</summary>
    public bool Initialised { get; private set; }

    /// <summary>The latest think's debug log, or null before the first.</summary>
    public ThinkReport? Last { get; private set; }

    /// <summary>Called with every think's report: a running debug log for tuning (MP-006). Null by default.</summary>
    public Action<ThinkReport>? Log { get; set; }

    /// <summary>
    /// Takes up the seat from the current state: picks the designs and drops every cache. <see cref="Think"/> calls it
    /// the first time if nobody has.
    /// </summary>
    public void Init(Simulation sim)
    {
        _plans.Clear();
        Design = AiDesigns.Pick(new GameQuery(sim, Player));
        Initialised = true;
    }

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
        if (!Initialised)
            Init(sim);
        var q = new GameQuery(sim, Player);
        if (q.Match().Over || q.Players().FirstOrDefault(p => p.Id == Player) is not { Eliminated: false })
            return [];
        var c = new AiContext(q, _plans, Design);
        if (c.Home < 0)
            return [];
        c.Influence = InfluenceMap.Build(c);

        var all = new List<(int Module, Proposal P, int Score)>();
        var records = new List<ProposalRecord>();
        for (int m = 0; m < Modules.Count; m++)
        {
            var module = Modules[m];
            int weight = Config.WeightOf(module.Name);
            if (weight <= 0)
                continue;
            foreach (var p in module.Propose(c))
                all.Add((m, p, (int)((long)p.Score * weight / 100)));
        }

        var commands = new List<Command>();
        var locks = new HashSet<string>();
        var funds = new Dictionary<int, Dictionary<string, long>>();
        foreach (var (m, p, score) in all.OrderByDescending(a => a.Score).ThenBy(a => a.Module).ThenBy(a => a.P.Key, StringComparer.Ordinal))
        {
            var outcome = Choose(q, p, commands.Count, locks, funds);
            if (outcome == ProposalOutcome.Chosen)
                commands.AddRange(p.Commands);
            records.Add(new ProposalRecord(Modules[m].Name, p.Key, score, outcome, p.Why, p.Commands.Count));
        }
        Last = new ThinkReport(sim.Tick, Player, c.Influence, records);
        Log?.Invoke(Last);
        return commands;
    }

    ProposalOutcome Choose(GameQuery q, Proposal p, int used, HashSet<string> locks, Dictionary<int, Dictionary<string, long>> funds)
    {
        if (p.Locks is { } want && want.Any(locks.Contains))
            return ProposalOutcome.Locked;
        if (used > 0 && used + p.Commands.Count > Config.Budget)
            return ProposalOutcome.OverBudget;
        if (p.Cost is { Count: > 0 } cost)
        {
            if (!funds.TryGetValue(p.CostTile, out var left))
                funds[p.CostTile] = left = q.Inventory(p.CostTile).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (cost.Any(kv => left.GetValueOrDefault(kv.Key) < kv.Value))
                return ProposalOutcome.Unaffordable;
            foreach (var (id, n) in cost)
                left[id] -= n;
        }
        foreach (string l in p.Locks ?? [])
            locks.Add(l);
        return ProposalOutcome.Chosen;
    }
}

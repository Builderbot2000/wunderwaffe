using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// One action a module offers (MP-006): commands that have already passed the sim's checks, with an integer score
/// (higher first). <paramref name="Key"/> is stable across runs (it breaks score ties after module order and names the
/// proposal in the log). <paramref name="Cost"/> is paid on <paramref name="CostTile"/>: the framework only takes
/// proposals that the funds left there after the better ones still cover. <paramref name="Locks"/> name what the
/// proposal uses up (a tile's layout, a unit): two proposals sharing a lock never go out on one think.
/// </summary>
public sealed record Proposal(
    string Key, int Score, IReadOnlyList<Command> Commands, string Why = "",
    int CostTile = -1, IReadOnlyDictionary<string, int>? Cost = null, IReadOnlyList<string>? Locks = null)
{
    /// <summary>The lock for placing or removing buildings on a tile: one layout change per tile per think.</summary>
    public static string Layout(int tile) => $"layout:{tile}";

    /// <summary>The lock for giving a unit an order.</summary>
    public static string UnitLock(int unit) => $"unit:{unit}";
}

/// <summary>
/// Per-seat tuning (MP-006): module weights in percent (a missing module is 100, 0 switches it off) and the command
/// budget of one think. Plain data, so a seat's settings can come from a match file later.
/// </summary>
public sealed record AiConfig(IReadOnlyDictionary<string, int>? Weights = null, int Budget = AiConfig.DefaultBudget)
{
    public const int DefaultBudget = 40;

    public static readonly AiConfig Default = new();

    public int WeightOf(string module) => Weights?.GetValueOrDefault(module, 100) ?? 100;
}

/// <summary>What became of one proposal on a think: chosen, or why not.</summary>
public enum ProposalOutcome { Chosen, Locked, Unaffordable, OverBudget, Off }

/// <summary>A proposal as the debug log shows it: module, key, weighted score, outcome, reason.</summary>
public sealed record ProposalRecord(string Module, string Key, int Score, ProposalOutcome Outcome, string Why, int Commands);

/// <summary>
/// The debug log of one think (MP-006 "observable"): the strategic layer's tile goals, every proposal with its score
/// and what was chosen. <see cref="AiPlayer.Last"/> keeps the latest; <see cref="Describe"/> prints it.
/// </summary>
public sealed record ThinkReport(long Tick, int Player, IReadOnlyList<TileInfluence> Goals, IReadOnlyList<ProposalRecord> Proposals)
{
    public IEnumerable<ProposalRecord> Chosen => Proposals.Where(p => p.Outcome == ProposalOutcome.Chosen);

    public string Describe()
    {
        var lines = new List<string> { $"AI {Player} at tick {Tick}" };
        lines.Add("  goals: " + string.Join(", ", Goals.Where(g => g.Goal != TileGoal.None).Select(g => $"{g.Tile}:{g.Goal}({g.Priority})")));
        foreach (var p in Proposals)
            lines.Add($"  {(p.Outcome == ProposalOutcome.Chosen ? "*" : " ")} {p.Score,6} {p.Module}/{p.Key} [{p.Outcome}] {p.Why}");
        return string.Join("\n", lines);
    }
}

using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Economy (MP-006, MP-007): clears machines whose output goes nowhere, keeps a home core off the frame, and runs a line
/// into the home core for every wanted item the home can make in one line (<see cref="AiContext.IsHomeLine"/>), heaviest
/// demand first, as many as <see cref="AiContext.Want"/>; each outpost gets a line of every raw wanted item it holds into
/// its own core. Outposts the strategic layer marks Reinforce (under threat) come after everything else.
/// </summary>
public sealed class EconomyModule : IAiModule
{
    public const int CleanScore = 900, BaseScore = 800, HomeScore = 700, OutpostScore = 300, ThreatenedScore = 100;

    public string Name => "economy";

    public IEnumerable<Proposal> Propose(AiContext c)
    {
        // A machine whose output goes nowhere (a line cut short) comes away: a full refund.
        foreach (int t in c.Owned)
        {
            foreach (var (id, outlet) in c.Outlets[t])
            {
                var b = c.Buildings[t][id];
                if (outlet < 0 && c.IsProducer(b))
                    yield return new Proposal($"clean@{t}:{id}", CleanScore, [new RemoveBuildingCommand(c.Player, t, b.X, b.Y)],
                        $"{b.DefId} feeds nothing", Locks: [Proposal.Layout(t)]);
            }
        }

        int h = c.Home;
        // A home core off the frame, so the home keeps funds when the starting unit lifts off (FB-019).
        if (c.BaseCore(h) is not { } core)
        {
            if (c.Place("base", BaseScore, h, c.CoreDef.Id, c.Q.FindSpot(h, c.CoreDef.Id)) is { } p)
                yield return p;
        }
        else
        {
            int score = HomeScore;
            foreach (var d in c.Plan.Items.Where(c.IsHomeLine))
            {
                if (c.Lines(h, d.Chain.Recipe.Id, core.Id) < c.Want(d) && c.Line(d.Item, score, h, d.Chain.Deposit, d.Chain.Stages, core) is { } line)
                    yield return line;
                score--;
            }
        }

        // Every other tile: a line of each raw wanted item into its first core, paid from what the landed unit brought.
        foreach (int t in c.Owned)
        {
            if (t == h || c.Cores(t).FirstOrDefault() is not { } outpost)
                continue;
            int score = c.GoalOf(t) == TileGoal.Reinforce ? ThreatenedScore : OutpostScore;
            foreach (var d in c.Plan.Raw)
            {
                if (c.Lines(t, d.Chain.Recipe.Id, outpost.Id) < 1 && c.Line($"outpost_{d.Item}", score, t, d.Item, [], outpost) is { } line)
                    yield return line;
                score--;
            }
        }
    }
}

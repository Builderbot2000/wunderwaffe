using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using static Wunderwaffe.Sim.Ai.AiContext;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Economy (MP-006): clears machines whose output goes nowhere, keeps a home core off the frame, runs ore and plate
/// lines into the home core and a copper and iron line into each outpost's core. Outposts the strategic layer marks
/// Reinforce (under threat) come after everything else.
/// </summary>
public sealed class EconomyModule : IAiModule
{
    public const int CleanScore = 900, BaseScore = 800, HomeScore = 700, OutpostScore = 300, ThreatenedScore = 100;

    public string Name => "economy";

    public IEnumerable<Proposal> Propose(AiContext c)
    {
        // A drill or smelter whose output goes nowhere (a line cut short) comes away: a full refund.
        foreach (int t in c.Owned)
        {
            foreach (var (id, outlet) in c.Outlets[t])
            {
                var b = c.Buildings[t][id];
                if (outlet < 0)
                    yield return new Proposal($"clean@{t}:{id}", CleanScore, [new RemoveBuildingCommand(c.Player, t, b.X, b.Y)],
                        $"{b.DefId} feeds nothing", Locks: [Proposal.Layout(t)]);
            }
        }

        int h = c.Home;
        // A home core off the frame, so the home keeps funds when the starting unit lifts off (FB-019).
        if (c.BaseCore(h) is not { } core)
        {
            if (c.Place("base", BaseScore, h, CoreDef, c.Q.FindSpot(h, CoreDef)) is { } p)
                yield return p;
        }
        else
        {
            int extra = c.Find(h, Mill) != null ? 1 : 0;
            Proposal?[] lines =
            [
                c.Line("copper", HomeScore, h, CopperOre, [], core, "mine_copper_ore", 2 + extra),
                c.Line("iron_plate", HomeScore - 1, h, IronOre, [new Stage(Smelter, "smelt_iron")], core, "smelt_iron", 2 + extra),
                c.Line("iron", HomeScore - 2, h, IronOre, [], core, "mine_iron_ore", 1 + extra),
                c.Line("copper_plate", HomeScore - 3, h, CopperOre, [new Stage(Smelter, "smelt_copper")], core, "smelt_copper", 1 + extra),
            ];
            foreach (var line in lines)
            {
                if (line != null)
                    yield return line;
            }
        }

        // Every other tile: a copper and an iron ore line into its first core, paid from what the landed unit brought.
        foreach (int t in c.Owned)
        {
            if (t == h || c.Cores(t).FirstOrDefault() is not { } outpost)
                continue;
            int score = c.GoalOf(t) == TileGoal.Reinforce ? ThreatenedScore : OutpostScore;
            if (c.Line("outpost_copper", score, t, CopperOre, [], outpost, "mine_copper_ore", 1) is { } copper)
                yield return copper;
            if (c.Line("outpost_iron", score - 1, t, IronOre, [], outpost, "mine_iron_ore", 1) is { } iron)
                yield return iron;
        }
    }
}

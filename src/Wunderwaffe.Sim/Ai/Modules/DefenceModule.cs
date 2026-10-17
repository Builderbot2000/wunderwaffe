using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Defence (MP-006): sends idle units to the seat's tiles the strategic layer marks Reinforce (more enemy strength on or
/// next to them than its own), most threatened first, all together with an attack-move to the tile's centre. A lone
/// unit stays put: it is the next claim's scout.
/// </summary>
public sealed class DefenceModule : IAiModule
{
    public const int Score = 450;

    public string Name => "defence";

    public IEnumerable<Proposal> Propose(AiContext c)
    {
        var idle = c.IdleUnits().ToList();
        if (idle.Count < 2)
            yield break;
        foreach (var goal in c.WithGoal(TileGoal.Reinforce))
        {
            var tile = c.Tiles[goal.Tile];
            var (_, ox, oy) = c.Facts(goal.Tile);
            int[] go = [.. idle.Where(u => u.TileId != goal.Tile).Select(u => u.Id)];
            if (go.Length == 0)
                continue;
            yield return new Proposal($"reinforce:{goal.Tile}", Score + Math.Min(goal.Priority, 49),
                [new AttackMoveCommand(c.Player, go, goal.Tile, tile.CenterX - ox, tile.CenterY - oy)],
                $"threat {goal.Threat} vs {goal.Own} on {goal.Tile}", Locks: [.. go.Select(Proposal.UnitLock)]);
        }
    }
}

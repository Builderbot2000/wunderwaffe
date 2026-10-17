using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai;

/// <summary>What the strategic layer wants with a tile (MP-006). Module scores read it.</summary>
public enum TileGoal
{
    None,

    /// <summary>An unclaimed neighbour of the seat's land: worth a claim.</summary>
    Expand,

    /// <summary>The seat's own tile, not under threat: keep building.</summary>
    Hold,

    /// <summary>The seat's own tile with more enemy strength on or next to it than its own: send units.</summary>
    Reinforce,

    /// <summary>A live enemy's tile: a target for attack groups.</summary>
    Attack,
}

/// <summary>
/// One tile on the influence map: the seat's and its enemies' strength there (unit HP), the threat (enemy strength on
/// the tile plus half of that on its neighbours), its value (buildable share and deposits), and the goal the
/// strategic layer set, with a priority among tiles of the same goal (higher first).
/// </summary>
public sealed record TileInfluence(int Tile, int Owner, int Own, int Enemy, int Threat, int Value, TileGoal Goal, int Priority);

/// <summary>
/// The strategic layer (MP-006, OQ-48): an influence map over the tile graph, rebuilt from the state on every think
/// (no memory), that sets a <see cref="TileGoal"/> per tile. Integer maths only, tiles in id order.
/// </summary>
public static class InfluenceMap
{
    /// <summary>The tile's value as a claim target: buildable share in percent plus deposit cells / 20.</summary>
    public static int Value(TileInfo t) => t.Buildable * 100 / Math.Max(1, t.Area) + t.Deposits.Values.Sum() / 20;

    public static IReadOnlyList<TileInfluence> Build(AiContext c)
    {
        var q = c.Q;
        var tiles = c.Tiles;
        var enemies = q.Players().Where(p => p.Id != c.Player && !p.Eliminated).Select(p => p.Id).ToHashSet();
        var own = new int[tiles.Count];
        var enemy = new int[tiles.Count];
        foreach (var u in q.Units())
        {
            if (u.TileId < 0 || u.TileId >= tiles.Count)
                continue;
            if (u.Owner == c.Player)
                own[u.TileId] += u.Hp;
            else if (enemies.Contains(u.Owner))
                enemy[u.TileId] += u.Hp;
        }

        var (hx, hy) = (tiles[c.Home].CenterX, tiles[c.Home].CenterY);
        var mine = c.Owned.ToHashSet();
        var result = new List<TileInfluence>(tiles.Count);
        foreach (var t in tiles)
        {
            int threat = enemy[t.Id];
            foreach (int n in t.Neighbors)
                threat += enemy[n] / 2;
            TileGoal goal;
            int value = 0, priority = 0;
            if (t.Owner == c.Player)
            {
                value = c.Value(t.Id);
                bool threatened = threat > 0 && threat > own[t.Id];
                goal = threatened ? TileGoal.Reinforce : TileGoal.Hold;
                priority = threatened ? threat - own[t.Id] : value;
            }
            else if (t.Owner == TileState.Unowned && t.Neighbors.Any(mine.Contains))
            {
                value = c.Value(t.Id);
                goal = TileGoal.Expand;
                priority = value;
            }
            else if (enemies.Contains(t.Owner))
            {
                long dx = t.CenterX - hx, dy = t.CenterY - hy;
                goal = TileGoal.Attack;
                priority = 1_000_000 - (int)IntMath.ISqrt(dx * dx + dy * dy) - enemy[t.Id] / 10;
            }
            else
            {
                goal = TileGoal.None;
            }
            result.Add(new TileInfluence(t.Id, t.Owner, own[t.Id], enemy[t.Id], threat, value, goal, priority));
        }
        return result;
    }
}

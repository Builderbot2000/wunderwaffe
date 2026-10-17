using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Ai.AiContext;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Expansion (MP-006): claims tiles the way a player does (WM-006). A unit flies to the victory circle of the best
/// tile the strategic layer marks Expand and lands there; its blocks hold the circle until the timer fills. The coal
/// tile first, then, once steel flows, any Expand tile by its priority. One claim at a time.
/// </summary>
public sealed class ExpansionModule : IAiModule
{
    public const int LandScore = 950, SendScore = 400;

    /// <summary>Added to a home neighbour with coal while the seat has none: steel needs it.</summary>
    public const int CoalBonus = 1000;

    public string Name => "expansion";

    /// <summary>The first claim's likely target: the best Expand tile next to home with coal, or -1.</summary>
    public static int CoalTarget(AiContext c) =>
        c.WithGoal(TileGoal.Expand).Where(g => c.Tiles[c.Home].Neighbors.Contains(g.Tile) && c.HasDeposit(g.Tile, Coal))
            .Select(g => g.Tile).DefaultIfEmpty(-1).First();

    public IEnumerable<Proposal> Propose(AiContext c)
    {
        var units = c.Units.Where(u => u.MaxSpeed > 0).ToList();
        // A unit standing in the circle of a tile the seat doesn't own lands there.
        foreach (var u in units.Where(u => !u.Moving))
        {
            var (_, ox, oy) = c.Facts(u.TileId);
            if (c.Tiles[u.TileId].Owner != c.Player && c.Q.InCircle(u.TileId, u.CellX - ox, u.CellY - oy)
                && c.Q.CheckDeploy(u.Id) == DeployResult.Ok)
                yield return new Proposal($"land:{u.Id}", LandScore, [new DeployUnitCommand(c.Player, u.Id)], $"in the circle of {u.TileId}",
                    Locks: [Proposal.UnitLock(u.Id)]);
        }

        int h = c.Home;
        bool haveCoal = c.Owned.Any(t => t != h && c.HasDeposit(t, Coal));
        bool steel = c.Find(h, Mill) != null;
        int baseCore = c.BaseCore(h)?.Id ?? -1;
        bool basics = c.Lines(h, "mine_copper_ore", baseCore) >= 2 && c.Lines(h, "smelt_iron", baseCore) >= 1;
        if (!basics || (haveCoal && !steel))
            yield break;
        // One claim at a time: a unit on its way to a circle, or a circle being held, waits for the outcome.
        var tiles = c.Tiles;
        if (units.Any(u => u.Moving && u.DestTileId >= 0 && tiles[u.DestTileId].Owner != c.Player && c.Q.InCircle(u.DestTileId, u.DestX, u.DestY))
            || tiles.Any(t => t.Owner != c.Player && c.Q.ClaimProgress(t.Id) > 0))
            yield break;
        if (units.Where(u => !u.Moving).OrderBy(u => u.Id).FirstOrDefault() is not { } scout)
            yield break;

        var homeNeighbors = c.Tiles[h].Neighbors;
        var best = c.WithGoal(TileGoal.Expand)
            .Select(g => (g.Tile, g.Priority))
            .Where(t => haveCoal || (homeNeighbors.Contains(t.Tile) && c.HasDeposit(t.Tile, Coal)))
            .OrderByDescending(t => (!haveCoal && c.HasDeposit(t.Tile, Coal) ? CoalBonus : 0) + t.Priority)
            .ThenBy(t => t.Tile)
            .Select(t => c.Tile(t.Tile))
            .FirstOrDefault(t => c.Q.Travel(scout.Id, t.Id, t.CircleX, t.CircleY) != null);
        if (best is { } target)
            yield return new Proposal($"claim:{target.Id}", SendScore, [new MoveUnitsCommand(c.Player, [scout.Id], target.Id, target.CircleX, target.CircleY)],
                $"unit {scout.Id} to the circle of {target.Id}", Locks: [Proposal.UnitLock(scout.Id)]);
    }
}

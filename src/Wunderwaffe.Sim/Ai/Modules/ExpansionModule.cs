using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Expansion (MP-006, MP-007): claims tiles the way a player does (WM-006). A unit flies to the victory circle of the best
/// tile the strategic layer marks Expand and lands there; its blocks hold the circle until the timer fills. While the
/// plan misses a deposit (<see cref="AiPlan.Missing"/>), only home's neighbours holding one are targets (an import
/// needs a port pair with home); once nothing is missing and the machines stand (<see cref="AiContext.Advanced"/>), any
/// Expand tile by its priority. One claim at a time, and only once every home line runs.
/// </summary>
public sealed class ExpansionModule : IAiModule
{
    public const int LandScore = 950, SendScore = 400;

    public string Name => "expansion";

    /// <summary>The Expand tiles next to home holding a deposit the plan misses, best first.</summary>
    public static IEnumerable<int> MissingTargets(AiContext c) =>
        c.WithGoal(TileGoal.Expand)
            .Where(g => c.Tiles[c.Home].Neighbors.Contains(g.Tile) && c.Plan.Missing.Any(m => c.HasDeposit(g.Tile, m)))
            .Select(g => g.Tile);

    /// <summary>The first claim's likely target and the missing deposit it is for, or (-1, null).</summary>
    public static (int Tile, string? Item) Target(AiContext c) =>
        MissingTargets(c).Select(t => (t, (string?)c.Plan.Missing.First(m => c.HasDeposit(t, m)))).DefaultIfEmpty((-1, null)).First();

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
        var missing = c.Plan.Missing;
        bool basics = c.BaseCore(h) is { } core && c.Plan.Items.Where(c.IsHomeLine).All(d => c.Lines(h, d.Chain.Recipe.Id, core.Id) > 0);
        if (!basics || (missing.Count == 0 && !c.Advanced))
            yield break;
        // One claim at a time: a unit on its way to a circle, or a circle being held, waits for the outcome.
        var tiles = c.Tiles;
        if (units.Any(u => u.Moving && u.DestTileId >= 0 && tiles[u.DestTileId].Owner != c.Player && c.Q.InCircle(u.DestTileId, u.DestX, u.DestY))
            || tiles.Any(t => t.Owner != c.Player && c.Q.ClaimProgress(t.Id) > 0))
            yield break;
        if (units.Where(u => !u.Moving).OrderBy(u => u.Id).FirstOrDefault() is not { } scout)
            yield break;

        // Missing deposits first; with none next to home, any tile rather than none.
        var wanted = MissingTargets(c).ToList();
        var targets = wanted.Count > 0 ? wanted : c.WithGoal(TileGoal.Expand).Select(g => g.Tile);
        if (targets.Select(c.Tile).FirstOrDefault(t => c.Q.Travel(scout.Id, t.Id, t.CircleX, t.CircleY) != null) is { } target)
            yield return new Proposal($"claim:{target.Id}", SendScore, [new MoveUnitsCommand(c.Player, [scout.Id], target.Id, target.CircleX, target.CircleY)],
                $"unit {scout.Id} to the circle of {target.Id}", Locks: [Proposal.UnitLock(scout.Id)]);
    }
}

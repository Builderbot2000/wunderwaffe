using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Claims (WM-019): takes unclaimed buildings worth having, a fallen site's or an eliminated rival's, the two ways a
/// player does. With funds and reach on their tile it places the cheapest conveyor within
/// <see cref="GameQuery.ClaimRadius"/> of them; otherwise it flies an idle unit beside them and lands it. One target
/// at a time, the best by <see cref="Targets"/>. A capture-to-build faction (MP-010) ranks those holding a building it
/// can't build yet higher and always wants them.
/// </summary>
public sealed class ClaimModule : IAiModule
{
    public const int LandScore = 940, PlaceScore = 300, SendScore = 300;

    /// <summary>Rank in percent for a target holding a capture target (<see cref="AiContext.CaptureTargets"/>).</summary>
    public const int CapturePercent = 200;

    /// <summary>Cells added to the distance from home when ranking targets.</summary>
    public const int RankDistance = 64;

    /// <summary>Unclaimed buildings are worth a claim at this share of the unit design's cost, in percent (<see cref="Wanted"/>).</summary>
    public const int WorthPercent = 50;

    public string Name => "claim";

    /// <summary>
    /// A claim on <see cref="Tile"/> from local cell (<see cref="X"/>, <see cref="Y"/>): the unclaimed buildings it takes
    /// (<see cref="Ids"/>), what they are worth (<see cref="Worth(AiContext, BuildingView)"/>) and whether they hold a capture target.
    /// </summary>
    public sealed record Target(int Tile, int X, int Y, IReadOnlyList<int> Ids, long Worth, bool Capture, long Rank);

    /// <summary>A building's worth as a claim: its cost plus what it holds, in items.</summary>
    public static long Worth(AiContext c, BuildingView b) => c.Q.Content.Buildings[b.DefId].Cost.Values.Sum() + b.Holding.Values.Sum(n => (long)n);

    /// <summary>
    /// Worth having: a capture target, or worth at least <see cref="WorthPercent"/> of the unit design's cost (landing
    /// grounds a unit, which can lift off again; with no design, anything).
    /// </summary>
    public static bool Wanted(AiContext c, long worth, bool capture) =>
        capture || worth * 100 >= (c.Design?.Cost.Values.Sum() ?? 0) * (long)WorthPercent;

    /// <summary>
    /// Per tile with unclaimed buildings, the claim around its most valuable one (ties: lowest id), if wanted: from the
    /// free core-sized spot nearest the worth-weighted centre of the unclaimed buildings within two claim radii of it.
    /// Best first: worth (x <see cref="CapturePercent"/> with a capture target) over the distance from home plus
    /// <see cref="RankDistance"/>, then by tile id.
    /// </summary>
    public static IEnumerable<Target> Targets(AiContext c)
    {
        var home = c.Tiles[c.Home];
        var list = new List<Target>();
        foreach (var t in c.Tiles)
        {
            var unclaimed = c.Cached(t.Id, "unclaimed", () => c.Q.UnclaimedBuildings(t.Id));
            if (unclaimed.Count == 0)
                continue;
            var (x, y, ids) = c.Cached(t.Id, "claim", () => Point(c, t.Id, unclaimed));
            var taken = unclaimed.Where(b => ids.Contains(b.Id)).ToList();
            long worth = taken.Sum(b => Worth(c, b));
            bool capture = c.HoldsCaptureTarget(taken);
            if (taken.Count == 0 || !Wanted(c, worth, capture))
                continue;
            var (_, ox, oy) = c.Facts(t.Id);
            int d = AiContext.Distance(home.CenterX, home.CenterY, ox + x, oy + y);
            list.Add(new Target(t.Id, x, y, ids, worth, capture, worth * (capture ? CapturePercent : 100) / (d + RankDistance)));
        }
        return list.OrderByDescending(x => x.Rank).ThenBy(x => x.Tile);
    }

    static (int X, int Y, IReadOnlyList<int> Ids) Point(AiContext c, int tile, IReadOnlyList<BuildingView> unclaimed)
    {
        var anchor = unclaimed.OrderByDescending(b => Worth(c, b)).ThenBy(b => b.Id).First();
        long reach = 2L * c.Q.ClaimRadius();
        var group = unclaimed.Where(b => Sq(b.X - anchor.X) + Sq(b.Y - anchor.Y) <= reach * reach).ToList();
        long total = group.Sum(b => Worth(c, b) + 1);
        int ax = (int)(group.Sum(b => (Worth(c, b) + 1) * (2 * b.X + b.Width)) / (2 * total));
        int ay = (int)(group.Sum(b => (Worth(c, b) + 1) * (2 * b.Y + b.Height)) / (2 * total));
        var (lx, ly) = LandingSpot(c, tile, ax, ay);
        return (lx, ly, c.Q.ClaimableNear(tile, lx, ly));

        static long Sq(long v) => v * v;
    }

    /// <summary>
    /// The cell nearest (x, y), within <see cref="GameQuery.ClaimRadius"/>, centring a clear square a unit can land on
    /// whatever its heading: the design's longer side plus two cells of slack, every cell in the tile, buildable, unbuilt,
    /// unframed and free of wrecks. (x, y) itself if there is none.
    /// </summary>
    static (int X, int Y) LandingSpot(AiContext c, int tile, int x, int y)
    {
        var (w, h) = c.Design?.Blueprint.Size(c.Q.Content.Buildings) ?? (c.CoreDef.Width, c.CoreDef.Height);
        int side = Math.Max(w, h) + 2, half = side / 2, r = c.Q.ClaimRadius();
        var wrecks = c.Q.Wrecks(tile);
        foreach (var (dx, dy) in Enumerable.Range(-r, 2 * r + 1).SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => (dx, dy)))
                     .OrderBy(d => d.dx * d.dx + d.dy * d.dy).ThenBy(d => d.dy).ThenBy(d => d.dx))
        {
            int bx = x + dx - half, by = y + dy - half;
            if (bx < 0 || by < 0)
                continue;
            if (c.Q.Cells(tile, bx, by, side, side) is { Count: > 0 } cells && cells.Count == side * side
                && cells.All(v => v.InTile && v.Buildable && v.Building < 0 && !v.Frame)
                && !wrecks.Any(k => k.X < bx + side && bx < k.X + k.Width && k.Y < by + side && by < k.Y + k.Height))
                return (x + dx, y + dy);
        }
        return (x, y);
    }

    public IEnumerable<Proposal> Propose(AiContext c)
    {
        if (c.Home < 0)
            yield break;
        var units = c.Units.Where(u => u.MaxSpeed > 0).ToList();
        // A unit hovering beside unclaimed buildings worth having lands on them.
        foreach (var u in units.Where(u => !u.Moving))
        {
            var (_, ox, oy) = c.Facts(u.TileId);
            var ids = c.Q.ClaimableNear(u.TileId, u.CellX - ox, u.CellY - oy);
            if (ids.Count == 0)
                continue;
            var taken = c.Cached(u.TileId, "unclaimed", () => c.Q.UnclaimedBuildings(u.TileId)).Where(b => ids.Contains(b.Id)).ToList();
            if (Wanted(c, taken.Sum(b => Worth(c, b)), c.HoldsCaptureTarget(taken)) && c.Q.CheckDeploy(u.Id) == DeployResult.Ok)
                yield return new Proposal($"land-claim:{u.Id}", LandScore, [new DeployUnitCommand(c.Player, u.Id)],
                    $"unit {u.Id} lands on {ids.Count} unclaimed buildings on {u.TileId}", Locks: [Proposal.UnitLock(u.Id)]);
        }

        if (Targets(c).FirstOrDefault() is not { } target)
            yield break;
        var (tile, x, y) = (target.Tile, target.X, target.Y);
        // One claim at a time: while a unit is on its way there, wait.
        if (units.Any(u => u.Moving && u.DestTileId == tile))
            yield break;

        var conveyor = Roles.Cheapest(c.Q.Content, BuildingKind.Conveyor, c.Q.Can);
        if (conveyor != null && c.Q.CanAfford(tile, conveyor.Cost)
            && c.Q.FindSpot(tile, conveyor.Id, nearX: x, nearY: y) is { } spot
            && c.Q.ClaimableNear(tile, spot.X, spot.Y).Count > 0
            && c.Place("claim", PlaceScore, tile, conveyor.Id, spot) is { } place)
        {
            yield return place;
            yield break;
        }

        // Fly over the claim point (landing needs no reach).
        var (_, tx, ty) = c.Facts(tile);
        var unit = c.IdleUnits().OrderBy(u => AiContext.Distance(u.CellX, u.CellY, tx + x, ty + y)).ThenBy(u => u.Id)
            .FirstOrDefault(u => c.Q.Travel(u.Id, tile, x, y) != null);
        // One already there that can't land stays put rather than being sent again.
        if (unit != null && !(unit.TileId == tile && c.Q.ClaimableNear(tile, unit.CellX - tx, unit.CellY - ty).Count > 0))
            yield return new Proposal($"claim-unclaimed:{tile}", SendScore, [new MoveUnitsCommand(c.Player, [unit.Id], tile, x, y)],
                $"unit {unit.Id} to claim {target.Ids.Count} unclaimed buildings on {tile}", Locks: [Proposal.UnitLock(unit.Id)]);
    }
}

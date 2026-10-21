using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Attack (MP-006): gathers idle units into squads of <see cref="Group"/>, sets their stance, and sends each idle
/// squad with an attack-move (CB-001) at the best neutral site it clearly outmatches (WM-010, WM-018:
/// <see cref="SiteTargets"/>), else at the core on the nearest tile the strategic layer marks Attack, leaving the
/// fighting on the way to the squad's stance. It commands squads, never single units.
/// </summary>
public sealed class AttackModule : IAiModule
{
    public const int SquadScore = 210, StanceScore = 205, AttackScore = 200;

    /// <summary>Idle units held back until there are this many to form a squad.</summary>
    public const int Group = 3;

    /// <summary>The stance attack squads fight in (UC-004).</summary>
    public static Stance StanceFor(TileGoal goal) => Stance.Aggressive;

    /// <summary>Which enemy attack squads shoot first (CB-007): whatever shoots back hardest.</summary>
    public static TargetMode TargetingFor(TileGoal goal) => TargetMode.MostWeapons;

    /// <summary>
    /// A squad takes on a site only if its fighting strength (DPS x HP, Lanchester's square law) is at least this many
    /// times the site's live turrets and guards: a Citadel early on never qualifies.
    /// </summary>
    public const int OutmatchFactor = 3;

    /// <summary>Site rank in percent for a site holding a capture target (MP-010, <see cref="AiContext.CaptureTargets"/>).</summary>
    public const int CapturePercent = 200;

    /// <summary>Cells added to the distance when ranking sites, so a near one doesn't win on nearness alone.</summary>
    public const int RankDistance = 64;

    public string Name => "attack";

    /// <summary>True if a force of <paramref name="dps"/> and <paramref name="hp"/> clearly outmatches the site (<see cref="OutmatchFactor"/>).</summary>
    public static bool Outmatches(long dps, long hp, SiteView site) => dps > 0 && dps * hp >= OutmatchFactor * (long)site.Dps * site.Hp;

    /// <summary>
    /// Live sites a force of <paramref name="dps"/> and <paramref name="hp"/> at world cell (x, y) clearly outmatches,
    /// best first: template value (x <see cref="CapturePercent"/> with a capture target) over distance plus
    /// <see cref="RankDistance"/>, then by id.
    /// </summary>
    public static IEnumerable<SiteView> SiteTargets(AiContext c, long dps, long hp, int x, int y) =>
        c.Q.Sites().Where(s => !s.Fallen && Outmatches(dps, hp, s))
            .Select(s => (Site: s, Rank: Rank(c, s, x, y)))
            .OrderByDescending(r => r.Rank).ThenBy(r => r.Site.Id).Select(r => r.Site);

    static long Rank(AiContext c, SiteView s, int x, int y)
    {
        var (_, ox, oy) = c.Facts(s.TileId);
        int d = AiContext.Distance(x, y, ox + s.X + s.Width / 2, oy + s.Y + s.Height / 2);
        long value = Math.Max(1, s.Value) * 1024;
        if (c.HoldsCaptureTarget(c.Q.Buildings(s.TileId).Where(b => b.Owner == s.Player)))
            value = value * CapturePercent / 100;
        return value / (d + RankDistance);
    }

    public IEnumerable<Proposal> Propose(AiContext c)
    {
        var idle = c.IdleUnits().ToList();
        var squads = c.Q.Squads(c.Player);
        var squadded = squads.SelectMany(s => s.Members).ToHashSet();

        var loose = idle.Where(u => !squadded.Contains(u.Id)).ToList();
        if (loose.Count >= Group)
        {
            int[] ids = [.. loose.Select(u => u.Id)];
            string name = $"Strike {ids[0]}";
            if (c.Q.CheckCreateSquad(name, ids) == SquadResult.Ok)
                yield return new Proposal($"squad:{ids[0]}", SquadScore, [new CreateSquadCommand(c.Player, name, ids)],
                    $"{ids.Length} idle units", Locks: [.. ids.Select(Proposal.UnitLock)]);
        }

        var idleIds = idle.Select(u => u.Id).ToHashSet();
        var targets = c.WithGoal(TileGoal.Attack).ToList();
        foreach (var squad in squads.OrderBy(s => s.Id))
        {
            if (squad.Members.Count == 0 || !squad.Members.All(idleIds.Contains))
                continue;
            if (squad.Stance != StanceFor(TileGoal.Attack))
            {
                yield return new Proposal($"stance:{squad.Id}", StanceScore, [new SetSquadStanceCommand(c.Player, squad.Id, StanceFor(TileGoal.Attack))],
                    $"squad {squad.Id} to {StanceFor(TileGoal.Attack)}", Locks: [.. squad.Members.Select(Proposal.UnitLock)]);
                continue;
            }
            if (squad.Targeting != TargetingFor(TileGoal.Attack))
            {
                yield return new Proposal($"targeting:{squad.Id}", StanceScore, [new SetSquadTargetingCommand(c.Player, squad.Id, TargetingFor(TileGoal.Attack))],
                    $"squad {squad.Id} targets {TargetingFor(TileGoal.Attack)}", Locks: [.. squad.Members.Select(Proposal.UnitLock)]);
                continue;
            }
            var lead = c.Units.First(u => u.Id == squad.Members[0]);
            var members = squad.Members.Select(id => c.Units.First(u => u.Id == id)).ToList();
            long dps = members.Sum(u => (long)(c.Q.UnitStatsOf(u.Id)?.Dps ?? 0)), hp = members.Sum(u => (long)u.Hp);
            var site = SiteTargets(c, dps, hp, lead.CellX, lead.CellY)
                .Select(s => (s, Core: c.Q.Buildings(s.TileId).FirstOrDefault(b => b.Owner == s.Player && c.Kind(b) == Content.BuildingKind.Core)))
                .FirstOrDefault(t => t.Core != null && c.Q.Travel(lead.Id, t.s.TileId, t.Core.X, t.Core.Y) != null);
            if (site.Core is { } keep)
            {
                int[] sent = [.. members.Where(u => u.TileId != site.s.TileId).Select(u => u.Id)];
                if (sent.Length > 0)
                    yield return new Proposal($"site:{squad.Id}", AttackScore,
                        [new AttackMoveCommand(c.Player, sent, site.s.TileId, keep.X + keep.Width / 2, keep.Y + keep.Height / 2)],
                        $"squad {squad.Id} at {site.s.Name}", Locks: [.. sent.Select(Proposal.UnitLock)]);
                continue;
            }
            var target = targets
                .Select(g => c.Tiles[g.Tile])
                .OrderBy(t => (long)(t.CenterX - lead.CellX) * (t.CenterX - lead.CellX) + (long)(t.CenterY - lead.CellY) * (t.CenterY - lead.CellY))
                .ThenBy(t => t.Id)
                .Select(t => (t.Id, Core: c.Q.Buildings(t.Id).FirstOrDefault(b => c.Q.Content.Buildings[b.DefId].Kind == Content.BuildingKind.Core)))
                .FirstOrDefault(t => t.Core != null);
            if (target.Core is not { } core)
                continue;
            // Members already on the target tile are there: sending them again would only spam the log.
            int[] go = [.. squad.Members.Where(id => c.Units.First(u => u.Id == id).TileId != target.Id)];
            if (go.Length == 0)
                continue;
            yield return new Proposal($"attack:{squad.Id}", AttackScore,
                [new AttackMoveCommand(c.Player, go, target.Id, core.X + core.Width / 2, core.Y + core.Height / 2)],
                $"squad {squad.Id} at the core on {target.Id}", Locks: [.. go.Select(Proposal.UnitLock)]);
        }
    }
}

using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// Attack (MP-006): gathers idle units into squads of <see cref="Group"/>, sets their stance, and sends each idle
/// squad at the core on the nearest tile the strategic layer marks Attack with an attack-move (CB-001), leaving the
/// fighting on the way to the squad's stance. It commands squads, never single units.
/// </summary>
public sealed class AttackModule : IAiModule
{
    public const int SquadScore = 210, StanceScore = 205, AttackScore = 200;

    /// <summary>Idle units held back until there are this many to form a squad.</summary>
    public const int Group = 3;

    /// <summary>The stance attack squads fight in. Seam for silo E's stances and targeting modes (UC-004, CB-007).</summary>
    public static Stance StanceFor(TileGoal goal) => Stance.Aggressive;

    public string Name => "attack";

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
            var lead = c.Units.First(u => u.Id == squad.Members[0]);
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

using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Movement and command queries (silo E): routes and travel times, squads, rally points.</summary>
public sealed partial class GameQuery
{
    /// <summary>
    /// The route and travel time of one of <see cref="Player"/>'s units to cell (x, y) of a tile (UC-009), or null if
    /// it can't get there over terrain its locomotion crosses, or isn't theirs.
    /// </summary>
    public TravelView? Travel(int unitId, int tileId, int x, int y) =>
        Sim.UnitById(unitId) is { } u && u.Owner == Player && UnitNav.Plan(Sim, u, tileId, x, y) is { } p
            ? new TravelView(p.Tiles, p.Length, p.Ticks)
            : null;

    /// <summary>Squads of a player (default <see cref="Player"/>), in id order.</summary>
    public IReadOnlyList<SquadView> Squads(int? owner = null) =>
        Sim.Squads.OwnedBy(owner ?? Player).OrderBy(s => s.Id)
            .Select(s => new SquadView(s.Id, s.Owner, s.Name, [.. s.Members], s.Formation, s.Stance, s.Order?.Kind, s.TargetId)).ToList();

    public SquadResult CheckCreateSquad(string? name, int[] unitIds) => UnitSim.CheckCreateSquad(Sim, Player, name, unitIds);

    public SquadResult CheckFormation(int squadId, Formation formation, int[]? custom = null) =>
        UnitSim.CheckFormation(Sim, Player, squadId, formation, custom);

    public RallyResult CheckRally(int tileId, int x, int y, int rallyTileId, int rallyX, int rallyY, int squadId = 0) =>
        UnitSim.CheckRally(Sim, Player, tileId, x, y, rallyTileId, rallyX, rallyY, squadId);
}

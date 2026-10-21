using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Territory queries (silo A): presence, victory circles, claims, Contested tiles and who pays.</summary>
public sealed partial class GameQuery
{
    /// <summary>Whether <see cref="Player"/> has a building or unit on the tile, or owns it: enough to enter it and land (WM-015).</summary>
    public bool HasPresence(int tileId) => Sim.TryGetTile(tileId) is { } t && Territory.HasPresence(Sim, Player, t);

    /// <summary>
    /// <see cref="Player"/>'s build radii on the tile (WM-015): major buildings' footprints and flying units' centre cells
    /// with their radius in cells, tile-local. Building operations need every cell inside one.
    /// </summary>
    public IReadOnlyList<BuildRadius> BuildRadii(int tileId) => Sim.TryGetTile(tileId) is { } t ? Territory.BuildRadii(Sim, Player, t) : [];

    /// <summary>Whether every cell of the area is inside one of <see cref="Player"/>'s build radii on the tile (WM-015).</summary>
    public bool InBuildRadius(int tileId, int x, int y, int width = 1, int height = 1) =>
        Sim.TryGetTile(tileId) is { } t && Territory.InBuildRadius(Sim, Player, t, x, y, width, height);

    /// <summary>Whether local cell (x, y) of the tile is in its victory circle (WM-014).</summary>
    public bool InCircle(int tileId, int x, int y) => Sim.TryGetTile(tileId) is { } t && t.InCircle(x, y);

    /// <summary>Players with a building in the tile's victory circle, ascending.</summary>
    public IReadOnlyList<int> CircleOccupants(int tileId) => Sim.TryGetTile(tileId) is { } t ? Expansion.CircleOccupants(t) : [];

    /// <summary>A player's (default <see cref="Player"/>'s) claim progress on the tile, 0 to <see cref="CaptureTicks"/> (WM-006).</summary>
    public int ClaimProgress(int tileId, int? player = null) => Sim.TryGetTile(tileId) is { } t ? Expansion.Progress(t, player ?? Player) : 0;

    /// <summary>Ticks a building must hold a circle to claim the tile, from the match settings.</summary>
    public int CaptureTicks() => Expansion.CaptureTicks(Sim);

    /// <summary>Ids of the tiles where more than one player has buildings or units (WM-004), ascending.</summary>
    public IReadOnlyList<int> ContestedTiles()
    {
        var contested = Territory.ContestedTiles(Sim);
        return Enumerable.Range(0, contested.Length).Where(i => contested[i]).ToList();
    }

    /// <summary>The tile's unclaimed buildings (WM-019; owner -1), in id order: anyone may claim them by building or landing near.</summary>
    public IReadOnlyList<BuildingView> UnclaimedBuildings(int tileId) =>
        Sim.TryGetTile(tileId) is { } t ? Territory.UnclaimedOn(t).Select(b => View(t, b)).ToList() : [];

    /// <summary>
    /// Ids of the unclaimed buildings that placing a building or landing over the area would claim (WM-019, within
    /// <see cref="ClaimRadius"/>), in id order. Any player may claim; it is <see cref="Player"/>'s build radius that limits where.
    /// </summary>
    public IReadOnlyList<int> ClaimableNear(int tileId, int x, int y, int width = 1, int height = 1) =>
        Sim.TryGetTile(tileId) is { } t ? Territory.ClaimableNear(t, x, y, width, height).Select(b => b.Id).ToList() : [];

    /// <summary>Cells from a placed or landed area within which unclaimed buildings are claimed (WM-019).</summary>
    public int ClaimRadius() => Balance.ClaimRadius;



}

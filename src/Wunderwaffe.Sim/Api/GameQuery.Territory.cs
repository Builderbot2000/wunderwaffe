using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Territory queries (silo A): presence, victory circles, claims, Contested tiles and who pays.</summary>
public sealed partial class GameQuery
{
    /// <summary>Whether <see cref="Player"/> may build or lay frame on the tile (WM-015).</summary>
    public bool HasPresence(int tileId) => Sim.TryGetTile(tileId) is { } t && Territory.HasPresence(Sim, Player, t);

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

    /// <summary>
    /// The tile that would pay <paramref name="cost"/> for something built on the tile: the tile itself or an owned
    /// neighbour (see <see cref="Territory.Payer"/>), or -1 if none can.
    /// </summary>
    public int Payer(int tileId, IReadOnlyDictionary<string, int> cost) =>
        Sim.TryGetTile(tileId) is { } t ? Territory.Payer(Sim, t, Player, cost)?.Id ?? -1 : -1;
}

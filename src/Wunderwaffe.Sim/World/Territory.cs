using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Who may act where (WM-015) and when a tile is Contested (WM-004); who pays is <see cref="Funds"/>. Read-only queries shared by placement,
/// landing, the UI and the other silos; <see cref="DestroyBuilding"/> is the one write.
/// </summary>
public static class Territory
{
    /// <summary>
    /// A player has presence on a tile they own or where they have a building or a unit (WM-015). Building and landing
    /// need it, so a player's first foothold on a new tile is a unit flying in.
    /// </summary>
    public static bool HasPresence(Simulation sim, int player, TileState tile)
    {
        if (tile.Owner == player)
            return true;
        foreach (var u in sim.Units)
        {
            if (u.Owner == player && u.TileId == tile.Id)
                return true;
        }
        foreach (var b in tile.Grid.Buildings)
        {
            if (b.Owner == player)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True if the footprint touches a circle owned by someone else while a building not of <paramref name="player"/>'s
    /// stands in it: nobody builds or lands there until those buildings are destroyed (WM-015, UD-011, CB-003).
    /// </summary>
    public static bool EnemyCircleBlocks(TileState tile, int player, int x, int y, int w, int h)
    {
        if (tile.Owner == TileState.Unowned || tile.Owner == player || !tile.InCircle(x, y, w, h))
            return false;
        return Expansion.CircleOccupants(tile).Any(p => p != player);
    }



    /// <summary>
    /// A tile is Contested while a player other than its owner has a unit on it (WM-004, OQ-20); an unowned tile is
    /// Contested while units of two or more players are on it. Exact at any moment: it reads unit positions.
    /// </summary>
    public static bool IsContested(Simulation sim, TileState tile)
    {
        int seen = TileState.Unowned;
        foreach (var u in sim.Units)
        {
            if (u.TileId != tile.Id || u.Owner == tile.Owner)
                continue;
            if (tile.Owner != TileState.Unowned || (seen != TileState.Unowned && seen != u.Owner))
                return true;
            seen = u.Owner;
        }
        return false;
    }

    /// <summary><see cref="IsContested"/> for every tile at once, indexed by tile id, in one pass over the units.</summary>
    public static bool[] ContestedTiles(Simulation sim)
    {
        var contested = new bool[sim.Tiles.Count];
        var seen = new int[sim.Tiles.Count];
        Array.Fill(seen, TileState.Unowned);
        foreach (var u in sim.Units)
        {
            var tile = sim.Tiles[u.TileId];
            if (u.Owner == tile.Owner)
                continue;
            if (tile.Owner != TileState.Unowned || (seen[tile.Id] != TileState.Unowned && seen[tile.Id] != u.Owner))
                contested[tile.Id] = true;
            seen[tile.Id] = u.Owner;
        }
        return contested;
    }

    /// <summary>
    /// Removes a building as destroyed: no refund, whatever it held is lost, and a wreck stays (CB-006, through
    /// <see cref="Combat.DestroyBuilding"/>). Used when a claim wipes a circle (WM-006) and by
    /// <see cref="DebugDestroyCommand"/>. Losing a last core eliminates the player (WM-016); the victory check sees
    /// that from the buildings left.
    /// </summary>
    internal static void DestroyBuilding(Simulation sim, TileState tile, PlacedBuilding b) =>
        Combat.DestroyBuilding(sim, tile, b);
}

using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Who may act where (WM-015), who pays, and when a tile is Contested (WM-004). Read-only queries shared by placement,
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
    /// The tile whose stock pays for <paramref name="player"/> building on <paramref name="tile"/>: the tile itself if
    /// they own it and it can afford the cost, otherwise the first neighbouring tile they own (in id order) that can.
    /// Null if none can. Nobody spends a stock that isn't theirs, so building on a tile you don't own is supplied from
    /// next door.
    /// </summary>
    public static TileState? Payer(Simulation sim, TileState tile, int player, IReadOnlyDictionary<string, int> cost)
    {
        if (tile.Owner == player && tile.Inventory.Has(cost))
            return tile;
        foreach (var n in sim.Neighbors(tile))
        {
            if (n.Owner == player && n.Inventory.Has(cost))
                return n;
        }
        return null;
    }

    /// <summary>Where a refund goes: the tile's stock if the player owns it, else their first neighbouring tile; null if neither.</summary>
    internal static TileState? RefundTo(Simulation sim, TileState tile, int player) =>
        tile.Owner == player ? tile : sim.Neighbors(tile).FirstOrDefault(n => n.Owner == player);

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
    /// Removes a building as destroyed: no refund, and whatever it held is lost. Used when a claim wipes a circle
    /// (WM-006) and by <see cref="DebugDestroyCommand"/>. Stand-in for combat's <c>Combat.DestroyBuilding</c> (wrecks,
    /// CB-001), which this should forward to once it exists. Losing a last core eliminates the player (WM-016); the
    /// victory check sees that from the buildings left.
    /// </summary>
    internal static void DestroyBuilding(Simulation sim, TileState tile, PlacedBuilding b)
    {
        tile.Grid.Remove(b);
    }
}

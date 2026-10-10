namespace Wunderwaffe.Sim.World;

public enum ClaimResult
{
    Ok,
    NoSuchTile,
    AlreadyOwned,
    NotAdjacent,
    CannotAfford,
}

/// <summary>
/// Tile claiming rules (WM-006). Read-only, so the world map can preview a claim with the same check
/// the command applies.
/// </summary>
public static class Expansion
{
    /// <summary>
    /// On success, <paramref name="source"/> is the tile that pays: the first neighbour, in
    /// <see cref="Simulation.Neighbors"/> order, that the player owns and that can afford the cost.
    /// </summary>
    public static ClaimResult Check(Simulation sim, int player, int tileId, out TileState? source)
    {
        source = null;
        if (sim.TryGetTile(tileId) is not { } tile)
            return ClaimResult.NoSuchTile;
        if (tile.Owner != TileState.Unowned)
            return ClaimResult.AlreadyOwned;

        bool adjacent = false;
        foreach (var n in sim.Neighbors(tile))
        {
            if (n.Owner != player)
                continue;
            adjacent = true;
            if (n.Inventory.Has(Balance.ClaimCost))
            {
                source = n;
                return ClaimResult.Ok;
            }
        }
        return adjacent ? ClaimResult.CannotAfford : ClaimResult.NotAdjacent;
    }
}

using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Who pays and where refunds go (FB-019, OQ-38, OQ-39). What a player can spend on a tile is the stock of their cores
/// standing on it; cores on units and other tiles' cores don't count. Every cost on a tile (buildings, frames,
/// blueprints, repairs) goes through <see cref="CanPay"/> and <see cref="TryPay"/>, every refund through
/// <see cref="Refund"/>.
/// </summary>
public static class Funds
{
    /// <summary>The player's cores standing on the tile, in id order: the order they pay in.</summary>
    public static IEnumerable<PlacedBuilding> Cores(TileState tile, int player) =>
        tile.Grid.Cores.Where(c => c.Owner == player);

    /// <summary>How much of an item the player's cores on the tile hold together.</summary>
    public static long Available(TileState tile, int player, string item)
    {
        long n = 0;
        foreach (var c in tile.Grid.Cores)
        {
            if (c.Owner == player)
                n += c.Stock!.Get(item);
        }
        return n;
    }

    /// <summary>Everything the player's cores on the tile hold, summed: their build funds there (the HUD stock).</summary>
    public static SortedDictionary<string, long> Total(TileState tile, int player)
    {
        var total = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var c in Cores(tile, player))
        {
            foreach (var (id, n) in c.Stock!.Entries)
                total[id] = total.GetValueOrDefault(id) + n;
        }
        return total;
    }

    /// <summary>True if the player's cores on the tile together hold the whole cost. A cost of nothing is always payable.</summary>
    public static bool CanPay(TileState tile, int player, IReadOnlyDictionary<string, int> cost)
    {
        foreach (var (id, n) in cost)
        {
            if (Available(tile, player, id) < n)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Pays the cost from the player's cores on the tile, all or nothing: each item is taken from the lowest-id core
    /// that has any, then the next (OQ-38). False, with nothing taken, if they can't cover it.
    /// </summary>
    internal static bool TryPay(TileState tile, int player, IReadOnlyDictionary<string, int> cost)
    {
        if (!CanPay(tile, player, cost))
            return false;
        foreach (var (id, n) in cost)
        {
            long left = n;
            foreach (var c in Cores(tile, player))
            {
                long take = Math.Min(left, c.Stock!.Get(id));
                c.Stock.Add(id, -take);
                left -= take;
                if (left == 0)
                    break;
            }
        }
        return true;
    }

    /// <summary>
    /// The player's core on the tile nearest the w x h footprint at (x, y), centre to centre, lower id on a tie; null
    /// if they have none there.
    /// </summary>
    public static PlacedBuilding? NearestCore(TileState tile, int player, int x, int y, int w = 1, int h = 1)
    {
        PlacedBuilding? best = null;
        long bestD = long.MaxValue;
        foreach (var c in Cores(tile, player))
        {
            // Doubled coordinates keep the centres whole.
            long dx = 2L * c.X + c.Width - (2L * x + w), dy = 2L * c.Y + c.Height - (2L * y + h);
            long d = dx * dx + dy * dy;
            if (d < bestD)
                (best, bestD) = (c, d);
        }
        return best;
    }

    /// <summary>
    /// Gives items back to the player's core on the tile nearest the footprint (OQ-39); with no core there they are lost.
    /// Refunds aren't held to <see cref="Content.BuildingDef.Capacity"/>, which only stops deliveries.
    /// </summary>
    internal static void Refund(TileState tile, int player, int x, int y, int w, int h, IEnumerable<KeyValuePair<string, long>> items)
    {
        if (NearestCore(tile, player, x, y, w, h) is not { } core)
            return;
        foreach (var (id, n) in items)
            core.Stock!.Add(id, n);
    }

    /// <inheritdoc cref="Refund(TileState, int, int, int, int, int, IEnumerable{KeyValuePair{string, long}})"/>
    internal static void Refund(TileState tile, int player, int x, int y, int w, int h, IReadOnlyDictionary<string, int> items, int percent = 100) =>
        Refund(tile, player, x, y, w, h, items.Select(kv => new KeyValuePair<string, long>(kv.Key, (long)kv.Value * percent / 100)));

    /// <summary>Whether a core takes one more of an item from a belt or a machine: a full core refuses, so belts back up.</summary>
    internal static bool Accepts(PlacedBuilding core, string item) => core.Stock!.Get(item) < core.Def.Capacity;
}

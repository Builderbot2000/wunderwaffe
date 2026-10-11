namespace Wunderwaffe.Sim.World;

public enum FilterResult
{
    Ok,
    NotOwner,

    /// <summary>No sorter on that cell.</summary>
    NoSorter,
    NoSuchItem,
}

/// <summary>Settings on logistics buildings (FB-004). The movement itself is in <see cref="TileEntities"/>.</summary>
public static class Logistics
{
    /// <summary>Whether <paramref name="player"/> may set the sorter at (x, y) to let <paramref name="itemId"/> through.</summary>
    public static FilterResult CheckFilter(Simulation sim, TileState tile, int player, int x, int y, string? itemId)
    {
        if (tile.Owner != player)
            return FilterResult.NotOwner;
        if (tile.Grid.BuildingAt(x, y)?.Sorter == null)
            return FilterResult.NoSorter;
        if (itemId != null && !sim.Content.Items.ContainsKey(itemId))
            return FilterResult.NoSuchItem;
        return FilterResult.Ok;
    }

    internal static FilterResult SetFilter(Simulation sim, TileState tile, int player, int x, int y, string? itemId)
    {
        var result = CheckFilter(sim, tile, player, x, y, itemId);
        if (result == FilterResult.Ok)
            tile.Grid.BuildingAt(x, y)!.Sorter!.Filter = itemId;
        return result;
    }
}

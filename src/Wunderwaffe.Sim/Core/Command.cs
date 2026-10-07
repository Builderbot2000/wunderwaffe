using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Core;

/// <summary>
/// Player input. The simulation only changes through commands, which is what lockstep multiplayer and
/// replays rely on (NF-004).
/// </summary>
public abstract record Command(int Player)
{
    internal abstract void Apply(Simulation sim);
}

/// <summary>
/// Places a building, paying its cost from the tile inventory (FB-002). Extractors pick their recipe
/// from the deposit underneath; crafters with several recipes need <paramref name="RecipeId"/>.
/// </summary>
public sealed record PlaceBuildingCommand(
    int Player, int TileId, string BuildingId, int X, int Y, int Rotation = 0, string? RecipeId = null)
    : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            sim.TryPlace(tile, Player, BuildingId, X, Y, Rotation, RecipeId);
    }
}

/// <summary>Removes the building covering cell (X, Y) and refunds its cost (FB-002).</summary>
public sealed record RemoveBuildingCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            sim.TryRemove(tile, Player, X, Y);
    }
}

/// <summary>Debug only: adds items to a tile. Must be disabled in multiplayer.</summary>
public sealed record DebugGrantCommand(int Player, int TileId, string ItemId, int Amount) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile && sim.Content.Items.ContainsKey(ItemId) && Amount > 0)
            tile.Inventory.Add(ItemId, Amount);
    }
}

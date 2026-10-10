using System.Text.Json.Serialization;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Core;

/// <summary>
/// Player input. The simulation only changes through commands, which is what lockstep multiplayer and
/// replays rely on (NF-004). Each kind has a stable JSON tag so saved command logs keep loading (NF-002).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PlaceBuildingCommand), "place")]
[JsonDerivedType(typeof(RemoveBuildingCommand), "remove")]
[JsonDerivedType(typeof(ClaimTileCommand), "claim")]
[JsonDerivedType(typeof(PlaceFrameCommand), "place_frame")]
[JsonDerivedType(typeof(RemoveFrameCommand), "remove_frame")]
[JsonDerivedType(typeof(ActivateUnitCommand), "activate_unit")]
[JsonDerivedType(typeof(DeployUnitCommand), "deploy_unit")]
[JsonDerivedType(typeof(MoveUnitsCommand), "move_units")]
[JsonDerivedType(typeof(DebugGrantCommand), "debug_grant")]
[JsonDerivedType(typeof(PlaceBlueprintCommand), "place_blueprint")]
[JsonDerivedType(typeof(SetFactoryBlueprintCommand), "set_factory_blueprint")]
public abstract record Command(int Player)
{
    internal abstract void Apply(Simulation sim);
}

/// <summary>A command as it was applied: at the start of <see cref="Tick"/>.</summary>
public readonly record struct LoggedCommand(long Tick, Command Command);

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

/// <summary>Claims an unclaimed tile next to one the player owns (WM-006).</summary>
public sealed record ClaimTileCommand(int Player, int TileId) : Command(Player)
{
    internal override void Apply(Simulation sim) => sim.TryClaim(Player, TileId);
}

/// <summary>Lays a unit frame on a cell, paying <see cref="Balance.FrameCost"/>. Frames go under buildings too.</summary>
public sealed record PlaceFrameCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitAssembly.PlaceFrame(tile, Player, X, Y);
    }
}

/// <summary>Takes a frame off a cell and refunds it.</summary>
public sealed record RemoveFrameCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitAssembly.RemoveFrame(tile, Player, X, Y);
    }
}

/// <summary>
/// Triggers the unit core at (X, Y): the buildings on the frame joined to it lift off the tile as one unit.
/// </summary>
public sealed record ActivateUnitCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitAssembly.Activate(sim, tile, Player, X, Y);
    }
}

/// <summary>Lands a unit: its blocks and frame go back on the tile, turned to the nearest quarter turn.</summary>
public sealed record DeployUnitCommand(int Player, int UnitId) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitAssembly.Deploy(sim, Player, UnitId);
}

/// <summary>
/// Sends units to a cell, possibly on another tile. The target cell is in <paramref name="TileId"/>'s own
/// coordinates; use <see cref="Simulation.ResolveCell"/> to turn a cell past a tile's edge into one.
/// </summary>
public sealed record MoveUnitsCommand(int Player, int[] UnitIds, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.Order(sim, Player, UnitIds, TileId, X, Y);
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

/// <summary>
/// Places a whole blueprint in one action with its top-left at (X, Y), mirrored if <paramref name="Flip"/> and then
/// turned <paramref name="Rotation"/> quarter turns, paying the total cost (FB-009). Nothing is placed unless every
/// block fits and the total is affordable; see <see cref="Blueprints.Check"/>.
/// </summary>
public sealed record PlaceBlueprintCommand(int Player, int TileId, Blueprint Blueprint, int X, int Y, int Rotation = 0, bool Flip = false)
    : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            Blueprints.Place(sim, tile, Player, Blueprint, X, Y, Rotation, Flip);
    }
}

/// <summary>
/// Projects a blueprint from a unit factory (UD-006): the factory covering cell (X, Y) builds it with its top-left at
/// (OX, OY), turned and flipped as for <see cref="PlaceBlueprintCommand"/>, over and over. A null blueprint stops it.
/// See <see cref="UnitFactory.CheckProject"/>.
/// </summary>
public sealed record SetFactoryBlueprintCommand(
    int Player, int TileId, int X, int Y, Blueprint? Blueprint, int OX = 0, int OY = 0, int Rotation = 0, bool Flip = false)
    : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitFactory.Project(sim, tile, Player, X, Y, Blueprint, OX, OY, Rotation, Flip);
    }
}

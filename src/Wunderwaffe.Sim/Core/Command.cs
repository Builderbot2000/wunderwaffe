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
[JsonDerivedType(typeof(AttackCommand), "attack")]
[JsonDerivedType(typeof(AttackBuildingCommand), "attack_building")]
[JsonDerivedType(typeof(AttackMoveCommand), "attack_move")]
[JsonDerivedType(typeof(DebugDamageCommand), "debug_damage")]
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
    internal override void Apply(Simulation sim)
    {
        foreach (int id in UnitIds)
        {
            if (sim.UnitById(id) is { } u && u.Owner == Player)
                CombatOrders.Clear(u);
        }
        UnitSim.Order(sim, Player, UnitIds, TileId, X, Y);
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

/// <summary>Sends units after an enemy unit until it is destroyed (CB-001). They close to weapon range and fire.</summary>
public sealed record AttackCommand(int Player, int[] UnitIds, int TargetUnitId) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.UnitById(TargetUnitId) is not { } target || target.Owner == Player)
            return;
        var (gx, gy) = ((int)(target.X / Unit.CellScale), (int)(target.Y / Unit.CellScale));
        var tile = sim.Tiles[target.TileId];
        CombatOrders.Issue(sim, Player, UnitIds, CombatOrderKind.Attack, tile.Id, gx - tile.OriginX, gy - tile.OriginY, TargetUnitId);
    }
}

/// <summary>Sends units to destroy the enemy building covering cell (X, Y) of a tile (CB-001, IO-006).</summary>
public sealed record AttackBuildingCommand(int Player, int[] UnitIds, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile && tile.Grid.BuildingAt(X, Y) is { } b && b.Owner != Player)
            CombatOrders.Issue(sim, Player, UnitIds, CombatOrderKind.Attack, TileId, X, Y);
    }
}

/// <summary>Moves units to a cell, stopping to fight whatever comes into weapon range on the way (CB-001).</summary>
public sealed record AttackMoveCommand(int Player, int[] UnitIds, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim) =>
        CombatOrders.Issue(sim, Player, UnitIds, CombatOrderKind.AttackMove, TileId, X, Y);
}

/// <summary>Debug only: damages the building on a cell, as an untyped hit (it may be destroyed). Must be disabled in multiplayer.</summary>
public sealed record DebugDamageCommand(int Player, int TileId, int X, int Y, int Amount) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile && tile.Grid.BuildingAt(X, Y) is { } b && Amount > 0)
            Combat.DamageBuilding(sim, tile, b, Amount, "debug");
    }
}

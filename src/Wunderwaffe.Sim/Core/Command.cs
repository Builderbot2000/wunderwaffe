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
[JsonDerivedType(typeof(DebugDestroyCommand), "debug_destroy")]
[JsonDerivedType(typeof(PlaceBlueprintCommand), "place_blueprint")]
[JsonDerivedType(typeof(SetFactoryBlueprintCommand), "set_factory_blueprint")]
[JsonDerivedType(typeof(HoldUnitsCommand), "hold_units")]
[JsonDerivedType(typeof(PatrolUnitsCommand), "patrol_units")]
[JsonDerivedType(typeof(RetreatUnitsCommand), "retreat_units")]
[JsonDerivedType(typeof(SetStanceCommand), "set_stance")]
[JsonDerivedType(typeof(CreateSquadCommand), "create_squad")]
[JsonDerivedType(typeof(DisbandSquadCommand), "disband_squad")]
[JsonDerivedType(typeof(AssignSquadCommand), "assign_squad")]
[JsonDerivedType(typeof(RenameSquadCommand), "rename_squad")]
[JsonDerivedType(typeof(SetFormationCommand), "set_formation")]
[JsonDerivedType(typeof(SetSquadStanceCommand), "set_squad_stance")]
[JsonDerivedType(typeof(SetRallyCommand), "set_rally")]
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

/// <summary>Lays a unit frame on a cell, paying <see cref="Balance.FrameCost"/> via <see cref="Territory.Payer"/>. Frames go under buildings too.</summary>
public sealed record PlaceFrameCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitAssembly.PlaceFrame(sim, tile, Player, X, Y);
    }
}

/// <summary>Takes a frame off a cell and refunds it.</summary>
public sealed record RemoveFrameCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitAssembly.RemoveFrame(sim, tile, Player, X, Y);
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
/// <summary>
/// Debug only: destroys the building covering cell (X, Y), whoever owns it, as combat will (WM-016: losing a last core
/// eliminates its owner). Must be disabled in multiplayer.
/// </summary>
public sealed record DebugDestroyCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile && tile.Grid.BuildingAt(X, Y) is { } b)
            Territory.DestroyBuilding(sim, tile, b);
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

/// <summary>Units stop where they are and stay (UC-001). They still fire, but don't chase.</summary>
public sealed record HoldUnitsCommand(int Player, int[] UnitIds) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.Issue(sim, Player, UnitIds, OrderKind.Hold, -1, 0, 0);
}

/// <summary>Units go back and forth between where they are and a cell (UC-001), in formation.</summary>
public sealed record PatrolUnitsCommand(int Player, int[] UnitIds, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.Issue(sim, Player, UnitIds, OrderKind.Patrol, TileId, X, Y);
}

/// <summary>Units fall back to the nearest tile their owner holds, without fighting (UC-001).</summary>
public sealed record RetreatUnitsCommand(int Player, int[] UnitIds) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.Issue(sim, Player, UnitIds, OrderKind.Retreat, -1, 0, 0);
}

/// <summary>Sets units' stance (UC-004); an escort follows unit <paramref name="EscortId"/>.</summary>
public sealed record SetStanceCommand(int Player, int[] UnitIds, Stance Stance, int EscortId = -1) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetStance(sim, Player, UnitIds, Stance, EscortId);
}

/// <summary>Makes a named squad of units, taking them out of any other (UC-002).</summary>
public sealed record CreateSquadCommand(int Player, string Name, int[] UnitIds) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.CreateSquad(sim, Player, Name, UnitIds);
}

/// <summary>Breaks a squad up; its units carry on alone.</summary>
public sealed record DisbandSquadCommand(int Player, int SquadId) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.DisbandSquad(sim, Player, SquadId);
}

/// <summary>Moves units into a squad, or out of their squads when <paramref name="SquadId"/> is 0.</summary>
public sealed record AssignSquadCommand(int Player, int SquadId, int[] UnitIds) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.AssignSquad(sim, Player, SquadId, UnitIds);
}

public sealed record RenameSquadCommand(int Player, int SquadId, string Name) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.RenameSquad(sim, Player, SquadId, Name);
}

/// <summary>
/// Sets a squad's formation (UC-003); a moving squad re-forms on the way. <paramref name="Custom"/> is custom slots as
/// flattened (forward, right) cell pairs.
/// </summary>
public sealed record SetFormationCommand(int Player, int SquadId, Formation Formation, int[]? Custom = null) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetFormation(sim, Player, SquadId, Formation, Custom);
}

/// <summary>Sets the stance of a squad and every member (UC-004). Escort is per unit, through <see cref="SetStanceCommand"/>.</summary>
public sealed record SetSquadStanceCommand(int Player, int SquadId, Stance Stance) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetSquadStance(sim, Player, SquadId, Stance);
}

/// <summary>
/// Sets where the factory building at (X, Y) sends its new units (UC-006): a rally cell (<paramref name="RallyTileId"/>
/// -1 for none) and a squad they join (0 for none).
/// </summary>
public sealed record SetRallyCommand(int Player, int TileId, int X, int Y, int RallyTileId, int RallyX, int RallyY, int SquadId)
    : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetRally(sim, Player, TileId, X, Y, RallyTileId, RallyX, RallyY, SquadId);
}

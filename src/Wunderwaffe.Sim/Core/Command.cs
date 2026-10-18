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
[JsonDerivedType(typeof(SetViewedTileCommand), "view_tile")]
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
[JsonDerivedType(typeof(SetFilterCommand), "set_filter")]
[JsonDerivedType(typeof(SetFactoryModeCommand), "set_factory_mode")]
[JsonDerivedType(typeof(TriggerFactoryCommand), "trigger_factory")]
[JsonDerivedType(typeof(ResumeFactoryCommand), "resume_factory")]
[JsonDerivedType(typeof(SetTargetingCommand), "set_targeting")]
[JsonDerivedType(typeof(SetSquadTargetingCommand), "set_squad_targeting")]
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

/// <summary>Lays the content's frame (<see cref="Content.ContentDb.Frame"/>) on a cell, paying its cost from the player's cores there (<see cref="Funds"/>). Frames go under buildings too; <see cref="PlaceBuildingCommand"/> with the frame's id does the same.</summary>
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
/// Triggers the core at (X, Y): the buildings on the frame joined to it lift off the tile as one unit.
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

/// <summary>Debug only: adds items to the player's first core on a tile (none: nothing). Must be disabled in multiplayer.</summary>
public sealed record DebugGrantCommand(int Player, int TileId, string ItemId, int Amount) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile && sim.Content.Items.ContainsKey(ItemId) && Amount > 0
            && Funds.Cores(tile, Player).FirstOrDefault() is { } core)
            core.Stock!.Add(ItemId, Amount);
    }
}

/// <summary>The tile <paramref name="TileId"/> the player is looking at, or -1 for the world map. Lockstep input: rate mode (FB-013) depends on it.</summary>
public sealed record SetViewedTileCommand(int Player, int TileId) : Command(Player)
{
    internal override void Apply(Simulation sim) => sim.Player(Player).ViewedTile = sim.TryGetTile(TileId) is null ? -1 : TileId;
}

/// <summary>
/// Sends units after an enemy unit until it is destroyed (CB-001). They close to weapon range and fire. Their squads
/// also take it as their focus target (UC-002).
/// </summary>
public sealed record AttackCommand(int Player, int[] UnitIds, int TargetUnitId) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.UnitById(TargetUnitId) is not { } target || target.Owner == Player)
            return;
        var (gx, gy) = ((int)(target.X / Unit.CellScale), (int)(target.Y / Unit.CellScale));
        var tile = sim.Tiles[target.TileId];
        UnitSim.Issue(sim, Player, UnitIds, OrderKind.Attack, tile.Id, gx - tile.OriginX, gy - tile.OriginY, TargetUnitId);
        foreach (int id in UnitIds)
        {
            if (sim.UnitById(id) is { } u && u.Owner == Player && sim.Squads.Of(u) is { } squad)
                squad.TargetId = TargetUnitId;
        }
    }
}

/// <summary>Sends units to destroy the enemy building covering cell (X, Y) of a tile (CB-001, IO-006).</summary>
public sealed record AttackBuildingCommand(int Player, int[] UnitIds, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile && tile.Grid.BuildingAt(X, Y) is { } b && b.Owner != Player)
            UnitSim.Issue(sim, Player, UnitIds, OrderKind.Attack, TileId, X, Y);
    }
}

/// <summary>Moves units to a cell, stopping to fight whatever comes into weapon range on the way (CB-001).</summary>
public sealed record AttackMoveCommand(int Player, int[] UnitIds, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim) =>
        UnitSim.Issue(sim, Player, UnitIds, OrderKind.AttackMove, TileId, X, Y);
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

/// <summary>Sets units' stance (UC-004).</summary>
public sealed record SetStanceCommand(int Player, int[] UnitIds, Stance Stance) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetStance(sim, Player, UnitIds, Stance);
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

/// <summary>Sets which item the sorter at (X, Y) lets through straight ahead; null lets nothing through (FB-004).</summary>
public sealed record SetFilterCommand(int Player, int TileId, int X, int Y, string? ItemId) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            Logistics.SetFilter(sim, tile, Player, X, Y, ItemId);
    }
}

/// <summary>Sets the production setting of the factory covering (X, Y): manual or continuous (UD-006, OQ-43).</summary>
public sealed record SetFactoryModeCommand(int Player, int TileId, int X, int Y, FactoryMode Mode) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitFactory.SetMode(tile, Player, X, Y, Mode);
    }
}

/// <summary>A manual factory covering (X, Y) builds one more copy (UD-006). See <see cref="UnitFactory.CheckTrigger"/>.</summary>
public sealed record TriggerFactoryCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitFactory.Trigger(tile, Player, X, Y);
    }
}

/// <summary>A factory stopped by a failed lift-off tries again (UD-006, OQ-44). See <see cref="UnitFactory.CheckResume"/>.</summary>
public sealed record ResumeFactoryCommand(int Player, int TileId, int X, int Y) : Command(Player)
{
    internal override void Apply(Simulation sim)
    {
        if (sim.TryGetTile(TileId) is { } tile)
            UnitFactory.Resume(tile, Player, X, Y);
    }
}

/// <summary>Sets units' targeting mode (CB-007).</summary>
public sealed record SetTargetingCommand(int Player, int[] UnitIds, TargetMode Mode) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetTargeting(sim, Player, UnitIds, Mode);
}

/// <summary>Sets a squad's targeting mode, and its members' (CB-007).</summary>
public sealed record SetSquadTargetingCommand(int Player, int SquadId, TargetMode Mode) : Command(Player)
{
    internal override void Apply(Simulation sim) => UnitSim.SetSquadTargeting(sim, Player, SquadId, Mode);
}

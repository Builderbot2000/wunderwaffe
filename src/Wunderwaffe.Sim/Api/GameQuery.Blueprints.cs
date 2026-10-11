using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Blueprint and unit-production queries (silo D): recording, placement checks, stats, factories.</summary>
public sealed partial class GameQuery
{
    /// <summary>Records the buildings and frame in a rectangle of a tile as a blueprint, or null if it holds none.</summary>
    public Blueprint? BlueprintFromArea(int tileId, int x, int y, int width, int height) =>
        Sim.TryGetTile(tileId) is { } t ? Blueprints.FromArea(t, x, y, width, height) : null;

    /// <summary>Records the unit design around a unit core, or null if it wouldn't lift off.</summary>
    public Blueprint? BlueprintFromUnitCore(int tileId, int x, int y) =>
        Sim.TryGetTile(tileId) is { } t ? Blueprints.FromUnitCore(Sim, t, Player, x, y, out _) : null;

    /// <summary>Records one of the units in play as a blueprint.</summary>
    public Blueprint? BlueprintFromUnit(int unitId) => Sim.UnitById(unitId) is { } u ? Blueprints.FromUnit(u) : null;

    /// <summary>What <see cref="Core.PlaceBlueprintCommand"/> would do with the same arguments.</summary>
    public BlueprintResult CheckBlueprint(int tileId, Blueprint blueprint, int x, int y, int rotation = 0, bool flip = false) =>
        Sim.TryGetTile(tileId) is { } t ? Blueprints.Check(Sim, t, Player, blueprint, x, y, rotation, flip, out _) : BlueprintResult.Blocked;

    /// <summary>Derived stats of a design (UD-002): mass, HP, power, cost, build time, DPS, upkeep.</summary>
    public UnitStats BlueprintStats(Blueprint blueprint) => UnitStats.Of(Content, blueprint);

    /// <summary>Where a design could go (UD-004): its locomotion class, or the reason it has none.</summary>
    public LocomotionView BlueprintLocomotion(Blueprint blueprint) =>
        new(Locomotion.Check(Content.Buildings, blueprint.Blocks.Select(b => b.DefId), out var cls), cls);

    /// <summary>Derived stats of a unit in play, or null.</summary>
    public UnitStats? UnitStatsOf(int unitId) => Sim.UnitById(unitId) is { } u ? UnitStats.Of(Content, u) : null;

    /// <summary>What <see cref="Core.SetFactoryBlueprintCommand"/> would do with the same arguments (UD-006).</summary>
    public ProjectResult CheckProject(int tileId, int x, int y, Blueprint? blueprint, int ox, int oy, int rotation = 0, bool flip = false) =>
        Sim.TryGetTile(tileId) is { } t
            ? UnitFactory.CheckProject(Sim, t, Player, x, y, blueprint, ox, oy, rotation, flip, out _)
            : ProjectResult.NoFactory;

    /// <summary>A unit factory's progress, or null if the building isn't one.</summary>
    public FactoryView? Factory(int tileId, int buildingId)
    {
        if (Sim.TryGetTile(tileId) is not { } t || t.Grid.BuildingById(buildingId) is not { Factory: { } f } b)
            return null;
        return new FactoryView(
            b.Id, f.Status, f.Projected != null, f.Next, f.Steps.Count, f.Built, UnitFactory.Radius(Sim, t, b), UnitFactory.BlockCap(Sim, t, b),
            UnitFactory.StageProgress(f).Select(s => new StageProgressView(s.Stage, s.Done, s.Total)).ToList(),
            new SortedDictionary<string, int>(f.Buffer, StringComparer.Ordinal), UnitFactory.Missing(f));
    }
}

using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Combat queries (silo F): weapons, wrecks, supply cover and disrupted links.</summary>
public sealed partial class GameQuery
{
    /// <summary>Damage per second of a weapon building type, 0 for anything else.</summary>
    public int Dps(string buildingId) => Content.Buildings.TryGetValue(buildingId, out var def) ? Combat.Dps(def) : 0;

    /// <summary>What a weapon building is doing (firing, out of ammo, unpowered...), or null for other buildings.</summary>
    public WeaponStatus? WeaponStatusOf(int tileId, int buildingId) =>
        Sim.TryGetTile(tileId)?.Grid.BuildingById(buildingId) is { } b ? Combat.StatusOf(b) : null;

    /// <summary>The status of a unit's weapons, or null if it has none.</summary>
    public WeaponStatus? UnitWeaponStatus(int unitId) => Sim.UnitById(unitId) is { } u ? Combat.UnitWeaponStatus(Sim, u) : null;

    /// <summary>A unit's weapon range in cells, 0 if unarmed.</summary>
    public int WeaponRange(int unitId) => Sim.UnitById(unitId) is { } u ? Combat.WeaponRange(Sim, u) : 0;

    /// <summary>The wrecks on a tile (CB-006); they block building until cleared.</summary>
    public IReadOnlyList<Wreck> Wrecks(int tileId) => Sim.TryGetTile(tileId) is { } t ? [.. t.Grid.Wrecks] : [];

    /// <summary>Whether a wrecked port cuts the link between two tiles (IO-006).</summary>
    public bool LinkDisrupted(int tileA, int tileB) =>
        Sim.TryGetTile(tileA) is { } a && Sim.TryGetTile(tileB) is { } b && Combat.LinkDisrupted(Sim, a, b);

    /// <summary>Whether <see cref="Player"/>'s resource radius covers world cell (x, y) (FB-018).</summary>
    public bool SupplyCovers(int worldX, int worldY) =>
        ResourceRadius.Covers(Sim, Player, (long)worldX * World.Unit.CellScale + World.Unit.CellScale / 2,
            (long)worldY * World.Unit.CellScale + World.Unit.CellScale / 2);
}

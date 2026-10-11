using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>How a unit gets about, which sets the terrain it can cross (UD-004). JSON names are snake_case.</summary>
public enum LocomotionClass
{
    /// <summary>Land, shallow water and rivers; not deep water or peaks.</summary>
    Ground,

    /// <summary>Deep and shallow water and rivers.</summary>
    Naval,

    /// <summary>Anywhere.</summary>
    Air,
}

public enum LocomotionResult
{
    Ok,

    /// <summary>No locomotor and no thruster: the design has no way to move.</summary>
    None,

    /// <summary>A hull and wheels together, with no rotor to lift both.</summary>
    Conflicting,
}

/// <summary>
/// A unit's locomotion class from its blocks (UD-004). Any rotor makes it air; otherwise a hull makes it naval (wheels
/// as well conflict); otherwise wheels make it ground. A design with thrusters but no locomotor is a ground sled.
/// Propulsion always comes from thrusters; locomotors only decide where the unit may go.
/// </summary>
public static class Locomotion
{
    /// <summary>Validates a design's blocks (by building id) and finds its class. Used by lift-off validation (UD-003).</summary>
    public static LocomotionResult Check(IReadOnlyDictionary<string, BuildingDef> buildings, IEnumerable<string> blockDefIds, out LocomotionClass cls)
    {
        bool ground = false, naval = false, air = false, thrust = false;
        foreach (var id in blockDefIds)
        {
            var def = buildings[id];
            thrust |= def.Kind == BuildingKind.Thruster;
            switch (def.Locomotion)
            {
                case LocomotionClass.Ground: ground = true; break;
                case LocomotionClass.Naval: naval = true; break;
                case LocomotionClass.Air: air = true; break;
            }
        }
        cls = air ? LocomotionClass.Air : naval ? LocomotionClass.Naval : LocomotionClass.Ground;
        if (!air && naval && ground)
            return LocomotionResult.Conflicting;
        return air || naval || ground || thrust ? LocomotionResult.Ok : LocomotionResult.None;
    }

    /// <summary>The design's class, or null if <see cref="Check"/> fails.</summary>
    public static LocomotionClass? Of(IReadOnlyDictionary<string, BuildingDef> buildings, IEnumerable<string> blockDefIds) =>
        Check(buildings, blockDefIds, out var cls) == LocomotionResult.Ok ? cls : null;

    /// <summary>Whether a unit of class <paramref name="cls"/> may cross terrain <paramref name="terrain"/>.</summary>
    public static bool CanCross(LocomotionClass cls, TerrainDef terrain) => cls switch
    {
        LocomotionClass.Ground => terrain.GroundPassable,
        LocomotionClass.Naval => terrain.NavalPassable,
        _ => true,
    };

    /// <summary>Whether <paramref name="cls"/> may cross world cell (gx, gy). Cells outside every tile never are.</summary>
    public static bool CanCross(Simulation sim, LocomotionClass cls, int gx, int gy) =>
        sim.Map.TileAtCell(gx, gy) >= 0 && CanCross(cls, sim.Content.TerrainList[sim.Map.TerrainAt(gx, gy)]);
}

using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// A building carried by a unit. <paramref name="GX"/>/<paramref name="GY"/> is its top-left cell in the unit's own
/// grid, whose origin is the unit core's cell; the footprint and rotation are as they were on the tile.
/// </summary>
public sealed record UnitBlock(string DefId, int GX, int GY, int Width, int Height, int Rotation, string? RecipeId);

/// <summary>One cell of a unit's body. Offsets are from the centre of mass, in the unit's own frame.</summary>
public readonly record struct UnitCell(int GX, int GY, int OffX, int OffY, int Mass);

/// <summary>An engine on a unit. <paramref name="Dir"/> is the way it pushes, in unit-frame angle.</summary>
public readonly record struct Thruster(int Thrust, int Dir);

/// <summary>
/// A mobile rigid body built from the blocks that stood on a tile's frame (see <see cref="UnitAssembly"/>). Position
/// is the centre of mass in world sub-cells (<see cref="CellScale"/> to a cell; see <see cref="WorldMap"/>), velocity is
/// sub-cells per tick and angle is a fraction of a turn (<see cref="Trig.Turn"/>). The tile it is on is the tile of the
/// cell under its centre. All integer, so it is deterministic (NF-004).
/// Blocks are inert while the unit is active; they only add mass and thrust.
/// </summary>
public sealed class Unit
{
    /// <summary>Sub-cells per cell.</summary>
    public const int CellScale = 65536;

    /// <summary>Mass of the frame under each cell.</summary>
    public const int FrameMass = 2;

    /// <summary>Linear drag: each tick velocity is multiplied by (DragDiv - 1) / DragDiv.</summary>
    public const int DragDiv = 16;

    /// <summary>Thrust force to acceleration, in sub-cells per tick squared per unit of force per unit of mass.</summary>
    public const int ThrustScale = CellScale / 1024;

    /// <summary>Lever arm of the turning torque a unit gets from its engines, in sub-cells.</summary>
    const int TurnArm = 4 * CellScale;

    /// <summary>Angle units per radian: <c>Turn / 2pi</c>.</summary>
    const long AnglePerRadian = 10430;

    internal Unit(int id, int owner, int tileId, int x, int y, IReadOnlyList<UnitBlock> blocks, List<(int GX, int GY, int Mass)> cells,
        IReadOnlyDictionary<string, BuildingDef> defs, int coreGX, int coreGY)
    {
        Id = id;
        Owner = owner;
        TileId = tileId;
        Blocks = blocks;

        long mass = cells.Sum(c => (long)c.Mass);
        long comX = cells.Sum(c => (c.GX * (long)CellScale + CellScale / 2) * c.Mass) / mass;
        long comY = cells.Sum(c => (c.GY * (long)CellScale + CellScale / 2) * c.Mass) / mass;

        var body = new List<UnitCell>(cells.Count);
        long inertia = 0, reach = 0;
        foreach (var (gx, gy, m) in cells)
        {
            int ox = (int)(gx * (long)CellScale + CellScale / 2 - comX), oy = (int)(gy * (long)CellScale + CellScale / 2 - comY);
            body.Add(new UnitCell(gx, gy, ox, oy, m));
            long r2 = (long)ox * ox + (long)oy * oy;
            // Point mass at the cell centre plus the cell's own spread (a square: m * side^2 / 6).
            inertia += m * (r2 + (long)CellScale * CellScale / 6);
            reach = Math.Max(reach, UnitPhysics.ISqrt(r2));
        }
        Cells = body;
        Mass = (int)mass;
        Inertia = inertia;
        Radius = (int)reach + CellScale / 2;
        CoreOffX = (int)(coreGX * (long)CellScale + CellScale / 2 - comX);
        CoreOffY = (int)(coreGY * (long)CellScale + CellScale / 2 - comY);
        CoreGX = coreGX;
        CoreGY = coreGY;
        // (x, y) is where the core's cell centre stands in the world; the centre of mass is offset from it.
        X = x - LocalCentre(coreGX) + (int)comX;
        Y = y - LocalCentre(coreGY) + (int)comY;

        var thrusters = new List<Thruster>();
        foreach (var b in blocks)
        {
            if (defs[b.DefId] is { Kind: BuildingKind.Thruster } t)
                thrusters.Add(new Thruster(t.Thrust, (b.Rotation & 3) * Trig.Quarter));
        }
        Thrusters = thrusters;
        TotalThrust = thrusters.Sum(t => t.Thrust);

        // The strongest engine (first on ties) is the one the unit turns toward its goal.
        if (thrusters.Count > 0)
        {
            var main = thrusters.MaxBy(t => t.Thrust);
            MainDir = main.Dir;
            long push = thrusters.Sum(t => (long)t.Thrust * Math.Max(0, Trig.Cos(t.Dir - main.Dir)) / Trig.One);
            // Terminal speed under drag, with a margin so the engines keep some authority.
            MaxSpeed = (int)(push * ThrustScale * DragDiv * 9 / (10 * mass));
        }
    }

    static int LocalCentre(int g) => g * CellScale + CellScale / 2;

    public int Id { get; }
    public int Owner { get; }
    public int TileId { get; internal set; }

    /// <summary>Centre of mass, in world sub-cells.</summary>
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int VX { get; internal set; }
    public int VY { get; internal set; }

    /// <summary>Heading in <see cref="Trig.Turn"/>ths of a turn. 0 is the orientation the blocks were built in.</summary>
    public int Angle { get; internal set; }

    /// <summary>Angle units per tick.</summary>
    public int AngVel { get; internal set; }

    public IReadOnlyList<UnitBlock> Blocks { get; }
    public IReadOnlyList<UnitCell> Cells { get; }
    public IReadOnlyList<Thruster> Thrusters { get; }
    public int Mass { get; }
    public long Inertia { get; }

    /// <summary>Distance from the centre of mass to the edge of the farthest cell.</summary>
    public int Radius { get; }

    public int TotalThrust { get; }
    public int MainDir { get; }

    /// <summary>Speed it settles at flying straight, in sub-cells per tick. 0 means it cannot move.</summary>
    public int MaxSpeed { get; }

    /// <summary>The unit core's cell in the unit grid, and its offset from the centre of mass.</summary>
    public int CoreGX { get; }
    public int CoreGY { get; }
    public int CoreOffX { get; }
    public int CoreOffY { get; }

    /// <summary>Tiles still to cross, in order, or null when the unit has no move order.</summary>
    internal int[]? Route { get; set; }
    internal int RouteIndex { get; set; }

    /// <summary>Where the current move order ends, in world sub-cells, inside <see cref="DestTileId"/>.</summary>
    public int DestTileId { get; internal set; }
    public int DestX { get; internal set; }
    public int DestY { get; internal set; }

    // The path across the current tile (and the next, when crossing) to the next goal, in world cells (see UnitPath).
    // Recomputed whenever the order, the tile, the goal or the tiles' buildings change, which `PathKey` records.
    internal (int X, int Y)[]? Path { get; set; }
    internal int PathIndex { get; set; }
    internal (int Tile, int Next, long Revision, int GoalX, int GoalY)? PathKey { get; set; }

    /// <summary>The world cell, in the next tile, where the unit crosses the current border.</summary>
    internal (int Tile, int Next, int X, int Y)? Portal { get; set; }

    public bool Moving => Route != null;

    /// <summary>Angular acceleration limit in angle units per tick squared, from the engines' turning torque.</summary>
    internal int TurnAccel => (int)Math.Max(1, TotalThrust * (long)ThrustScale * TurnArm * AnglePerRadian / Inertia);

    /// <summary>A cell centre in world sub-cells, given the unit's current position and heading.</summary>
    public (long X, long Y) CellWorld(UnitCell c)
    {
        var (rx, ry) = Trig.Rotate(c.OffX, c.OffY, Angle);
        return (X + rx, Y + ry);
    }
}

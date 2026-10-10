namespace Wunderwaffe.Sim.World;

/// <summary>A tile edge. The order is fixed because neighbour iteration and hashing depend on it.</summary>
public enum Direction
{
    North,
    East,
    South,
    West,
}

public static class Directions
{
    public static readonly Direction[] All = [Direction.North, Direction.East, Direction.South, Direction.West];

    /// <summary>Tile-coordinate step. North is -y, matching the screen.</summary>
    public static int Dx(this Direction d) => d switch { Direction.East => 1, Direction.West => -1, _ => 0 };

    public static int Dy(this Direction d) => d switch { Direction.South => 1, Direction.North => -1, _ => 0 };

    /// <summary>
    /// The way a building faces at a given rotation: quarter turns clockwise from east, as the sprites are drawn.
    /// </summary>
    public static Direction FromRotation(int rotation) => (rotation & 3) switch
    {
        0 => Direction.East,
        1 => Direction.South,
        2 => Direction.West,
        _ => Direction.North,
    };

    public static Direction Opposite(this Direction d) => (Direction)(((int)d + 2) & 3);
}

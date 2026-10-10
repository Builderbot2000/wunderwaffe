namespace Wunderwaffe.Sim.World;

/// <summary>
/// Where ports may go and which ports pair up (IO-001, IO-002). A port faces an edge (its rotation) and must
/// sit within <see cref="ZoneDepth"/> cells of it. An export port sends to the import ports in the same lane
/// of the facing edge's opposite side on the neighbouring tile, so tiles' ports line up like the tiles do.
/// </summary>
public static class PortRules
{
    public const int ZoneDepth = 3;

    /// <summary>Ticks an item spends per tile of distance crossing between tiles. Adjacent tiles are distance 1.</summary>
    public const int TransitTicksPerTile = 180;

    /// <summary>The tile edge a port at this rotation faces.</summary>
    public static Direction SideOf(int rotation) => Directions.FromRotation(rotation);

    /// <summary>How far the footprint's farthest cell is from the given edge, counting the edge row as 0.</summary>
    public static int Depth(TileGrid grid, Direction side, int x, int y, int w, int h) => side switch
    {
        Direction.North => y + h - 1,
        Direction.South => grid.Height - 1 - y,
        Direction.West => x + w - 1,
        _ => grid.Width - 1 - x,
    };

    public static bool InZone(TileGrid grid, int rotation, int x, int y, int w, int h) =>
        Depth(grid, SideOf(rotation), x, y, w, h) < ZoneDepth;

    /// <summary>Position along the edge: the column for north/south ports, the row for east/west ones.</summary>
    public static int Lane(PlacedBuilding port) =>
        SideOf(port.Rotation) is Direction.North or Direction.South ? port.X : port.Y;
}

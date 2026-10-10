namespace Wunderwaffe.Sim.World;

/// <summary>
/// Where ports may go and which ports pair up (IO-001, IO-002). Each tile has one I/O zone per neighbour: the cells
/// within <see cref="ZoneDepth"/> steps of the shared border, on the side facing that neighbour, with no zone
/// overlapping another (see <see cref="TileGrid.ZoneAt"/>). A port must stand wholly in one zone, and that zone decides
/// the tile it links to. Tile A's zone for B is paired with B's zone for A: an export port in one sends to the import
/// ports in the other, taking turns between them, and items flow both ways. A port's rotation only matters to the
/// belts around it.
/// </summary>
public static class PortRules
{
    /// <summary>How deep a zone reaches into the tile from the border, in cells.</summary>
    public const int ZoneDepth = 4;

    /// <summary>Ticks an item spends per tile of distance crossing between tiles. Adjacent tiles are distance 1.</summary>
    public const int TransitTicksPerTile = 180;

    /// <summary>The neighbour whose zone the whole footprint is in, or -1 if it isn't wholly in one zone.</summary>
    public static int ZoneOf(TileGrid grid, int x, int y, int w, int h)
    {
        int zone = grid.ZoneAt(x, y);
        for (int cy = y; cy < y + h && zone >= 0; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (grid.ZoneAt(cx, cy) != zone)
                    return -1;
            }
        }
        return zone;
    }

    public static bool InZone(TileGrid grid, int x, int y, int w, int h) => ZoneOf(grid, x, y, w, h) >= 0;

    /// <summary>The tile a port on <paramref name="grid"/> links to.</summary>
    public static int PartnerOf(TileGrid grid, PlacedBuilding port) => grid.ZoneAt(port.X, port.Y);
}

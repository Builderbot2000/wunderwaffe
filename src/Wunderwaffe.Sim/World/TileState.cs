using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// One territory of the world map. Its local map (<see cref="Grid"/>) is its bounding box in the world map, with
/// <see cref="OriginX"/>, <see cref="OriginY"/> as the box's top-left cell: local (x, y) is global (OriginX + x, OriginY + y).
/// </summary>
public sealed class TileState(int id, int originX, int originY, TileGrid grid)
{
    public const int Unowned = -1;

    public int Id { get; } = id;

    /// <summary>Generated place name, unique in the world. Terrain and the like are stats, not part of it.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Top-left of the tile's bounding box, in world cells.</summary>
    public int OriginX { get; } = originX;
    public int OriginY { get; } = originY;

    /// <summary>Centre of the tile's cells, in world cells: where its label goes on the world map.</summary>
    public int CenterX { get; internal set; }
    public int CenterY { get; internal set; }

    /// <summary>Ids of the tiles sharing a border with this one, ascending (WM-003).</summary>
    public IReadOnlyList<int> Neighbors { get; internal set; } = [];

    /// <summary>The terrain covering most of the tile, by id (WM-002).</summary>
    public string Biome { get; internal set; } = "";

    public int Owner { get; set; } = Unowned;



    public TileGrid Grid { get; } = grid;

    /// <summary>
    /// Centre of the tile's victory circle (WM-014), in local cells; its radius is <see cref="Balance.CircleRadius"/>.
    /// Placed by generation on the richest deposits clear of the border; hand-built tiles keep it at their centre.
    /// </summary>
    public int CircleX { get; internal set; }
    public int CircleY { get; internal set; }

    /// <summary>
    /// Claim progress in ticks (WM-006), per player with a building in the circle who doesn't own the tile. A player
    /// drops out, back to zero, the tick they have no building in it.
    /// </summary>
    public IReadOnlyDictionary<int, int> ClaimTicks => Claims;

    internal SortedDictionary<int, int> Claims { get; } = new();

    /// <summary>Whose buildings are in the circle, cached until <see cref="TileGrid.Revision"/> changes.</summary>
    internal (int Revision, int[] Players) CircleCache = (-1, []);

    /// <summary>Buildings with a build radius (WM-015), every player's, in id order; cached until <see cref="TileGrid.Revision"/> changes.</summary>
    internal (int Revision, PlacedBuilding[] Majors) RadiusCache = (-1, []);

    /// <summary>Whether local cell (x, y) is in the victory circle: its centre is within the radius of the circle's centre.</summary>
    public bool InCircle(int x, int y)
    {
        int dx = x - CircleX, dy = y - CircleY;
        return dx * dx + dy * dy <= Balance.CircleRadius * Balance.CircleRadius;
    }

    /// <summary>Whether any cell of the w x h footprint at (x, y) is in the victory circle.</summary>
    public bool InCircle(int x, int y, int w, int h)
    {
        // The footprint cell nearest the centre decides.
        int cx = Math.Clamp(CircleX, x, x + w - 1), cy = Math.Clamp(CircleY, y, y + h - 1);
        return InCircle(cx, cy);
    }

    /// <summary>Per-item production and consumption, and link throughput, over the last 10 minutes (FB-011).</summary>
    public ProductionStats Stats { get; } = new();

    public (int X, int Y) ToGlobal(int x, int y) => (OriginX + x, OriginY + y);
}

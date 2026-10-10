using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>The ownership states a tile can be in (WM-004).</summary>
public enum TileOwnership
{
    Unclaimed,
    Owned,
    Contested,
}

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

    /// <summary>Set while hostile presence disputes the tile. Nothing sets it until combat exists (CB-001).</summary>
    public bool Contested { get; set; }

    public TileOwnership Ownership =>
        Contested ? TileOwnership.Contested : Owner == Unowned ? TileOwnership.Unclaimed : TileOwnership.Owned;

    public Inventory Inventory { get; } = new();

    public TileGrid Grid { get; } = grid;

    public (int X, int Y) ToGlobal(int x, int y) => (OriginX + x, OriginY + y);

    public (int X, int Y) ToLocal(int gx, int gy) => (gx - OriginX, gy - OriginY);
}

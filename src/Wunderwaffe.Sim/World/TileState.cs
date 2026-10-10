using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>The ownership states a tile can be in (WM-004).</summary>
public enum TileOwnership
{
    Unclaimed,
    Owned,
    Contested,
}

public sealed class TileState(int id, int x, int y, int width, int height)
{
    public const int Unowned = -1;

    public int Id { get; } = id;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Owner { get; set; } = Unowned;

    /// <summary>Set while hostile presence disputes the tile. Nothing sets it until combat exists (CB-001).</summary>
    public bool Contested { get; set; }

    public TileOwnership Ownership =>
        Contested ? TileOwnership.Contested : Owner == Unowned ? TileOwnership.Unclaimed : TileOwnership.Owned;

    public Inventory Inventory { get; } = new();

    public TileGrid Grid { get; } = new(width, height);
}

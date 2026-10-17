using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Helpers for building small factories in tests. Coordinates are cells; rotation 0 faces east.</summary>
static class Harness
{
    public const int Me = 0;
    public const int Them = 1;

    /// <summary>An empty sim on the real content with no power draw (<see cref="TestContent.Unpowered"/>), for hand-built tiles.</summary>
    public static Simulation NewSim() => new(TestContent.Unpowered());

    /// <summary>An empty sim on the real content, power draws included (FB-005).</summary>
    public static Simulation PoweredSim() => new(TestContent.Load());

    /// <summary>
    /// An owned tile with plenty of items to pay for buildings: a stocked bank core of the owner's in the bottom-right
    /// corner cell (FB-019, see <see cref="Bank"/>). Tests measure deltas, not totals.
    /// </summary>
    public static TileState Tile(Simulation sim, int x = 0, int y = 0, int size = 30, int owner = Me)
    {
        var tile = sim.AddTile(x, y, owner, size);
        if (owner != TileState.Unowned)
            Bank(sim, tile, owner, size - 1, size - 1);
        return tile;
    }

    /// <summary>
    /// A bank core (<see cref="TestContent.Bank"/>: 1x1, no power, no practical capacity) of <paramref name="player"/>'s
    /// at (x, y), unpaid, holding 100,000 of each basic item.
    /// </summary>
    public static PlacedBuilding Bank(Simulation sim, TileState tile, int player, int x, int y)
    {
        var core = sim.AddCore(tile, player, x, y, "bank");
        foreach (var item in new[] { "copper_ore", "iron_ore", "copper_plate", "iron_plate", "steel" })
            core.Stock!.Add(item, 100_000);
        return core;
    }

    /// <summary>Puts items into a core's stock.</summary>
    public static void Stock(PlacedBuilding core, string item, long n) => core.Stock!.Add(item, n);

    /// <summary>
    /// The player's first core on the tile, which pays first (OQ-38); if they have none, an empty bank put unpaid on the
    /// free buildable unframed cell nearest the bottom-right corner, out of the way of test layouts.
    /// </summary>
    public static PlacedBuilding Fund(Simulation sim, TileState tile, int player = Me)
    {
        if (Funds.Cores(tile, player).FirstOrDefault() is { } core)
            return core;
        var g = tile.Grid;
        for (int y = g.Height - 1; y >= 0; y--)
        {
            for (int x = g.Width - 1; x >= 0; x--)
            {
                if (g.InBounds(x, y) && g.Buildable(x, y) && g.IsFree(x, y, 1, 1) && !g.HasFrame(x, y))
                    return sim.AddCore(tile, player, x, y, "bank");
            }
        }
        throw new InvalidOperationException($"no room for a core on tile {tile.Id}");
    }

    /// <summary>
    /// Top-left of the first w x h block of free, buildable cells, row by row. For generated tiles, whose shape and
    /// terrain depend on the seed.
    /// </summary>
    public static (int X, int Y) Open(TileState tile, int w, int h)
    {
        var g = tile.Grid;
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (g.InBounds(x, y, w, h) && g.Buildable(x, y, w, h) && g.IsFree(x, y, w, h))
                    return (x, y);
            }
        }
        throw new InvalidOperationException($"no open {w}x{h} block on tile {tile.Id}");
    }

    public static void Place(Simulation sim, TileState tile, string building, int x, int y, int rotation = 0, string? recipe = null) =>
        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, building, x, y, rotation, recipe));

    /// <summary>Lays a deposit under a w x h block, so a drill there mines <paramref name="item"/>.</summary>
    public static void Ore(TileState tile, string item, int x, int y, int w = 2, int h = 2)
    {
        for (int cy = y; cy < y + h; cy++)
            for (int cx = x; cx < x + w; cx++)
                tile.Grid.SetDeposit(cx, cy, item);
    }

    /// <summary>A straight run of conveyors starting at (x, y) and heading the way <paramref name="rotation"/> faces.</summary>
    public static void Belt(Simulation sim, TileState tile, int x, int y, int length, int rotation = 0)
    {
        var d = Directions.FromRotation(rotation);
        for (int i = 0; i < length; i++)
            Place(sim, tile, "conveyor", x + d.Dx() * i, y + d.Dy() * i, rotation);
    }

    public static void Run(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
    }

    /// <summary>What every core on the tile holds of an item together, whoever owns them.</summary>
    public static long Items(TileState tile, string item) => tile.Grid.Cores.Sum(c => c.Stock!.Get(item));
}

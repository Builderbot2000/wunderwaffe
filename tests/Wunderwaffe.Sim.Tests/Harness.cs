using Wunderwaffe.Sim.Content;
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

    /// <summary>What a start's core holds: the default start prefab's items (FB-021).</summary>
    public static IReadOnlyDictionary<string, int> StartItems(Simulation sim) => sim.Content.Prefabs[GameSetup.DefaultStart].Items!;

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
                if (g.InBounds(x, y) && g.Buildable(x, y) && g.IsFree(x, y, 1, 1) && !g.Has(BuildingKind.Frame, x, y))
                    return sim.AddCore(tile, player, x, y, "bank");
            }
        }
        throw new InvalidOperationException($"no room for a core on tile {tile.Id}");
    }

    /// <summary>Adds items to the player's funds on the tile: into their first core there (<see cref="Fund(Simulation, TileState, int)"/>).</summary>
    public static PlacedBuilding Fund(Simulation sim, TileState tile, string item, long n, int player = Me)
    {
        var core = Fund(sim, tile, player);
        core.Stock!.Add(item, n);
        return core;
    }

    /// <inheritdoc cref="Fund(Simulation, TileState, string, long, int)"/>
    public static PlacedBuilding Fund(Simulation sim, TileState tile, IReadOnlyDictionary<string, int> items, int player = Me)
    {
        var core = Fund(sim, tile, player);
        core.Stock!.Add(items);
        return core;
    }

    /// <summary>A core of <paramref name="player"/>'s at (x, y), put down unpaid and empty (see <see cref="Stock"/>).</summary>
    public static PlacedBuilding PutCore(Simulation sim, TileState tile, int player, int x, int y, string? defId = null) =>
        sim.AddCore(tile, player, x, y, defId);

    /// <summary>
    /// The standard test unit (WM-016), laid by <paramref name="player"/>'s commands and paid from the tile's funds: a
    /// thruster pushing east at (x - 1, y), the 2x2 <c>core</c> with its top-left at (x, y), then <paramref name="right"/>
    /// side by side along y from x + 2, all on a frame two rows deep (deeper for taller blocks). Steps once so it
    /// stands. Lift it off with <see cref="LiftOff"/> at (x, y).
    /// </summary>
    public static void LayUnit(Simulation sim, TileState tile, int x, int y, int player, params string[] right)
    {
        var defs = right.Select(id => sim.Content.Buildings[id]).ToList();
        int end = x + 2 + defs.Sum(d => d.Width), h = Math.Max(2, defs.Select(d => d.Height).DefaultIfEmpty(0).Max());
        for (int cy = y; cy < y + h; cy++)
            for (int cx = x - 1; cx < end; cx++)
                sim.Enqueue(new PlaceFrameCommand(player, tile.Id, cx, cy));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "thruster", x - 1, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "core", x, y));
        int bx = x + 2;
        foreach (var d in defs)
        {
            sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, d.Id, bx, y));
            bx += d.Width;
        }
        sim.Step();
    }

    /// <summary>The standard scout: <see cref="LayUnit"/> with a second thruster on the right, so it flies east and west.</summary>
    public static void LayScout(Simulation sim, TileState tile, int x, int y, int player = Me) =>
        LayUnit(sim, tile, x, y, player, "thruster");

    /// <summary>Lifts off the unit whose core is at (x, y) and returns it.</summary>
    public static Unit LiftOff(Simulation sim, TileState tile, int x, int y, int player = Me)
    {
        sim.Enqueue(new ActivateUnitCommand(player, tile.Id, x, y));
        sim.Step();
        return sim.Units[^1];
    }

    /// <summary>A standard scout (<see cref="LayScout"/>) at (x, y), lifted off.</summary>
    public static Unit Scout(Simulation sim, TileState tile, int x = 10, int y = 10, int player = Me)
    {
        LayScout(sim, tile, x, y, player);
        return LiftOff(sim, tile, x, y, player);
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

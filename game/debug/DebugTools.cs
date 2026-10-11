using System;
using System.Collections.Generic;
using System.Linq;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Debug;

/// <summary>Debug cheats and the NF-001 stress test (F3, F4, <c>-- --stress</c>). All of it goes through commands.</summary>
public static class DebugTools
{
    const int StressBuildings = 2000;

    /// <summary>F4: plenty of every item on the viewed tile.</summary>
    public static void Cheat(SimHost host)
    {
        foreach (var item in host.Content.Items.Keys)
            host.Sim.Enqueue(new DebugGrantCommand(host.LocalPlayer, host.ViewedTile.Id, item, 1000));
    }

    /// <summary>
    /// Covers the viewed tile with ~2,000 producing buildings, then benchmarks the sim (<see cref="StressBench"/>).
    /// </summary>
    public static void StressFill(SimHost host) => StressFill(host, null);

    static void StressFill(SimHost host, Action? prepare)
    {
        var sim = host.Sim;
        var tile = host.ViewedTile;
        int me = host.LocalPlayer;
        sim.Enqueue(new DebugGrantCommand(me, tile.Id, "copper_ore", 1_000_000));
        sim.Enqueue(new DebugGrantCommand(me, tile.Id, "iron_ore", 1_000_000));

        int placed = 0;
        for (int y = 0; y < tile.Grid.Height - 1 && placed < StressBuildings; y += 2)
        {
            for (int x = 0; x < tile.Grid.Width - 1 && placed < StressBuildings; x += 2)
            {
                if (!tile.Grid.IsFree(x, y, 2, 2))
                    continue;
                bool onOre = Placement.Check(sim, tile, me, "drill", x, y, 0, null, out _) != PlaceResult.NeedsDeposit;
                sim.Enqueue(onOre
                    ? new PlaceBuildingCommand(me, tile.Id, "drill", x, y)
                    : new PlaceBuildingCommand(me, tile.Id, "smelter", x, y, RecipeId: "smelt_iron"));
                placed++;
            }
        }
        host.AddChild(new StressBench { StartTick = sim.Tick + 1, Prepare = prepare });
    }

    /// <summary>
    /// <c>-- --stress</c>: the NF-001 scene. The local player claims every tile, nearest first; the viewed tile gets the
    /// 2,000-building fill and runs the full sim, and every other tile gets drills around its core and runs in rate mode.
    /// </summary>
    public static void StressWorld(SimHost host)
    {
        var sim = host.Sim;
        int me = host.LocalPlayer;
        sim.Enqueue(new SetViewedTileCommand(me, host.ViewedTile.Id));
        foreach (var t in sim.Tiles)
        {
            sim.Enqueue(new DebugGrantCommand(me, t.Id, "copper_ore", 10_000));
            sim.Enqueue(new DebugGrantCommand(me, t.Id, "iron_ore", 10_000));
        }
        // Breadth first from the tiles already owned, so each claim borders one made before it (same tick, in order).
        var seen = sim.Tiles.Where(t => t.Owner == me).Select(t => t.Id).ToHashSet();
        var queue = new Queue<TileState>(sim.Tiles.Where(t => t.Owner == me));
        while (queue.Count > 0)
        {
            foreach (var n in sim.Neighbors(queue.Dequeue()))
            {
                if (n.Owner != TileState.Unowned || !seen.Add(n.Id))
                    continue;
                sim.Enqueue(new ClaimTileCommand(me, n.Id));
                queue.Enqueue(n);
            }
        }
        StressFill(host, () => FurnishClaimed(host));
    }

    /// <summary>Drills on whatever deposits touch each claimed tile's core, so the rate-mode tiles have flows to run.</summary>
    static void FurnishClaimed(SimHost host)
    {
        var sim = host.Sim;
        int me = host.LocalPlayer;
        (int X, int Y)[] around = [(-2, 0), (-2, 1), (3, 0), (3, 1), (0, -2), (1, -2), (0, 3), (1, 3)];
        foreach (var t in sim.Tiles)
        {
            if (t.Owner != me || t == host.ViewedTile || t.Grid.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Core) is not { } core)
                continue;
            foreach (var (dx, dy) in around)
            {
                if (Placement.Check(sim, t, me, "drill", core.X + dx, core.Y + dy, 0, null, out _) == PlaceResult.Ok)
                    sim.Enqueue(new PlaceBuildingCommand(me, t.Id, "drill", core.X + dx, core.Y + dy));
            }
        }
    }
}

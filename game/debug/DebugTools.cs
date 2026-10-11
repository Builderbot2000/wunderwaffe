using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Debug;

/// <summary>Debug cheats and the NF-001 stress test (F3, F4, <c>-- --stress</c>). All of it goes through commands.</summary>
public static class DebugTools
{
    /// <summary>F4: plenty of every item on the viewed tile.</summary>
    public static void Cheat(SimHost host)
    {
        foreach (var item in host.Content.Items.Keys)
            host.Send(new DebugGrantCommand(host.LocalPlayer, host.ViewedTile.Id, item, 1000));
    }

    /// <summary>F3: covers the viewed tile with ~2,000 producing buildings, then benchmarks the sim (<see cref="StressBench"/>).</summary>
    public static void StressFill(SimHost host)
    {
        var sim = host.Sim;
        var tile = host.ViewedTile;
        int me = host.LocalPlayer;
        host.Send(new DebugGrantCommand(me, tile.Id, "copper_ore", 1_000_000));
        host.Send(new DebugGrantCommand(me, tile.Id, "iron_ore", 1_000_000));
        StressScene.Fill(sim, tile, me, host.Send);
        host.AddChild(new StressBench { StartTick = sim.Tick + 1 });
    }

    /// <summary>
    /// <c>-- --stress</c>: the NF-001 scene (<see cref="StressScene"/>: 8 full-sim tiles of 2,000 buildings, each viewed
    /// by a player, plus 200 rate-mode tiles), opened on the first full tile, then benchmarked.
    /// </summary>
    public static void StressWorld(SimHost host)
    {
        host.StartWith(StressScene.Build(host.Content).Sim, ViewMode.Tile);
        host.AddChild(new StressBench { StartTick = host.Sim.Tick });
    }
}

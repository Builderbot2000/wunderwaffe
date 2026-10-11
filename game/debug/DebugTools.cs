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
            host.Send(new DebugGrantCommand(host.LocalPlayer, host.ViewedTile.Id, item, 1000));
    }

    /// <summary>
    /// Covers the viewed tile with ~2,000 producing buildings, then benchmarks the sim (<see cref="StressBench"/>).
    /// </summary>
    public static void StressFill(SimHost host)
    {
        var sim = host.Sim;
        var tile = host.ViewedTile;
        int me = host.LocalPlayer;
        host.Send(new DebugGrantCommand(me, tile.Id, "copper_ore", 1_000_000));
        host.Send(new DebugGrantCommand(me, tile.Id, "iron_ore", 1_000_000));

        int placed = 0;
        for (int y = 0; y < tile.Grid.Height - 1 && placed < StressBuildings; y += 2)
        {
            for (int x = 0; x < tile.Grid.Width - 1 && placed < StressBuildings; x += 2)
            {
                if (!tile.Grid.IsFree(x, y, 2, 2))
                    continue;
                bool onOre = Placement.Check(sim, tile, me, "drill", x, y, 0, null, out _) != PlaceResult.NeedsDeposit;
                host.Send(onOre
                    ? new PlaceBuildingCommand(me, tile.Id, "drill", x, y)
                    : new PlaceBuildingCommand(me, tile.Id, "smelter", x, y, RecipeId: "smelt_iron"));
                placed++;
            }
        }
        host.AddChild(new StressBench { StartTick = sim.Tick + 1 });
    }
}

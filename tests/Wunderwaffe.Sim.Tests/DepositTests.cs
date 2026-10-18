using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>RS-004 deposit depletion: patches found at generation, drained by extraction, emptied, drawn by level, saved.</summary>
public class DepositTests
{
    [Fact]
    public void PatchesAreFoundAtGeneration()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default with { RichnessPercent = 150 });
        var g = sim.Tiles[sim.StartTileId].Grid;
        Assert.NotEmpty(g.Patches.All);
        var cells = new int[g.Patches.All.Count + 1];
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (g.DepositAt(x, y) is not { } item)
                {
                    Assert.Equal(0, g.Patches.IdAt(x, y));
                    continue;
                }
                var p = g.Patches.At(x, y)!;
                Assert.Equal(item, p.Item);
                cells[p.Id]++;
                // Connected cells of one item are one patch.
                if (g.DepositAt(x + 1, y) == item)
                    Assert.Equal(p.Id, g.Patches.IdAt(x + 1, y));
                if (g.DepositAt(x, y + 1) == item)
                    Assert.Equal(p.Id, g.Patches.IdAt(x, y + 1));
            }
        }
        foreach (var p in g.Patches.All)
        {
            Assert.Equal(p.Cells, cells[p.Id]);
            Assert.Equal((long)sim.Content.Items[p.Item].Deposit!.PerCell * p.Cells * 150 / 100, p.Start);
            Assert.Equal(p.Start, p.Amount);
            Assert.Equal(p.Def.MaxLevel, p.Level);
        }
    }

    [Fact]
    public void EveryItemMadeComesOutOfThePatchUnderTheDrill()
    {
        var sim = NewSim();
        var t = Tile(sim);
        var patch = Patch(sim, t, "copper_ore", 2, 2, 1000);
        Place(sim, t, "drill", 2, 2);
        Place(sim, t, "core_large", 4, 2);
        Run(sim, 10 * Simulation.TicksPerSecond);
        long made = t.Stats.TotalMade.Single(m => m.Item == "copper_ore").Total;
        Assert.True(made > 0);
        Assert.Equal(1000 - made, patch.Amount);
    }

    [Fact]
    public void AnEmptiedPatchStopsItsDrillsAndIsNoLongerADeposit()
    {
        var sim = NewSim();
        var t = Tile(sim);
        var patch = Patch(sim, t, "copper_ore", 2, 2, 5, w: 4);
        Place(sim, t, "drill", 2, 2);
        Place(sim, t, "drill", 4, 2);
        Place(sim, t, "core_large", 2, 4);                  // Under both drills.
        Run(sim, 20 * Simulation.TicksPerSecond);

        Assert.True(patch.Empty);
        Assert.Equal(5, t.Grid.BuildingAt(2, 4)!.Stock!.Get("copper_ore"));
        for (int x = 2; x < 6; x++)
        {
            Assert.Null(t.Grid.DepositAt(x, 2));
            Assert.Equal(0, t.Grid.Patches.IdAt(x, 3));
        }
        Assert.Equal(MachineStatus.Depleted, t.Grid.BuildingAt(2, 2)!.Machine!.Status);
        Assert.Equal(MachineStatus.Depleted, t.Grid.BuildingAt(4, 2)!.Machine!.Status);

        sim.Enqueue(new RemoveBuildingCommand(Me, t.Id, 2, 2));
        sim.Step();
        Assert.Equal(PlaceResult.NeedsDeposit, Placement.Check(sim, t, Me, "drill", 2, 2, 0, null, out _));
    }

    [Fact]
    public void TheTileRedrawsOnlyWhenAPatchCrossesADensityLevel()
    {
        // 4 cells at levels [50, 120] per cell: level 2 down to 480, level 1 down to 200, then 0, then empty.
        var sim = NewSim();
        var t = Tile(sim);
        var patch = Patch(sim, t, "copper_ore", 2, 2, 500);
        Assert.Equal(2, patch.Level);
        Place(sim, t, "drill", 2, 2);
        Place(sim, t, "core_large", 4, 2);
        sim.Step();
        var levels = new List<int>();
        int revision = t.Grid.Patches.Revision, bumps = 0;
        while (!patch.Empty)
        {
            sim.Step();
            if (t.Grid.Patches.Revision == revision)
                continue;
            revision = t.Grid.Patches.Revision;
            bumps++;
            levels.Add(patch.Level);
        }
        Assert.Equal([1, 0, 0], levels);                      // The last bump is the patch running out.
        Assert.Equal(3, bumps);
    }

    [Fact]
    public void PatchAmountsAreHashedAndSnapshotted()
    {
        var content = TestContent.Load();
        var setup = GameSetup.Default;
        var sim = Simulation.NewGame(content, setup);
        var before = sim.StateHash();
        var g = sim.Tiles[sim.StartTileId].Grid;
        var (mined, emptied) = (g.Patches.All[0], g.Patches.All[1]);
        g.Patches.SetAmount(mined, mined.Amount - 7);
        Assert.NotEqual(before, sim.StateHash());
        g.Patches.SetAmount(emptied, 0);

        var restored = Snapshot.Capture(sim).Restore(content, setup, []);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var rg = restored.Tiles[sim.StartTileId].Grid;
        Assert.Equal(mined.Amount, rg.Patches.ById(mined.Id)!.Amount);
        Assert.True(rg.Patches.ById(emptied.Id)!.Empty);
        Assert.Equal(g.DepositCounts(), rg.DepositCounts());
    }
}

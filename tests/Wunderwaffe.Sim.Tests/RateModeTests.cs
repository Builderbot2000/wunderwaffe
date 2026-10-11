using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Rate mode (FB-012 to FB-014): accuracy against the full sim, mode switching, supply shortfalls.</summary>
public class RateModeTests(ITestOutputHelper output)
{
    const int East = 0, West = 2;
    const int Minute = 60 * Simulation.TicksPerSecond;

    static Simulation RateSim() => new(TestContent.Load()) { RateModeEnabled = true };

    static void View(Simulation sim, int player, TileState? tile)
    {
        sim.Enqueue(new SetViewedTileCommand(player, tile?.Id ?? -1));
        sim.Step();
    }

    /// <summary>
    /// Home (0,0): a copper line straight into the core, an iron drill feeding a smelter by belt, a smelter fed directly
    /// by a drill, and an import port belting the neighbour's ore to the core. East (1,0), owned too: two drills belting
    /// iron ore to an export port into home. Home's core is at (12..14, 12..14).
    /// </summary>
    static (Simulation Sim, TileState Home, TileState East) Factory()
    {
        var sim = RateSim();
        var home = Tile(sim, 0, 0);
        var east = Tile(sim, 1, 0);
        Place(sim, home, "core", 12, 12);

        // Copper drill -> belt -> core (row 13).
        Ore(home, "copper_ore", 2, 12);
        Place(sim, home, "drill", 2, 12);
        Belt(sim, home, 4, 13, 8);                       // (4..11, 13) into the core.

        // Iron drill -> belt -> smelter -> belt -> core (row 16, then up column 13).
        Ore(home, "iron_ore", 2, 16);
        Place(sim, home, "drill", 2, 16);
        Belt(sim, home, 4, 17, 3);                       // (4..6, 17) into the smelter.
        Place(sim, home, "smelter", 7, 16, recipe: "smelt_iron");
        Belt(sim, home, 9, 16, 4);                       // (9..12, 16) east, then north up (13, 16..15) into the core.
        Belt(sim, home, 13, 16, 2, 3);

        // A drill straight into a copper smelter, belted to the core.
        Ore(home, "copper_ore", 16, 20);
        Place(sim, home, "drill", 16, 20);
        Place(sim, home, "smelter", 16, 18, recipe: "smelt_copper");
        Belt(sim, home, 16, 17, 3, 3);                   // North up (16, 17..15), then west along (16..15, 14) into the core.
        Belt(sim, home, 16, 14, 2, West);

        // East: iron ore to an export port at (0, 10) facing home; home's import port at (29, 10) belts west to the core.
        Ore(east, "iron_ore", 10, 9);
        Place(sim, east, "drill", 10, 9);
        Ore(east, "iron_ore", 7, 11);
        Place(sim, east, "drill", 7, 11);                // Below the belt.
        Belt(sim, east, 9, 10, 9, West);                 // (9..1, 10) west.
        Place(sim, east, "export_port", 0, 10, West);
        Place(sim, home, "import_port", 29, 10, East);
        Belt(sim, home, 28, 10, 13, West);               // (28..16, 10) west...
        Belt(sim, home, 15, 10, 2, 1);                   // ...(15, 10..11) south...
        Place(sim, home, "conveyor", 15, 12, West);      // ...into the core's east side.
        sim.Step();
        return (sim, home, east);
    }

    static Dictionary<string, long> Stock(TileState t) => t.Inventory.Entries.ToDictionary(kv => kv.Key, kv => kv.Value);

    static Dictionary<string, long> Delta(Dictionary<string, long> before, Dictionary<string, long> after) =>
        after.Keys.Union(before.Keys).ToDictionary(k => k, k => after.GetValueOrDefault(k) - before.GetValueOrDefault(k));

    [Fact]
    public void AStableTileYieldsWhatTheFullSimDoesWithinFivePercent()
    {
        // FB-012: leaving a stable tile for 5 minutes yields the same net output (+-5%) as watching it.
        var (full, fullHome, fullEast) = Factory();
        var (rate, rateHome, rateEast) = Factory();
        foreach (var (sim, home, east) in new[] { (full, fullHome, fullEast), (rate, rateHome, rateEast) })
        {
            View(sim, 0, home);
            View(sim, 1, east);
            Run(sim, Minute); // Warm up to a steady state.
        }
        Assert.Equal(full.StateHash(), rate.StateHash());
        View(rate, 0, null);
        View(rate, 1, null);
        Run(full, 2);
        Assert.False(RateMode.IsFullSim(rate, rateHome));
        Assert.False(RateMode.IsFullSim(rate, rateEast));

        var fullBefore = Stock(fullHome);
        var rateBefore = Stock(rateHome);
        Run(full, 5 * Minute);
        Run(rate, 5 * Minute);
        var fullGain = Delta(fullBefore, Stock(fullHome));
        var rateGain = Delta(rateBefore, Stock(rateHome));

        foreach (var item in new[] { "copper_ore", "iron_ore", "iron_plate", "copper_plate" })
        {
            output.WriteLine($"{item}: full {fullGain.GetValueOrDefault(item)}, rate {rateGain.GetValueOrDefault(item)}");
            long f = fullGain.GetValueOrDefault(item), r = rateGain.GetValueOrDefault(item);
            Assert.True(f > 0, $"{item}: the full sim made none");
            Assert.InRange(r, f * 95 / 100, f * 105 / 100);
        }
    }

    [Fact]
    public void RatesComeFromTheGraph()
    {
        // OQ-14: a drill makes 1 per 60 ticks; a smelter needs 2 ore per plate; a belt carries 4/32 items a tick.
        var (sim, home, east) = Factory();
        var rates = RateMode.RatesOf(sim, home);
        long perDrill = RateModel.One / 60;
        Assert.InRange(rates.RateOf(FlowKind.Core, "copper_ore"), perDrill - 2, perDrill);
        Assert.InRange(rates.RateOf(FlowKind.Core, "iron_plate"), perDrill / 2 - 2, perDrill / 2);
        Assert.InRange(rates.RateOf(FlowKind.Core, "copper_plate"), perDrill / 2 - 2, perDrill / 2);
        Assert.InRange(rates.RateOf(FlowKind.Core, "iron_ore"), 2 * perDrill - 4, 2 * perDrill);
        Assert.Equal(rates.RateOf(FlowKind.Draw, "iron_ore"), rates.RateOf(FlowKind.Core, "iron_ore"));
        Assert.Equal(rates.RateOf(FlowKind.Core, "iron_ore"), RateMode.RatesOf(sim, east).Total(FlowKind.Export, "iron_ore"));

        // A belt is the bottleneck: sixteen drills on one belt deliver its 1/8 item per tick, not 16/60.
        var busy = RateSim();
        var t = Tile(busy, 0, 0);
        Place(busy, t, "core", 25, 0);
        Belt(busy, t, 0, 2, 25);                         // (0..24, 2) into the core's west side.
        for (int i = 0; i < 8; i++)
        {
            Ore(t, "iron_ore", i * 3, 0);
            Place(busy, t, "drill", i * 3, 0);
            Ore(t, "iron_ore", i * 3, 3);
            Place(busy, t, "drill", i * 3, 3);
        }
        busy.Step();
        Assert.InRange(RateMode.RatesOf(busy, t).RateOf(FlowKind.Core, "iron_ore"), RateModel.One / 8 - 16, RateModel.One / 8);
    }

    [Fact]
    public void ARateModeTileOnlyWakesWhenViewedOrContested()
    {
        // FB-013: full sim while a player views the tile, rate mode otherwise.
        var (sim, home, east) = Factory();
        Run(sim, 2);
        Assert.False(RateMode.IsFullSim(sim, home));
        View(sim, 0, home);
        Assert.True(RateMode.IsFullSim(sim, home));
        Assert.False(RateMode.IsFullSim(sim, east));
        View(sim, 0, east);
        Assert.False(RateMode.IsFullSim(sim, home));
        Assert.True(RateMode.IsFullSim(sim, east));

        // Off by default for hand-built sims, so the entity tests keep running every tile.
        var plain = NewSim();
        var t = Tile(plain, 0, 0);
        plain.Step();
        Assert.True(RateMode.IsFullSim(plain, t));
    }

    /// <summary>Copper and iron ore everywhere in the factory, counting plates (made from 2 ore) and batches in progress.</summary>
    static long OreEverywhere(Simulation sim)
    {
        long n = 0;
        long Weight(string item) => item.EndsWith("_plate") ? 2 : item.EndsWith("_ore") ? 1 : 0;
        foreach (var t in sim.Tiles)
        {
            n += t.Inventory.Entries.Sum(kv => Weight(kv.Key) * kv.Value);
            foreach (var b in t.Grid.Buildings)
            {
                if (b.Belt is { } belt)
                    n += belt.Items.Take(belt.Count).Sum(i => Weight(i!));
                if (b.Machine is { } m)
                {
                    n += m.Input.Sum(kv => Weight(kv.Key) * kv.Value) + m.Output.Sum(kv => Weight(kv.Key) * kv.Value);
                    if (m.Working && m.Recipe != null)
                        n += m.Recipe.Inputs.Sum(kv => Weight(kv.Key) * kv.Value);
                }
                if (b.Export is { } link)
                    n += link.InFlight.Sum(s => Weight(s.ItemId));
            }
        }
        return n;
    }

    static long OreMined(Simulation sim) => sim.Players.Sum(p => p.ProducedOf("copper_ore") + p.ProducedOf("iron_ore"));

    [Fact]
    public void SwitchingModesNeitherLosesNorDuplicatesItems()
    {
        // FB-013: no item loss or duplication across promotion and demotion. Every ore item ever mined is somewhere
        // (as ore, or half a plate), give or take the part-made item each rate flow carries.
        var (sim, home, east) = Factory();
        long start = OreEverywhere(sim) - OreMined(sim);
        int flows = 0;
        for (int i = 0; i < 40; i++)
        {
            View(sim, 0, i % 3 == 0 ? home : null);
            View(sim, 1, i % 4 == 1 ? east : null);
            Run(sim, 97 + i * 13);
            flows = Math.Max(flows, RateMode.RatesOf(sim, home).Flows.Count + RateMode.RatesOf(sim, east).Flows.Count);
            long drift = OreEverywhere(sim) - OreMined(sim) - start;
            Assert.True(Math.Abs(drift) <= 2 * flows, $"round {i}: {drift} ore unaccounted for");
        }
        Assert.True(OreMined(sim) > 1000);
    }

    [Fact]
    public void CuttingAnImportCutsWhatDependsOnItAndRestoringItRecovers()
    {
        // FB-014: when imports fall short, output scales down, and the shortfall propagates; restoring recovers it.
        var (sim, home, east) = Factory();
        Run(sim, Minute);
        Assert.True(RateMode.RatesOf(sim, home).RateOf(FlowKind.Core, "iron_ore") > 0);

        // East loses its drills: home's import dries up within a tick, the rest of home is untouched.
        sim.Enqueue(new RemoveBuildingCommand(Me, east.Id, 10, 9));
        sim.Enqueue(new RemoveBuildingCommand(Me, east.Id, 7, 11));
        sim.Step();
        var cut = RateMode.RatesOf(sim, home);
        Assert.Equal(0, cut.RateOf(FlowKind.Core, "iron_ore"));
        Assert.Equal(0, cut.RateOf(FlowKind.Draw, "iron_ore"));
        Assert.True(cut.RateOf(FlowKind.Core, "copper_ore") > 0);

        // What was already in transit still arrives (nothing is lost), then the flow stops.
        Run(sim, Minute);
        long before = Items(home, "iron_ore");
        Run(sim, Minute);
        Assert.Equal(before, Items(home, "iron_ore"));

        // Rebuilding the drills brings it back.
        Place(sim, east, "drill", 10, 9);
        Place(sim, east, "drill", 7, 11);
        sim.Step();
        Run(sim, Minute);
        Assert.True(Items(home, "iron_ore") > before);
        Assert.True(RateMode.RatesOf(sim, home).RateOf(FlowKind.Core, "iron_ore") > 0);
    }

    [Fact]
    public void AShortfallPropagatesAlongAChainOfTiles()
    {
        // FB-014: C mines and ships to B, B passes it on to A. Cutting C's drill stops A's income, though B and A are
        // untouched.
        var sim = RateSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        var c = Tile(sim, 2, 0);
        Place(sim, a, "core", 10, 9);
        Place(sim, a, "import_port", 29, 10, East);
        Belt(sim, a, 28, 10, 16, West);                  // (28..13, 10) into the core's east side at x=12.
        Place(sim, b, "import_port", 29, 10, East);
        Belt(sim, b, 28, 10, 28, West);                  // (28..1, 10)...
        Place(sim, b, "export_port", 0, 10, West);       // ...into the export port to A.
        Ore(c, "iron_ore", 10, 9);
        Place(sim, c, "drill", 10, 9);
        Belt(sim, c, 9, 10, 9, West);
        Place(sim, c, "export_port", 0, 10, West);
        sim.Step();

        Assert.True(RateMode.RatesOf(sim, a).RateOf(FlowKind.Core, "iron_ore") > 0);
        Run(sim, 2 * Minute);
        Assert.True(Items(a, "iron_ore") > 0);

        sim.Enqueue(new RemoveBuildingCommand(Me, c.Id, 10, 9));
        sim.Step();
        Assert.Equal(0, RateMode.RatesOf(sim, a).RateOf(FlowKind.Core, "iron_ore"));
        Assert.Equal(0, RateMode.RatesOf(sim, b).Total(FlowKind.Export, "iron_ore"));
    }

    [Fact]
    public void ImportsFeedAMachineOnlyAsFastAsTheyArrive()
    {
        // FB-014: home smelts imported ore. A smelter takes 1/60 ore a tick, which one drill in the next tile supplies,
        // so it makes a plate every 120 ticks and no ore reaches the core.
        var sim = RateSim();
        var home = Tile(sim, 0, 0);
        var east = Tile(sim, 1, 0);
        Place(sim, home, "core", 10, 9);
        Place(sim, home, "import_port", 29, 10, East);
        Belt(sim, home, 28, 10, 13, West);               // (28..16, 10) into the smelter at (14..15, 10..11).
        Place(sim, home, "smelter", 14, 10, recipe: "smelt_iron");
        Place(sim, home, "conveyor", 13, 10, West);      // Out of the smelter into the core at (10..12).
        Ore(east, "iron_ore", 10, 9);
        Place(sim, east, "drill", 10, 9);
        Belt(sim, east, 9, 10, 9, West);
        Place(sim, east, "export_port", 0, 10, West);
        sim.Step();
        var rates = RateMode.RatesOf(sim, home);
        Assert.InRange(rates.RateOf(FlowKind.Core, "iron_plate"), RateModel.One / 120 - 2, RateModel.One / 120);
        Assert.Equal(0, rates.RateOf(FlowKind.Core, "iron_ore"));
    }
}

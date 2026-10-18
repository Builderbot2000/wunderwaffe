using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>FB-020 (OQ-42): the cable layer, power nodes, and power carried across tiles by linked port pairs.</summary>
public class PowerLayerTests
{
    const string Iron = "iron_ore";
    const int East = 0, Minute = 60 * Simulation.TicksPerSecond;

    static long Made(TileState tile) => tile.Stats.MadeTotal(Iron);

    static void Remove(Simulation sim, TileState tile, int x, int y)
    {
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, x, y));
        sim.Step();
    }

    [Fact]
    public void CablesLieUnderBuildingsAndStillJoinWhatTheyTouch()
    {
        var sim = PoweredSim();
        var tile = Tile(sim);
        var grid = tile.Grid;
        Harness.Ore(tile, Iron, 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Place(sim, tile, "core_large", 20, 10);
        sim.Step();
        // A cable under the drill, a run to the core, then a container and a belt laid over the run.
        Place(sim, tile, "wire", 10, 10);
        for (int x = 12; x < 20; x++)
            Place(sim, tile, "wire", x, 10);
        Place(sim, tile, "container", 14, 10);
        Place(sim, tile, "conveyor", 17, 10);
        Run(sim, 61);

        Assert.Equal(9, grid.CountOn(BuildingKind.Wire));
        Assert.Null(grid.BuildingAt(12, 10));
        Assert.Equal("container", grid.BuildingAt(14, 10)!.DefId);
        Assert.Equal(PlaceResult.Blocked, Placement.Check(sim, tile, Me, "wire", 12, 10, 0, null, out _));
        Assert.True(Made(tile) >= 1);
        var drill = grid.BuildingAt(10, 10)!;
        var net = grid.Power.NetworkOf(drill)!;
        Assert.Same(net, grid.Power.NetworkOf(grid.BuildingAt(20, 10)!));
        Assert.Equal(2, net.Members.Count); // Cables are a layer, not members; the container and belt draw nothing.

        // Removing on the belt's cell takes the belt; again, the cable under it, which cuts the drill off.
        Remove(sim, tile, 17, 10);
        Assert.Null(grid.BuildingAt(17, 10));
        Assert.Equal(9, grid.CountOn(BuildingKind.Wire));
        Assert.Same(grid.Power.NetworkOf(drill), grid.Power.NetworkOf(grid.BuildingAt(20, 10)!));
        Remove(sim, tile, 17, 10);
        Assert.Equal(8, grid.CountOn(BuildingKind.Wire));
        Assert.NotSame(grid.Power.NetworkOf(drill), grid.Power.NetworkOf(grid.BuildingAt(20, 10)!));
        Run(sim, 120);
        Assert.Equal(MachineStatus.NoPower, drill.Machine!.Status);
    }

    [Fact]
    public void ANodeJoinsGeneratorsBatteriesAndNodesInItsRadiusButNotConsumers()
    {
        var sim = PoweredSim();
        var tile = Tile(sim);
        var grid = tile.Grid;
        Place(sim, tile, "core_large", 0, 0);   // (0..2, 0..2).
        Place(sim, tile, "power_node", 8, 1);   // 6 cells from the core.
        Place(sim, tile, "power_node", 16, 1);  // 8 from the first node: just in reach.
        Place(sim, tile, "battery", 22, 1);     // 6 from the second.
        Harness.Ore(tile, Iron, 18, 4);
        Place(sim, tile, "drill", 18, 4);       // In the second node's radius, but a consumer.
        Harness.Ore(tile, Iron, 23, 0);
        Place(sim, tile, "drill", 23, 0);       // Touches the battery.
        Run(sim, 61);

        var core = grid.BuildingAt(0, 0)!;
        var net = grid.Power.NetworkOf(core)!;
        Assert.Equal(["core_large", "power_node", "power_node", "battery", "drill"], net.Members.Select(m => m.DefId));
        Assert.Same(grid.BuildingAt(23, 0), net.Members[^1]);
        Assert.NotSame(net, grid.Power.NetworkOf(grid.BuildingAt(18, 4)!));
        Assert.Equal(MachineStatus.NoPower, grid.BuildingAt(18, 4)!.Machine!.Status);
        Assert.Equal(1, Made(tile));
        Assert.Equal(["core_large", "power_node"], TilePower.InRadius(grid, grid.BuildingAt(8, 1)!).Select(b => b.DefId));

        // Removing the first node splits the network again: the battery side runs on what it stored.
        Remove(sim, tile, 8, 1);
        var battery = grid.BuildingAt(22, 1)!;
        Assert.NotSame(grid.Power.NetworkOf(core), grid.Power.NetworkOf(battery));
        Assert.Equal(["power_node", "battery", "drill"], grid.Power.NetworkOf(battery)!.Members.Select(m => m.DefId));
    }

    /// <summary>
    /// Tile A (0,0): a large core touching a shield generator (40, always on) and an export port in B's zone. Tile B
    /// (1,0), no power of its own: an import port in A's zone, a cable from it along four drills (2 each) that belt their
    /// ore into B's bank core. One network over both tiles: 20 supply against 48 demand.
    /// </summary>
    static (TileState A, TileState B) Linked(Simulation sim)
    {
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        Place(sim, a, "core_large", 26, 9);         // (26..28, 9..11).
        Place(sim, a, "shield_generator", 24, 9);   // West of it.
        Place(sim, a, "export_port", 29, 10, East);

        Place(sim, b, "import_port", 0, 10, East);
        for (int y = 10; y <= 26; y++)
            Place(sim, b, "wire", 1, y);
        for (int x = 2; x <= 12; x++)
            Place(sim, b, "wire", x, 26);
        for (int x = 4; x <= 10; x += 2)
        {
            Harness.Ore(b, Iron, x, 27);
            Place(sim, b, "drill", x, 27);
        }
        Belt(sim, b, 4, 29, 25);                    // (4..28, 29) into the bank at (29, 29).
        return (a, b);
    }

    [Fact]
    public void ALinkedPortPairCarriesPowerAcrossTilesAndBothTilesShowTheNetwork()
    {
        var sim = PoweredSim();
        var (a, b) = Linked(sim);
        Run(sim, 2 * Minute);

        var drill = b.Grid.BuildingAt(4, 27)!;
        var net = b.Grid.Power.NetworkOf(drill)!;
        Assert.Same(net, a.Grid.Power.NetworkOf(a.Grid.BuildingAt(26, 9)!));
        Assert.Contains(net, PowerNet.Networks(a));
        Assert.Contains(net, PowerNet.Networks(b));
        Assert.Equal([a.Id, b.Id], net.TileIds);
        Assert.Equal(20, net.Supply);
        Assert.Equal(48, net.Demand);
        Assert.Equal(20 * 1000 / 48, drill.Machine!.PowerPermille);
        Assert.True(Made(b) > 0);

        var query = new GameQuery(sim, Me);
        var view = query.PowerNetworks(b.Id).Single(v => v.Id == net.Id);
        Assert.Equal([a.Id, b.Id], view.Tiles);
        Assert.Equal(4, view.Members.Count); // The drills: members on the tile asked about.

        // Without the export port the link is gone and B has no power.
        Remove(sim, a, 29, 10);
        Run(sim, 120);
        Assert.Equal(MachineStatus.NoPower, drill.Machine!.Status);
        Assert.DoesNotContain(PowerNet.Networks(a), n => n.TileIds.Contains(b.Id));
    }

    [Fact]
    public void RateModeUsesTheJoinedNetworksSatisfaction()
    {
        // Built twice: one keeps B watched (full sim; A, unwatched, still counts through the live figures), the other
        // leaves both tiles in rate mode, where B's drills slow by the satisfaction of the network both tiles share.
        Simulation full = new(TestContent.Load()) { RateModeEnabled = true }, rate = new(TestContent.Load()) { RateModeEnabled = true };
        var (_, fullB) = Linked(full);
        var (_, rateB) = Linked(rate);
        foreach (var (sim, b) in new[] { (full, fullB), (rate, rateB) })
        {
            sim.Step();
            sim.Enqueue(new SetViewedTileCommand(Me, b.Id));
            Run(sim, Minute);
        }
        rate.Enqueue(new SetViewedTileCommand(Me, -1));
        Run(rate, 1);
        Run(full, 1);
        Assert.False(RateMode.IsFullSim(rate, rateB));

        long f0 = Made(fullB), r0 = Made(rateB);
        Run(full, 5 * Minute);
        Run(rate, 5 * Minute);
        long f = Made(fullB) - f0, r = Made(rateB) - r0;
        // Four drills at 20/48 of full speed: one ore per 60 ticks each at full power.
        Assert.InRange(f, 4 * 300 * 20 / 48 * 95 / 100, 4 * 300 * 20 / 48 * 105 / 100);
        Assert.InRange(r, f * 95 / 100, f * 105 / 100);
    }
}

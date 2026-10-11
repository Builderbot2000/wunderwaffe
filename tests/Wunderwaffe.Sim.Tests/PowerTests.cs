using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Power networks (FB-005) on the real content: draws, core power, wires, generators and batteries.</summary>
public class PowerTests
{
    const string Iron = "iron_ore", Copper = "copper_ore";

    static long Made(TileState tile, string item) => tile.Stats.TotalMade.GetValueOrDefault(item);

    [Fact]
    public void CorePowerRunsADrillTouchingIt()
    {
        var sim = PoweredSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Place(sim, tile, "core", 12, 10);
        Run(sim, 61);

        Assert.Equal(1, Made(tile, Iron));
        var net = PowerNet.Networks(tile).Single();
        Assert.Equal(20, net.Supply);
        Assert.Equal(2, net.Demand);
        Assert.Equal(1000, net.Satisfaction);
    }

    [Fact]
    public void ADrillWithNoPowerDoesNothing()
    {
        var sim = PoweredSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Place(sim, tile, "core", 20, 10); // Not touching.
        Run(sim, 600);

        Assert.Equal(0, Made(tile, Iron));
        var drill = tile.Grid.BuildingAt(10, 10)!;
        Assert.Equal(MachineStatus.NoPower, drill.Machine!.Status);
        Assert.False(PowerNet.IsPowered(tile, drill));
    }

    [Fact]
    public void WireCarriesCorePowerToADistantDrill()
    {
        var sim = PoweredSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Place(sim, tile, "core", 20, 10);
        for (int x = 12; x < 20; x++)
            Place(sim, tile, "wire", x, 10);
        Run(sim, 61);

        Assert.Equal(1, Made(tile, Iron));
        Assert.Single(PowerNet.Networks(tile));
        Assert.Equal(10, PowerNet.Networks(tile)[0].Members.Count);
    }

    [Fact]
    public void AShortNetworkSlowsEveryConsumerBySameShare()
    {
        // Twenty touching drills (40 power) on one core (20): each runs at half speed.
        var sim = PoweredSim();
        var tile = Tile(sim, size: 44);
        Place(sim, tile, "core", 0, 1);
        for (int i = 0; i < 20; i++)
        {
            Harness.Ore(tile, Iron, i * 2, 4);
            Place(sim, tile, "drill", i * 2, 4);
        }
        Run(sim, 601);

        var net = PowerNet.Networks(tile).Single();
        Assert.Equal(40, net.Demand);
        Assert.Equal(500, net.Satisfaction);
        Assert.InRange(Made(tile, Iron), 95, 100); // 20 drills x 600 ticks / 120 ticks a batch.
    }

    [Fact]
    public void AGeneratorBurnsFuelFromAPressStartedOnCorePower()
    {
        // Core -> fuel press (touching) -> generator (touching); a copper drill feeds the press.
        var sim = PoweredSim();
        var tile = Tile(sim);
        Place(sim, tile, "core", 0, 0);
        Harness.Ore(tile, Copper, 3, 2);
        Place(sim, tile, "drill", 3, 2);
        Place(sim, tile, "fuel_press", 3, 0, recipe: "press_fuel");
        Place(sim, tile, "fuel_generator", 5, 0);
        Run(sim, 2);
        var gen = tile.Grid.BuildingAt(5, 0)!;

        bool burning = false;
        for (int i = 0; i < 60 * Simulation.TicksPerSecond && !burning; i++)
        {
            sim.Step();
            burning = PowerNet.Networks(tile).Single().Supply == 20 + 40;
        }
        Assert.True(burning, "the generator never burned");
        Assert.True(gen.Machine!.Working);
        Assert.True(tile.Stats.TotalUsed.GetValueOrDefault("fuel") >= 1);
    }

    [Fact]
    public void ABatteryStoresSurplusAndCarriesTheNetworkWhenCutOff()
    {
        // Core - wire - battery - drill, the drill emptying into a container.
        var sim = PoweredSim();
        var tile = Tile(sim);
        Place(sim, tile, "core", 0, 0);
        Place(sim, tile, "wire", 3, 0);
        Place(sim, tile, "battery", 4, 0);
        Harness.Ore(tile, Iron, 5, 0);
        Place(sim, tile, "drill", 5, 0);
        Place(sim, tile, "container", 7, 0);
        Run(sim, 601);

        var battery = tile.Grid.BuildingAt(4, 0)!.Battery!;
        Assert.InRange(battery.Charge, 17 * 600, 18 * 600 + 40); // 20 supply - 2 draw, a tick.

        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 3, 0));
        Run(sim, 2);
        long charge = battery.Charge;
        long made = Made(tile, Iron);
        Run(sim, 600);

        Assert.Equal(10, Made(tile, Iron) - made);
        Assert.InRange(charge - battery.Charge, 2 * 590, 2 * 600);
        var net = PowerNet.Networks(tile).Single(n => n.Members.Any(m => m.Battery != null));
        Assert.Equal(0, net.Supply);
        Assert.Equal(1000, net.Satisfaction);
    }

    [Fact]
    public void UnitPartsAreInertOnATileAndOnboardSupplyIsGeneratorPlusBattery()
    {
        var content = TestContent.Load();
        Assert.False(content.Buildings["thruster"].IsPower);
        Assert.False(content.Buildings["unit_core"].IsPower);
        Assert.Equal(10, PowerNet.OnboardSupply(content.Buildings["unit_core"]));
        Assert.Equal(content.Buildings["battery"].Discharge, PowerNet.OnboardSupply(content.Buildings["battery"]));
        Assert.Equal(40, PowerNet.OnboardSupply(content.Buildings["fuel_generator"]));
    }
}

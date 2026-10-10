using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

public class PortTests
{
    const string Iron = "iron_ore";

    // Rotations: 0 faces east, 1 south, 2 west, 3 north.
    const int East = 0, South = 1, West = 2, North = 3;

    static PlaceResult Check(Simulation sim, TileState tile, string building, int x, int y, int rotation) =>
        Placement.Check(sim, tile, Me, building, x, y, rotation, null, out _);

    /// <summary>
    /// Tile A (0,0) mines iron, belts it east to an export port on its east edge (lane 10). Tile B (1,0) has an
    /// import port on its west edge in <paramref name="importLane"/> feeding a belt to a core.
    /// </summary>
    static (Simulation Sim, TileState A, TileState B) Pair(int importLane = 10, bool withReceivingBelt = true)
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);

        Harness.Ore(a, Iron, 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);                    // (22..28, 10)
        Place(sim, a, "export_port", 29, 10, East);

        Place(sim, b, "import_port", 0, importLane, West);
        if (withReceivingBelt)
        {
            Belt(sim, b, 1, importLane, 8);         // (1..8, lane)
            Place(sim, b, "core", 9, importLane - 1); // Core rows importLane-1 .. importLane+1.
        }
        return (sim, a, b);
    }

    [Fact]
    public void PortsOnlyFitInTheZoneOfTheEdgeTheyFace()
    {
        var sim = NewSim();
        var tile = Tile(sim);

        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "export_port", 29, 10, East));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "export_port", 27, 10, East));  // Third row in.
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "export_port", 26, 10, East));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "import_port", 0, 5, West));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "import_port", 3, 5, West));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "export_port", 5, 2, North));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "export_port", 5, 3, North));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "export_port", 5, 29, South));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "export_port", 5, 26, South));
    }

    [Fact]
    public void AZoneCellFacesTheEdgeItChoosesNotJustTheNearest()
    {
        var sim = NewSim();
        var tile = Tile(sim);

        // Near the top-right corner a port can face either edge, but not one it's far from.
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "export_port", 28, 1, North));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "export_port", 28, 1, East));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "export_port", 28, 1, West));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "export_port", 28, 1, South));
        // Mid-edge a port can't face a different edge from the one it sits on.
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, tile, "export_port", 29, 10, North));
    }

    [Fact]
    public void ItemsCrossToTheNeighbourAfterTheTransitDelay()
    {
        var (sim, a, b) = Pair();
        long start = Items(b, Iron);

        // Drill output at tick 59, 7 belt cells, then transit, then 8 belt cells on the far side.
        int earliest = 59 + 7 * 32 + PortRules.TransitTicksPerTile + 8 * 32;
        Run(sim, earliest - 5);
        Assert.Equal(start, Items(b, Iron));

        Run(sim, 5 + 100);
        Assert.True(Items(b, Iron) > start, "first item should have arrived");
        Run(sim, 60 * Simulation.TicksPerSecond);
        Assert.True(Items(b, Iron) - start >= 55);
    }

    [Fact]
    public void ADrillsWorthOfItemsAllArriveNothingLostNothingMade()
    {
        var (sim, a, b) = Pair();
        Run(sim, 4000);

        var drill = a.Grid.BuildingAt(20, 10)!.Machine!;
        long inSystem = a.Grid.Belts.Sum(x => x.Belt!.Count) + b.Grid.Belts.Sum(x => x.Belt!.Count)
            + a.Grid.ExportPorts.Sum(x => x.Export!.InFlight.Count) + drill.OutputCount
            + b.Grid.ImportPorts.Sum(x => x.Machine!.OutputCount);
        long produced = 4000 / 60; // One a second; the first lands on tick 59.
        long delivered = Items(b, Iron) - 100_000;
        Assert.Equal(produced, delivered + inSystem);
    }

    [Fact]
    public void ALaneWithNoImportPortStopsTheExportAndBacksTheBeltUp()
    {
        var (sim, a, b) = Pair(importLane: 14);
        Run(sim, 3000);

        Assert.Empty(a.Grid.BuildingAt(29, 10)!.Export!.InFlight);
        Assert.Equal(100_000, Items(b, Iron));
        Assert.Equal(BeltState.Capacity, a.Grid.BuildingAt(28, 10)!.Belt!.Count);
    }

    [Fact]
    public void ExportingToATileYouDontOwnDoesNothing()
    {
        var (sim, a, b) = Pair();
        b.Owner = Them;
        Run(sim, 2000);

        Assert.Empty(a.Grid.BuildingAt(29, 10)!.Export!.InFlight);
        Assert.Equal(100_000, Items(b, Iron));
    }

    [Fact]
    public void ExportingOffTheEdgeOfTheWorldDoesNothing()
    {
        var sim = NewSim();
        var a = Tile(sim);
        Harness.Ore(a, Iron, 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);
        Place(sim, a, "export_port", 29, 10, East);
        Run(sim, 1500);

        Assert.Empty(a.Grid.BuildingAt(29, 10)!.Export!.InFlight);
        Assert.Equal(BeltState.Capacity, a.Grid.BuildingAt(28, 10)!.Belt!.Count);
    }

    [Fact]
    public void ABlockedImportPortBacksTheWholeLineUp()
    {
        // No belt takes items from the import port, so it fills and the link and belts behind it fill in turn.
        var (sim, a, b) = Pair(withReceivingBelt: false);
        Run(sim, 8000);

        var import = b.Grid.BuildingAt(0, 10)!.Machine!;
        var link = a.Grid.BuildingAt(29, 10)!.Export!;
        Assert.Equal(MachineState.OutputCapacity, import.OutputCount);
        Assert.InRange(link.InFlight.Count, 1, ExportState.LinkCapacity);
        Assert.Equal(BeltState.Capacity, a.Grid.BuildingAt(28, 10)!.Belt!.Count);
        Assert.Equal(100_000, Items(b, Iron));
    }

    [Fact]
    public void ImportPortsInOneLaneShareDeliveriesInTurn()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        Harness.Ore(a, Iron, 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);
        Place(sim, a, "export_port", 29, 10, East);
        Place(sim, b, "import_port", 0, 10, West);
        Place(sim, b, "import_port", 1, 10, West); // Same lane, one cell deeper; neither has an outlet.
        Run(sim, 59 + 7 * 32 + PortRules.TransitTicksPerTile + 9 * 60);

        int first = b.Grid.BuildingAt(0, 10)!.Machine!.OutputCount;
        int second = b.Grid.BuildingAt(1, 10)!.Machine!.OutputCount;
        Assert.True(first + second >= 6);
        Assert.InRange(Math.Abs(first - second), 0, 1);
    }

    [Fact]
    public void RemovingAnExportPortReturnsItemsStillInFlight()
    {
        var (sim, a, _) = Pair();
        Run(sim, 59 + 7 * 32 + 200);
        var link = a.Grid.BuildingAt(29, 10)!.Export!;
        Assert.True(link.InFlight.Count > 0);

        long before = Items(a, Iron);
        int flying = link.InFlight.Count;
        sim.Enqueue(new RemoveBuildingCommand(Me, a.Id, 29, 10));
        sim.Step();
        Assert.Equal(before + flying, Items(a, Iron));
    }

    [Fact]
    public void LinksAreDeterministic()
    {
        ulong Run1()
        {
            var (sim, _, _) = Pair();
            Run(sim, 1500);
            return sim.StateHash();
        }
        Assert.Equal(Run1(), Run1());
    }
}

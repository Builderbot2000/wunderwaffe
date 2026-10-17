using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

public class PortTests
{
    const string Iron = "iron_ore";

    // Rotations: 0 faces east, 1 south, 2 west, 3 north. A port's rotation only matters to the belts around it.
    const int East = 0, South = 1, West = 2, North = 3;

    static PlaceResult Check(Simulation sim, TileState tile, string building, int x, int y, int rotation) =>
        Placement.Check(sim, tile, Me, building, x, y, rotation, null, out _);

    /// <summary>
    /// Tile A (0,0) mines iron and belts it east to an export port in its zone for B, at (29, 10). Tile B (1,0) has an
    /// import port in its zone for A at (0, <paramref name="importRow"/>), feeding a belt to a core.
    /// </summary>
    static (Simulation Sim, TileState A, TileState B) Pair(int importRow = 10, bool withReceivingBelt = true)
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);

        Harness.Ore(a, Iron, 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);                    // (22..28, 10)
        Place(sim, a, "export_port", 29, 10, East);

        Place(sim, b, "import_port", 0, importRow, West);
        if (withReceivingBelt)
        {
            Belt(sim, b, 1, importRow, 8);          // (1..8, row)
            Place(sim, b, "core_large", 9, importRow - 1); // Core rows importRow-1 .. importRow+1.
        }
        return (sim, a, b);
    }

    [Fact]
    public void PortsOnlyFitInAZoneAndTheZonePicksTheNeighbour()
    {
        // A (0,0) has B (1,0) to the east and C (0,1) to the south: one zone along each of those sides.
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        var c = Tile(sim, 0, 1);

        Assert.Equal(b.Id, a.Grid.ZoneAt(29, 10));
        Assert.Equal(b.Id, a.Grid.ZoneAt(29 - PortRules.ZoneDepth + 1, 10));
        Assert.Equal(-1, a.Grid.ZoneAt(29 - PortRules.ZoneDepth, 10));
        Assert.Equal(c.Id, a.Grid.ZoneAt(10, 29));
        Assert.Equal(-1, a.Grid.ZoneAt(0, 10));  // The west and north sides are the edge of the world.
        Assert.Equal(-1, a.Grid.ZoneAt(10, 0));

        Assert.Equal(PlaceResult.Ok, Check(sim, a, "export_port", 29, 10, East));
        Assert.Equal(PlaceResult.Ok, Check(sim, a, "export_port", 26, 10, North)); // Any rotation: the zone decides.
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, a, "export_port", 25, 10, East));
        Assert.Equal(PlaceResult.Ok, Check(sim, a, "import_port", 10, 29, South));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, a, "import_port", 0, 10, West));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, a, "export_port", 10, 0, North));
    }

    [Fact]
    public void ZonesNeverOverlapAtACorner()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        var c = Tile(sim, 0, 1);

        // Each cell belongs to the neighbour whose border is closer; equally close goes to the lower id.
        Assert.Equal(b.Id, a.Grid.ZoneAt(29, 29));
        Assert.Equal(b.Id, a.Grid.ZoneAt(28, 28));
        Assert.Equal(c.Id, a.Grid.ZoneAt(27, 29));
        Assert.Equal(b.Id, a.Grid.ZoneAt(29, 27));
        Assert.True(b.Id < c.Id);
    }

    [Fact]
    public void ATileWithNoNeighboursHasNowhereForPorts()
    {
        var sim = NewSim();
        var lone = Tile(sim);
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, lone, "export_port", 29, 10, East));
        Assert.Equal(PlaceResult.NotInPortZone, Check(sim, lone, "import_port", 0, 10, West));
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
    public void AnImportAnywhereInThePairedZoneReceives()
    {
        // Rows don't have to line up: the zones are paired, not lanes.
        var (sim, _, b) = Pair(importRow: 22);
        long start = Items(b, Iron);
        Run(sim, 45 * Simulation.TicksPerSecond);
        Assert.True(Items(b, Iron) - start >= 20);
    }

    [Fact]
    public void ItemsFlowBothWaysBetweenPairedZones()
    {
        var (sim, a, b) = Pair();
        // And back: B mines iron and exports it west from its zone for A; A imports it in its zone for B.
        Harness.Ore(b, Iron, 5, 15);
        Place(sim, b, "drill", 5, 15);
        Belt(sim, b, 4, 15, 3, West);               // (4..2, 15)
        Place(sim, b, "export_port", 1, 15, West);
        Place(sim, a, "import_port", 28, 15, East);
        Belt(sim, a, 27, 15, 3, West);              // (27..25, 15)
        Place(sim, a, "core_large", 22, 14);              // Covers (22..24, 14..16).
        long aStart = Items(a, Iron), bStart = Items(b, Iron);

        Run(sim, 40 * Simulation.TicksPerSecond);
        Assert.True(Items(a, Iron) - aStart >= 20, "B to A");
        Assert.True(Items(b, Iron) - bStart >= 20, "A to B");
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
    public void AnImportFacingAnotherTileDoesNotReceiveAndTheBeltBacksUp()
    {
        // B's only import port is in its zone for D (1,1), not for A, so A's export has nowhere to send.
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        var d = Tile(sim, 1, 1);
        Harness.Ore(a, Iron, 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);
        Place(sim, a, "export_port", 29, 10, East);
        Place(sim, b, "import_port", 10, 28, South);
        Run(sim, 3000);

        Assert.Equal(d.Id, b.Grid.ZoneAt(10, 28));
        Assert.Empty(a.Grid.BuildingAt(29, 10)!.Export!.InFlight);
        Assert.Equal(0, b.Grid.BuildingAt(10, 28)!.Machine!.OutputCount);
        Assert.Equal(BeltState.Capacity, a.Grid.BuildingAt(28, 10)!.Belt!.Count);
    }

    [Fact]
    public void ExportingToATileYouDontOwnDoesNothing()
    {
        var (sim, a, b) = Pair();
        b.Owner = Them;
        // Short of the capture time: my core on B stands in its circle and would claim it (WM-006).
        Run(sim, Expansion.CaptureTicks(sim) - 60);

        Assert.Empty(a.Grid.BuildingAt(29, 10)!.Export!.InFlight);
        Assert.Equal(100_000, Items(b, Iron));
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
    public void ImportPortsInTheZoneShareDeliveriesInTurn()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        Harness.Ore(a, Iron, 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);
        Place(sim, a, "export_port", 29, 10, East);
        Place(sim, b, "import_port", 0, 10, West);
        Place(sim, b, "import_port", 2, 20, West); // Same zone, elsewhere in it; neither has an outlet.
        Run(sim, 59 + 7 * 32 + PortRules.TransitTicksPerTile + 9 * 60);

        int first = b.Grid.BuildingAt(0, 10)!.Machine!.OutputCount;
        int second = b.Grid.BuildingAt(2, 20)!.Machine!.OutputCount;
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

    [Fact]
    public void ALinkReportsItsItemsPerMinute()
    {
        // IO-002, IO-005: one drill's worth (60 a minute) crosses from A to B.
        var (sim, a, b) = Pair();
        Run(sim, 2 * 60 * Simulation.TicksPerSecond);
        Assert.InRange(a.Stats.SentPerMinute(b.Id), 59, 61);
        Assert.Equal([b.Id], a.Stats.Partners);
        Assert.Equal(0, b.Stats.SentPerMinute(a.Id));
    }
}

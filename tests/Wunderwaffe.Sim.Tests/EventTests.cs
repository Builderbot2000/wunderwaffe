using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>The UI-003 events other silos raise, and the MP-005 match settings they read.</summary>
public class EventTests
{
    static List<GameEvent> Of(Simulation sim, GameEventKind kind) => sim.Events.Where(e => e.Kind == kind).ToList();

    [Fact]
    public void ClaimingATileRaisesClaimedForTheTakerAndLostForTheOwner()
    {
        var sim = NewSim();
        Tile(sim, 0, 0);
        var east = sim.AddTile(1, 0, Them, 30);
        // Planted with the tile lent to Me, as TerritoryTests does: an enemy circle can't be built in while held.
        east.Owner = Me;
        Fund(sim, east, sim.Content.Buildings["conveyor"].Cost);
        sim.Enqueue(new PlaceBuildingCommand(Me, east.Id, "conveyor", east.CircleX, east.CircleY));
        sim.Step();
        east.Owner = Them;

        Run(sim, Expansion.CaptureTicks(sim) + 1);
        Assert.Equal(Me, east.Owner);
        var claimed = Assert.Single(Of(sim, GameEventKind.TileClaimed));
        Assert.Equal((Me, east.Id), (claimed.Player, claimed.TileId));
        var lost = Assert.Single(Of(sim, GameEventKind.TileLost));
        Assert.Equal((Them, east.Id), (lost.Player, lost.TileId));
    }

    [Fact]
    public void LiftingOffRaisesUnitCompleted()
    {
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(3, 3, 1));
        var home = sim.Tiles[sim.StartTileId];
        var core = home.Grid.Cores.Single();
        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, core.X, core.Y));
        sim.Step();
        var e = Assert.Single(Of(sim, GameEventKind.UnitCompleted));
        Assert.Equal((Me, home.Id, Assert.Single(Lifted(sim)).Id), (e.Player, e.TileId, e.UnitId));
    }

    [Fact]
    public void DamageRaisesUnderAttackOncePerTileAndVictimInAWhile()
    {
        var sim = NewSim();
        var home = Tile(sim);
        Place(sim, home, "wall", 2, 2);
        Place(sim, home, "wall", 4, 2);
        sim.Step();
        for (int i = 0; i < 3; i++)
            sim.Enqueue(new DebugDamageCommand(Me, home.Id, 2 + 2 * (i % 2), 2, 10));
        Run(sim, 2);
        var e = Assert.Single(Of(sim, GameEventKind.TileUnderAttack));
        Assert.Equal((Me, home.Id, -1), (e.Player, e.TileId, e.UnitId));
    }

    [Fact]
    public void CaptureTimeAndRichnessComeFromTheMatchSettings()
    {
        var content = TestContent.Load();
        var quick = Simulation.NewGame(content, new GameSetup(3, 3, 5, CaptureSeconds: 5));
        Assert.Equal(5 * Simulation.TicksPerSecond, Expansion.CaptureTicks(quick));
        Assert.Equal(Balance.CaptureSeconds * Simulation.TicksPerSecond, Expansion.CaptureTicks(NewSim()));

        static int Deposits(Simulation sim) => sim.Tiles.Sum(t => t.Grid.DepositCounts().Values.Sum());
        var rich = Simulation.NewGame(content, new GameSetup(3, 3, 5, RichnessPercent: 300));
        var poor = Simulation.NewGame(content, new GameSetup(3, 3, 5, RichnessPercent: 50));
        Assert.True(Deposits(rich) > Deposits(quick) && Deposits(quick) > Deposits(poor),
            $"rich {Deposits(rich)}, normal {Deposits(quick)}, poor {Deposits(poor)}");
    }
}

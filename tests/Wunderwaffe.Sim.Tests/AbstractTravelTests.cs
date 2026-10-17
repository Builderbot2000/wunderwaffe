using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>UC-009: units on rate-mode tiles travel as abstract nodes and materialise where they are.</summary>
public class AbstractTravelTests
{
    const int S = Unit.CellScale;

    /// <summary>Three tiles in a row; a scout on the first, ordered to the far side of the third.</summary>
    static (Simulation Sim, Unit Unit, TileState Far) Trip(bool rateMode)
    {
        var sim = NewSim();
        sim.RateModeEnabled = rateMode;
        var a = Tile(sim, 0, 0);
        Tile(sim, 1, 0);
        var far = Tile(sim, 2, 0);
        var u = Scout(sim, a, 4, 15);
        sim.Enqueue(new MoveUnitsCommand(Me, [u.Id], far.Id, 25, 15));
        sim.Step();
        return (sim, u, far);
    }

    static long Arrive(Simulation sim, Unit u, int max = 20_000)
    {
        long start = sim.Tick;
        for (int i = 0; i < max && u.Moving; i++)
            sim.Step();
        Assert.False(u.Moving, "never arrived");
        return sim.Tick - start;
    }

    [Fact]
    public void OpeningItsTileMaterialisesItWhereItIs()
    {
        var (sim, u, far) = Trip(rateMode: true);
        Run(sim, 200);
        Assert.True(sim.IsAbstract(u));
        int tile = u.TileId;
        var (x, y) = (u.X, u.Y);

        sim.Enqueue(new SetViewedTileCommand(Me, tile));
        sim.Step();
        // The abstract step moved it once more this tick, then it materialised there with its cruise velocity.
        Assert.False(sim.IsAbstract(u));
        Assert.True(Math.Abs(u.X - x) <= AbstractTravel.Speed(u) + 1 && Math.Abs(u.Y - y) <= AbstractTravel.Speed(u) + 1);
        Assert.True(u.VX > 0);

        // Physics takes over and finishes the trip.
        Arrive(sim, u);
        Assert.Equal(far.Id, u.TileId);
    }

    [Fact]
    public void AnEnemyUnitMakesATileContestedSoItRunsFullAndTheUnitMaterialises()
    {
        // WM-004, FB-013: B belongs to player 1, A to player 0. Player 1's scout flies into A, which nobody views.
        var sim = NewSim();
        sim.RateModeEnabled = true;
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0, owner: Them);
        var u = Scout(sim, b, 15, 15, Them);
        Assert.True(sim.IsAbstract(u));
        Assert.False(RateMode.IsFullSim(sim, a));

        sim.Enqueue(new MoveUnitsCommand(Them, [u.Id], a.Id, 10, 15));
        for (int i = 0; i < 5000 && u.TileId != a.Id; i++)
            sim.Step();
        Assert.Equal(a.Id, u.TileId);
        Assert.True(Territory.ContestedTiles(sim)[a.Id]);
        Assert.True(RateMode.IsFullSim(sim, a));
        Assert.False(sim.IsAbstract(u));
        Assert.False(RateMode.IsFullSim(sim, b));
    }

    [Fact]
    public void AnAbstractPatrolTurnsRoundAtEachEnd()
    {
        // UC-001 orders behave the same as nodes: arriving hands over to movement, which sends the patrol back.
        var sim = NewSim();
        sim.RateModeEnabled = true;
        var a = Tile(sim, 0, 0);
        var far = Tile(sim, 2, 0);
        Tile(sim, 1, 0);
        var u = Scout(sim, a, 4, 15);
        sim.Enqueue(new PatrolUnitsCommand(Me, [u.Id], far.Id, 25, 15));
        sim.Step();
        Assert.True(sim.IsAbstract(u));
        for (int i = 0; i < 20_000 && !u.PatrolReturning; i++)
            sim.Step();
        Assert.Equal(far.Id, u.TileId);
        sim.Step();
        Assert.Equal(OrderKind.Patrol, u.Order?.Kind);
        Assert.True(u.PatrolReturning);
        Assert.True(u.Moving);
        Assert.True(sim.IsAbstract(u));
    }

    [Fact]
    public void CrossingTheViewedTilesBorderMakesANodeThereWithoutStopping()
    {
        // UC-005: a unit flies physically across the viewed tile and goes abstract the tick it crosses, still moving.
        var sim = NewSim();
        sim.RateModeEnabled = true;
        var a = Tile(sim, 0, 0);
        Tile(sim, 1, 0);
        var u = Scout(sim, a, 15, 15);
        sim.Enqueue(new SetViewedTileCommand(Me, a.Id));
        Run(sim, 2);
        Assert.False(sim.IsAbstract(u));
        sim.Enqueue(new MoveUnitsCommand(Me, [u.Id], a.Id + 1, 25, 15));
        for (int i = 0; i < 5000 && u.TileId == a.Id; i++)
        {
            sim.Step();
            Assert.Equal(u.TileId != a.Id, sim.IsAbstract(u));
        }
        Assert.True(sim.IsAbstract(u));
        Assert.True(u.Moving);

        // No wait at the border: from the first tick it covers its cruise speed (within rounding) every tick.
        for (int i = 0; i < 10; i++)
        {
            int x = u.X;
            sim.Step();
            Assert.True(u.X - x >= AbstractTravel.Speed(u) * 9 / 10, $"tick {i}: moved {u.X - x}");
        }
    }

    [Fact]
    public void ANodeOrderedOffTheViewedTileIsANodeFromTheStart()
    {
        var (sim, u, _) = Trip(rateMode: true);
        Assert.True(sim.IsAbstract(u));
        Assert.True(u.Moving);
    }

    [Fact]
    public void ANodePassingThroughTheViewedTileMaterialisesThereAndGoesAbstractAgainAfter()
    {
        var sim = NewSim();
        sim.RateModeEnabled = true;
        var a = Tile(sim, 0, 0);
        var mid = Tile(sim, 1, 0);
        var far = Tile(sim, 2, 0);
        var u = Scout(sim, a, 4, 15);
        sim.Enqueue(new SetViewedTileCommand(Me, mid.Id));
        sim.Enqueue(new MoveUnitsCommand(Me, [u.Id], far.Id, 25, 15));
        sim.Step();
        Assert.True(sim.IsAbstract(u));
        var order = u.Order;

        bool sawPhysical = false;
        for (int i = 0; i < 20_000 && u.Moving; i++)
        {
            sim.Step();
            Assert.Equal(u.TileId != mid.Id, sim.IsAbstract(u));
            sawPhysical |= u.TileId == mid.Id;
            if (u.Moving)
                Assert.Same(order, u.Order);
        }
        Assert.True(sawPhysical);
        Assert.False(u.Moving);
        Assert.Equal(far.Id, u.TileId);

        // A stopped node stays one, exactly where it is, until its tile is opened.
        var (x, y) = (u.X, u.Y);
        Run(sim, 300);
        Assert.True(sim.IsAbstract(u));
        Assert.Equal((x, y), (u.X, u.Y));
        sim.Enqueue(new SetViewedTileCommand(Me, far.Id));
        sim.Step();
        Assert.False(sim.IsAbstract(u));
        Assert.Equal((x, y), (u.X, u.Y));
    }

    /// <summary>My scout travels from tile 0 to tile 2 through tile 1, where an enemy scout is parked; nobody views anything.</summary>
    static (Simulation Sim, Unit Mine, Unit Enemy, TileState Mid, TileState Far) Ambush(Stance stance)
    {
        var sim = NewSim();
        sim.RateModeEnabled = true;
        var a = Tile(sim, 0, 0);
        var mid = Tile(sim, 1, 0, owner: Them);
        var far = Tile(sim, 2, 0);
        var mine = Scout(sim, a, 4, 15);
        var enemy = Scout(sim, mid, 15, 25, Them);
        sim.Enqueue(new SetStanceCommand(Me, [mine.Id], stance));
        sim.Enqueue(new MoveUnitsCommand(Me, [mine.Id], far.Id, 25, 15));
        sim.Step();
        Assert.True(sim.IsAbstract(mine));
        for (int i = 0; i < 20_000 && mine.TileId != mid.Id; i++)
            sim.Step();
        return (sim, mine, enemy, mid, far);
    }

    [Fact]
    public void AnAggressiveNodeThatMeetsEnemiesStopsToFightThenResumesItsOrder()
    {
        var (sim, u, enemy, mid, far) = Ambush(Stance.Aggressive);
        var order = u.Order;
        Assert.True(RateMode.IsFullSim(sim, mid));
        Assert.False(sim.IsAbstract(u));
        Assert.True(AbstractTravel.IsInterrupted(sim, u));

        Run(sim, 600);
        Assert.Equal(mid.Id, u.TileId);
        Assert.Same(order, u.Order);
        Assert.False(sim.IsAbstract(u));

        Combat.DestroyUnit(sim, enemy);
        for (int i = 0; i < 20_000 && (u.TileId != far.Id || u.Moving); i++)
            sim.Step();
        Assert.False(AbstractTravel.IsInterrupted(sim, u));
        Assert.Equal(far.Id, u.TileId);
        Assert.False(u.Moving);
        Assert.True(sim.IsAbstract(u));
    }

    [Fact]
    public void APassiveNodeThatMeetsEnemiesKeepsItsCourse()
    {
        var (sim, u, _, mid, far) = Ambush(Stance.Passive);
        Assert.True(RateMode.IsFullSim(sim, mid));
        Assert.False(sim.IsAbstract(u));
        Assert.False(AbstractTravel.IsInterrupted(sim, u));
        Assert.True(u.Moving);
        Arrive(sim, u);
        Assert.Equal(far.Id, u.TileId);
        Assert.True(sim.IsAbstract(u));
    }

    [Fact]
    public void TurretsCanNotSeeAnAbstractUnitUntilItMaterialises()
    {
        // A laser by the border of my viewed tile, an enemy scout parked just across it on their own unviewed tile.
        var sim = NewSim();
        sim.RateModeEnabled = true;
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0, owner: Them);
        sim.Enqueue(new PlaceBuildingCommand(Me, a.Id, "laser_turret", a.Grid.Width - 2, 15));
        sim.Enqueue(new SetViewedTileCommand(Me, a.Id));
        sim.Step();
        var u = Scout(sim, b, 3, 15, Them);
        Run(sim, 2);
        Assert.True(sim.IsAbstract(u));
        for (int i = 0; i < 300; i++)
        {
            sim.Step();
            Assert.Empty(sim.Projectiles);
        }

        sim.Enqueue(new SetViewedTileCommand(Them, b.Id));
        bool fired = false;
        for (int i = 0; i < 300 && !fired; i++)
        {
            sim.Step();
            fired = sim.Projectiles.Count > 0;
        }
        Assert.False(sim.IsAbstract(u));
        Assert.True(fired);
    }
}

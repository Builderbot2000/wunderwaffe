using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>UC-009: units on rate-mode tiles travel as abstract nodes and materialise where they are.</summary>
public class AbstractTravelTests(ITestOutputHelper output)
{
    const int S = Unit.CellScale;

    /// <summary>Three framed cells in a row: a thruster pushing east on each end and a unit core between them.</summary>
    internal static Unit Scout(Simulation sim, TileState tile, int x, int y, int player = Me)
    {
        for (int cx = x - 1; cx <= x + 1; cx++)
            sim.Enqueue(new PlaceFrameCommand(player, tile.Id, cx, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "thruster", x - 1, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "unit_core", x, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "thruster", x + 1, y));
        sim.Step();
        sim.Enqueue(new ActivateUnitCommand(player, tile.Id, x, y));
        sim.Step();
        return sim.Units[^1];
    }

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
    public void AUnitNobodyWatchesIsANodeAndTakesAboutAsLongAsAFlyingOne()
    {
        var (full, fu, ffar) = Trip(rateMode: false);
        var (rate, ru, rfar) = Trip(rateMode: true);
        Assert.False(full.IsAbstract(fu));
        Assert.True(rate.IsAbstract(ru));

        long eta = AbstractTravel.Eta(rate, ru);
        long flown = Arrive(full, fu), travelled = Arrive(rate, ru);
        output.WriteLine($"full sim {flown} ticks, abstract {travelled} ticks, ETA {eta}");
        Assert.Equal(rfar.Id, ru.TileId);
        Assert.True(Math.Abs(ru.X - (rfar.OriginX + 25L) * S - S / 2) < S && Math.Abs(ru.Y - (rfar.OriginY + 15L) * S - S / 2) < S);
        // UC-009: travel time matches the full sim within a set tolerance.
        Assert.InRange(travelled, flown * (100 - AbstractTravel.TolerancePercent) / 100, flown * (100 + AbstractTravel.TolerancePercent) / 100);
        Assert.InRange(eta, travelled - 1, travelled + 1);
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
        Assert.True(RateMode.ContestedTiles(sim)[a.Id]);
        Assert.True(RateMode.IsFullSim(sim, a));
        Assert.False(sim.IsAbstract(u));
        Assert.False(RateMode.IsFullSim(sim, b));
    }
}

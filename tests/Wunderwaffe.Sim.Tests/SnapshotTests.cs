using System.Diagnostics;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// NF-008 snapshots and NF-004 determinism over a generated match with rate mode: a factory on the home tile, an
/// export link to a claimed neighbour, a unit flying back and forth, and the player's view moving around so tiles and
/// units keep switching modes.
/// </summary>
public class SnapshotTests(ITestOutputHelper output)
{
    const int Minute = 60 * Simulation.TicksPerSecond;

    /// <summary>A scripted match: everything is enqueued by <see cref="Play"/> on fixed ticks, so two runs are identical.</summary>
    sealed class Match
    {
        public Simulation Sim { get; }
        readonly TileState _home, _east;
        readonly Unit _unit;
        readonly (int X, int Y) _here, _there;

        public Match(int snapshotEvery)
        {
            Sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
            Sim.SnapshotIntervalTicks = snapshotEvery;
            _home = Sim.Tiles[Sim.StartTileId];
            _east = Sim.Neighbors(_home).First(n => n.Grid.BuildableCount * 2 > n.Grid.Area);
            foreach (var item in new[] { "copper_ore", "iron_ore", "copper_plate", "iron_plate", "steel" })
                Sim.Enqueue(new DebugGrantCommand(Me, _home.Id, item, 10_000));
            Sim.Step();
            int before = _home.Grid.BuildingCount;

            // Drills on the tile's own deposits (generated from the seed, so a replay has them too): copper straight into a
            // core of its own, iron through a smelter into another.
            var copper = Spot(_home, "copper_ore", 6);
            Place(Sim, _home, "drill", copper.X, copper.Y);
            Place(Sim, _home, "core_large", copper.X + 2, copper.Y);
            var iron = Spot(_home, "iron_ore", 8);
            Place(Sim, _home, "drill", iron.X, iron.Y);
            Place(Sim, _home, "smelter", iron.X + 2, iron.Y, recipe: "smelt_iron");
            Place(Sim, _home, "core_large", iron.X + 4, iron.Y);
            Sim.Step();
            Assert.Equal(before + 5, _home.Grid.BuildingCount);

            var (x, y) = Open(_home, 3, 1);
            _unit = AbstractTravelTests.Scout(Sim, _home, x + 1, y);
            _here = (_home.CenterX - _home.OriginX, _home.CenterY - _home.OriginY);
            _there = (_east.CenterX - _east.OriginX, _east.CenterY - _east.OriginY);
        }

        /// <summary>A free run of <paramref name="width"/> x 3 buildable cells whose first 2 x 2 lies on <paramref name="item"/>.</summary>
        static (int X, int Y) Spot(TileState tile, string item, int width)
        {
            var g = tile.Grid;
            for (int y = 0; y < g.Height - 2; y++)
            {
                for (int x = 0; x < g.Width - width; x++)
                {
                    if (g.InBounds(x, y, width, 3) && g.Buildable(x, y, width, 3) && g.IsFree(x, y, width, 3)
                        && g.DepositAt(x, y) == item && g.DepositAt(x + 1, y) == item && g.DepositAt(x, y + 1) == item && g.DepositAt(x + 1, y + 1) == item)
                        return (x, y);
                }
            }
            throw new InvalidOperationException($"no {item} spot on tile {tile.Id}");
        }

        /// <summary>Runs to <paramref name="until"/>, sending the unit back and forth and moving the view every 20 s.</summary>
        public void Play(long until)
        {
            while (Sim.Tick < until)
            {
                long t = Sim.Tick;
                if (t % (20 * Simulation.TicksPerSecond) == 0)
                {
                    long step = t / (20 * Simulation.TicksPerSecond);
                    int view = (step % 3) switch { 0 => _home.Id, 1 => -1, _ => _east.Id };
                    Sim.Enqueue(new SetViewedTileCommand(Me, view));
                    var (tile, cell) = step % 2 == 0 ? (_east, _there) : (_home, _here);
                    if (Sim.UnitById(_unit.Id) != null)
                        Sim.Enqueue(new MoveUnitsCommand(Me, [_unit.Id], tile.Id, cell.X, cell.Y));
                }
                Sim.Step();
            }
        }
    }

    [Fact]
    public void ASnapshotRestoresTheSameStateAndCarriesOnTheSame()
    {
        var match = new Match(snapshotEvery: 0);
        var sim = match.Sim;
        foreach (long at in new long[] { 30, 75, 130 })
        {
            match.Play(at * Simulation.TicksPerSecond);
            // Through JSON, as a save carries it.
            string json = (SaveFile.From(sim) with { Snapshot = Snapshot.Capture(sim) }).ToJson();
            var restored = SaveFile.FromJson(json).Snapshot!.Restore(TestContent.Load(), sim.Setup!, sim.CommandLog);
            Assert.Equal(sim.StateHash(), restored.StateHash());
            for (int i = 0; i < 300; i++)
            {
                sim.Step();
                restored.Step();
            }
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
        Assert.Single(sim.Units);
        Assert.True(sim.PlayerOrNull(Me)!.ProducedOf("iron_plate") > 20);
    }

    [Fact]
    public void LoadingFromTheLatestSnapshotMatchesAFullReplay()
    {
        // NF-008: the latest snapshot plus the commands after it give the same state hash as replaying everything.
        var match = new Match(snapshotEvery: 50 * Simulation.TicksPerSecond);
        match.Play(170 * Simulation.TicksPerSecond);
        var sim = match.Sim;
        Assert.Equal(150 * Simulation.TicksPerSecond, sim.LatestSnapshot!.Tick);

        var save = SaveFile.FromJson(SaveFile.From(sim, "test").ToJson());
        var fromSnapshot = new Replay(TestContent.Load(), save);
        Assert.True(fromSnapshot.FromSnapshot);
        Assert.Equal(save.Snapshot!.Tick, fromSnapshot.Sim.Tick);
        fromSnapshot.Advance(int.MaxValue);
        Assert.True(fromSnapshot.FromSnapshot, "fell back to a full replay: the snapshot misses some state");
        Assert.True(fromSnapshot.Matches);

        var full = Replay.Load(TestContent.Load(), save, useSnapshot: false);
        Assert.Equal(full.StateHash(), fromSnapshot.Sim.StateHash());
        Assert.Equal(sim.CommandLog.Count, fromSnapshot.Sim.CommandLog.Count);
    }

    [Fact]
    public void ASnapshotThatMissesStateFallsBackToAFullReplay()
    {
        var match = new Match(snapshotEvery: 30 * Simulation.TicksPerSecond);
        match.Play(40 * Simulation.TicksPerSecond);
        var save = SaveFile.From(match.Sim);
        var snap = save.Snapshot!;
        var broken = save with { Snapshot = snap with { Players = [.. snap.Players.Select(p => p with { Shipped = p.Shipped + 1 })] } };
        var replay = new Replay(TestContent.Load(), broken);
        while (!replay.Done)
            replay.Advance(int.MaxValue);
        Assert.False(replay.FromSnapshot);
        Assert.True(replay.Matches);
    }

    [Fact]
    [Trait("Category", "Long")]
    public void ThirtyMinutesWithRateModeAndSwitchingAreDeterministic()
    {
        // NF-004: the same inputs reproduce the same state hash after 30 minutes, with rate-mode tiles, promotion and
        // demotion, and abstract travel. Takes a few seconds (most tiles are in rate mode); filter out Category=Long to skip.
        var sw = Stopwatch.StartNew();
        var a = new Match(snapshotEvery: 5 * Minute);
        var b = new Match(snapshotEvery: 5 * Minute);
        for (int m = 1; m <= 30; m++)
        {
            a.Play(m * Minute);
            b.Play(m * Minute);
            Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        }
        var save = SaveFile.FromJson(SaveFile.From(a.Sim).ToJson());
        var loaded = new Replay(TestContent.Load(), save);
        loaded.Advance(int.MaxValue);
        Assert.True(loaded.FromSnapshot && loaded.Matches);
        Assert.True(Replay.Load(TestContent.Load(), save, useSnapshot: false).StateHash() == a.Sim.StateHash());
        output.WriteLine($"30 min twice plus a full replay in {sw.Elapsed.TotalSeconds:F1} s");
    }
    /// <summary>
    /// Takes a snapshot of a replay of <paramref name="sim"/>'s log at each of <paramref name="seconds"/>, and checks that
    /// resuming from it (through JSON, as a save does) reaches the same final state as the original, without falling back.
    /// </summary>
    static void EverySnapshotResumes(Simulation sim, IEnumerable<int> seconds)
    {
        var save = SaveFile.From(sim);
        foreach (int at in seconds)
        {
            var replay = new Replay(TestContent.Load(), save, useSnapshot: false);
            replay.Advance(at * Simulation.TicksPerSecond);
            var snap = SaveFile.FromJson((save with { Snapshot = Snapshot.Capture(replay.Sim) }).ToJson());
            var resumed = new Replay(TestContent.Load(), snap);
            Assert.True(resumed.FromSnapshot, $"{at} s: restoring alone diverged");
            resumed.Advance(int.MaxValue);
            Assert.True(resumed.FromSnapshot && resumed.Matches, $"{at} s: resuming from the snapshot diverged");
        }
    }

    [Fact]
    public void SnapshotsAnywhereInASessionOfEveryCommandResume()
    {
        // NF-008 against everything the commands touch: frames, sorters, units, squads, rally, combat orders, damage,
        // wrecks, landing, blueprints and the factory.
        var sim = SaveTests.Session();
        EverySnapshotResumes(sim, Enumerable.Range(0, 10).Select(i => 1 + i * 3));
    }

    [Fact]
    public void SnapshotsCarryPowerLogisticsAndAFirefight()
    {
        // NF-008 against factory and combat state: power networks with a battery and a fuel generator, storage, relays,
        // a sorter, a laser turret firing at an enemy scout (projectiles, block damage, wrecks).
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(3, 3, 7, Players: 2));
        var home = sim.Tiles[sim.StartTileIds[0]];
        var away = sim.Tiles[sim.StartTileIds[1]];
        foreach (var item in new[] { "copper_ore", "iron_ore", "copper_plate", "iron_plate", "steel", "fuel" })
            sim.Enqueue(new DebugGrantCommand(Me, home.Id, item, 5_000));
        sim.Enqueue(new SetViewedTileCommand(Me, home.Id));
        sim.Step();

        var core = home.Grid.Buildings.First(b => b.Kind == BuildingKind.Core);
        var (x, y) = Open(home, 14, 6);
        void Put(string id, int dx, int dy, int rotation = 0, string? recipe = null) =>
            sim.Enqueue(new PlaceBuildingCommand(Me, home.Id, id, x + dx, y + dy, rotation, recipe));
        // A battery, a fuel generator, a laser turret and a second core on the core's network: each touching it.
        void Touching(string id, string? recipe = null)
        {
            for (int dy = -3; dy <= core.Height; dy++)
            {
                for (int dx = -3; dx <= core.Width; dx++)
                {
                    if (Placement.Check(sim, home, Me, id, core.X + dx, core.Y + dy, 0, recipe, out _) == PlaceResult.Ok)
                    {
                        sim.Enqueue(new PlaceBuildingCommand(Me, home.Id, id, core.X + dx, core.Y + dy, 0, recipe));
                        sim.Step();
                        var placed = home.Grid.BuildingAt(core.X + dx, core.Y + dy)!;
                        if (home.Grid.Touching(placed).Contains(core))
                            return;
                        sim.Enqueue(new RemoveBuildingCommand(Me, home.Id, core.X + dx, core.Y + dy));
                        sim.Step();
                    }
                }
            }
            throw new InvalidOperationException($"no room for {id} by the core");
        }
        Touching("laser_turret");
        Touching("battery");
        Touching("fuel_generator", "burn_fuel");
        Touching("core_large"); // Spare supply, so the battery charges.
        Put("supply_depot", 4, 0);
        // Belts with a junction, a router, a sorter and a bridge pair, fed by the depot.
        Put("conveyor", 6, 0);
        Put("junction", 7, 0);
        Put("router", 8, 0);
        Put("sorter", 9, 0);
        Put("bridge", 10, 0);
        Put("bridge", 12, 0);
        Put("conveyor", 13, 0);
        sim.Enqueue(new SetFilterCommand(Me, home.Id, x + 9, y, "iron_plate"));
        sim.Step();

        // The enemy's starting scout lifts off and flies over the turret.
        var scoutCore = away.Grid.Cores.First(b => b.Owner == Them);
        sim.Enqueue(new ActivateUnitCommand(Them, away.Id, scoutCore.X, scoutCore.Y));
        sim.Step();
        var scout = sim.Units.Last(u => u.Owner == Them);
        var laser = home.Grid.Buildings.First(b => b.DefId == "laser_turret");
        sim.Enqueue(new MoveUnitsCommand(Them, [scout.Id], home.Id, laser.X + 2, laser.Y + 2));
        long fired = 0;
        for (int i = 0; i < 240 * Simulation.TicksPerSecond && sim.UnitById(scout.Id) != null; i++)
        {
            sim.Step();
            fired = Math.Max(fired, sim.Projectiles.Count);
        }
        for (int i = 0; i < 10 * Simulation.TicksPerSecond; i++)
            sim.Step();
        output.WriteLine($"{sim.Tick / Simulation.TicksPerSecond} s; projectiles at once: {fired}; scout alive: {sim.UnitById(scout.Id) != null}");
        Assert.True(fired > 0, "the turret never fired");
        Assert.Contains(PowerNet.Networks(home), n => n.Stored > 0);

        int end = (int)(sim.Tick / Simulation.TicksPerSecond);
        EverySnapshotResumes(sim, Enumerable.Range(1, 8).Select(i => end * i / 9));
    }
}

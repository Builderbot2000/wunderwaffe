using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>WM-019 / OQ-54: a seat that loses its last core leaves its buildings unclaimed; building or landing near claims them.</summary>
public class UnclaimedTests
{
    /// <summary>
    /// An unowned 40-cell tile, my bank in the corner, where <see cref="Them"/> has a drill on iron ore at
    /// (10, 20) and a belt running east from (12, 20); their bank at (30, 2) is their only core.
    /// </summary>
    static (Simulation Sim, TileState Tile, PlacedBuilding Drill, PlacedBuilding Belt) Site()
    {
        var sim = NewSim();
        var tile = sim.AddTile(0, 0, TileState.Unowned, 40); // nobody's circle in the way
        Bank(sim, tile, Me, 39, 39);
        Bank(sim, tile, Them, 30, 2);
        Ore(tile, "iron_ore", 10, 20);
        var drill = Plant(sim, tile, "drill", 10, 20, "mine_iron_ore");
        var belt = Plant(sim, tile, "conveyor", 12, 20);
        return (sim, tile, drill, belt);
    }

    static PlacedBuilding Plant(Simulation sim, TileState tile, string id, int x, int y, string? recipe = null, int player = Them)
    {
        var def = sim.Content.Buildings[id];
        return tile.Grid.Add(def, player, x, y, def.Width, def.Height, 0, recipe == null ? null : sim.Content.Recipes[recipe]);
    }

    [Fact]
    public void LosingTheLastCoreLeavesEveryBuildingUnclaimed()
    {
        var (sim, tile, drill, belt) = Site();
        var other = PutCore(sim, tile, Them, 30, 30);
        Combat.DestroyBuilding(sim, tile, other);
        Assert.Equal(Them, drill.Owner); // a core left: nothing changes

        Eliminate(sim, Them);
        Assert.True(Territory.IsUnclaimed(drill) && Territory.IsUnclaimed(belt));
        Assert.Equal(sim.Content.Buildings["drill"].Hp, drill.Hp);
        Assert.Contains(sim.Events, e => e.Kind == GameEventKind.BuildingsUnclaimed && e.Player == Them && e.TileId == tile.Id);
        var query = new GameQuery(sim, Me);
        Assert.Equal([drill.Id, belt.Id], query.UnclaimedBuildings(tile.Id).Select(b => b.Id));
    }

    [Fact]
    public void ACoreOnAUnitKeepsTheSeatsBuildingsClaimed()
    {
        var (sim, tile, drill, _) = Site();
        var scout = Scout(sim, tile, 20, 10, Them);
        Combat.DestroyBuilding(sim, tile, tile.Grid.Cores.Single(c => c.Owner == Them));
        Assert.Equal(Them, drill.Owner);

        Combat.DestroyUnit(sim, scout);
        Assert.True(Territory.IsUnclaimed(drill));
    }

    [Fact]
    public void UnclaimedBuildingsDoNothingAndGiveNoRadiusPresenceOrCircle()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40, owner: Them);

        Ore(tile, "iron_ore", 18, 18);
        var drill = Plant(sim, tile, "drill", 18, 18, "mine_iron_ore");
        var turret = Plant(sim, tile, "gun_turret", 24, 18);
        Assert.Contains(Them, Expansion.CircleOccupants(tile));

        Eliminate(sim, Them);
        Assert.Equal(TileState.Unowned, tile.Owner);
        Assert.Empty(Expansion.CircleOccupants(tile));
        for (int p = Me; p <= Them; p++)
        {
            Assert.DoesNotContain(Territory.BuildRadii(sim, p, tile), r => r.BuildingId == turret.Id);
            Assert.False(Territory.HasPresence(sim, p, tile));
        }
        Run(sim, 600);
        Assert.Equal(0, drill.Machine!.OutputCount);
        Assert.False(drill.Machine.Working);
    }

    [Fact]
    public void PlacingNearUnclaimedBuildingsClaimsThem()
    {
        var (sim, tile, drill, belt) = Site();
        var far = Plant(sim, tile, "conveyor", 2, 2);
        Eliminate(sim, Them);
        var query = new GameQuery(sim, Me);
        Assert.Equal([drill.Id, belt.Id], query.ClaimableNear(tile.Id, 14, 26));

        Place(sim, tile, "conveyor", 14, 26);
        sim.Step();
        Assert.Equal(Me, drill.Owner);
        Assert.Equal(Me, belt.Owner);
        Assert.True(Territory.IsUnclaimed(far));
        Assert.Contains(sim.Events, e => e.Kind == GameEventKind.BuildingClaimed && e.Player == Me && e.ItemId == "drill");

        // Mine now: it mines onto the belt.
        Run(sim, 600);
        Assert.True(belt.Belt!.Count > 0 || drill.Machine!.OutputCount > 0);
    }

    [Fact]
    public void TwoClaimsOnOneTickGoToTheFirstCommand()
    {
        var (sim, tile, drill, _) = Site();
        Bank(sim, tile, 2, 0, 39);
        Eliminate(sim, Them);
        sim.Enqueue(new PlaceBuildingCommand(2, tile.Id, "conveyor", 10, 25));
        Place(sim, tile, "conveyor", 11, 25);
        sim.Step();
        Assert.Equal(2, drill.Owner);
    }

    [Fact]
    public void LandingNearUnclaimedBuildingsClaimsThem()
    {
        var (sim, tile, drill, _) = Site();
        var scout = Scout(sim, tile, 10, 12);
        Eliminate(sim, Them);
        Assert.True(Territory.IsUnclaimed(drill));

        sim.Enqueue(new DeployUnitCommand(Me, scout.Id));
        sim.Step();
        Assert.Empty(sim.Units);
        Assert.Equal(Me, drill.Owner);
    }

    [Fact]
    public void UnclaimedStateIsHashedSnapshottedAndReplayed()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default with { Players = 2 });
        var theirs = sim.Tiles[sim.StartTileIds[Them]];
        var core = theirs.Grid.Cores.Single(c => c.Owner == Them);
        Run(sim, 10);
        ulong before = sim.StateHash();
        sim.Enqueue(new DebugDestroyCommand(Me, theirs.Id, core.X, core.Y));
        Run(sim, 60);
        Assert.NotEqual(before, sim.StateHash());
        Assert.NotEmpty(Territory.UnclaimedOn(theirs));
        Assert.True(Victory.IsEliminated(sim, Them));

        string json = (SaveFile.From(sim) with { Snapshot = Snapshot.Capture(sim) }).ToJson();
        var restored = SaveFile.FromJson(json).Snapshot!.Restore(TestContent.Load(), sim.Setup!, sim.CommandLog);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.NotEmpty(Territory.UnclaimedOn(restored.Tiles[theirs.Id]));
        Assert.Equal(sim.StateHash(), Replay.Load(TestContent.Load(), SaveFile.FromJson(json), useSnapshot: false).StateHash());
    }
}

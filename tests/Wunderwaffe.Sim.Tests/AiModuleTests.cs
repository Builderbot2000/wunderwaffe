using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Ai.Modules;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>The AI framework (MP-006): selection, the strategic layer and each module on its own.</summary>
public class AiModuleTests
{
    sealed class Fixed(string name, params Proposal[] proposals) : IAiModule
    {
        public string Name => name;
        public IEnumerable<Proposal> Propose(AiContext c) => proposals;
    }

    static Proposal P(string key, int score, string? lockId = null, Dictionary<string, int>? cost = null, int tile = -1) =>
        new(key, score, [new SetViewedTileCommand(0, 0)], CostTile: tile, Cost: cost, Locks: lockId == null ? null : [lockId]);

    [Fact]
    public void BestScoresGoFirstWithinTheBudgetLocksAndFunds()
    {
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(3, 3, 5));
        int home = sim.StartTileId;
        var ai = new AiPlayer(0, new AiConfig(Budget: 4))
        {
            Modules =
            [
                new Fixed("a", P("x", 10), P("b", 15), P("locked", 30, "L"), P("rich", 40, cost: new() { ["copper_ore"] = 1_000_000_000 }, tile: home)),
                new Fixed("b", P("y", 20), P("a", 15), P("loser", 25, "L"), P("last", 5)),
            ],
        };
        ai.Think(sim);
        var report = ai.Last!;
        Assert.Equal(["a/rich", "a/locked", "b/loser", "b/y", "a/b", "b/a", "a/x", "b/last"],
            report.Proposals.Select(p => $"{p.Module}/{p.Key}"));
        Assert.Equal([ProposalOutcome.Unaffordable, ProposalOutcome.Chosen, ProposalOutcome.Locked, ProposalOutcome.Chosen,
            ProposalOutcome.Chosen, ProposalOutcome.Chosen, ProposalOutcome.OverBudget, ProposalOutcome.OverBudget], report.Proposals.Select(p => p.Outcome));
        Assert.Contains("a/locked", report.Describe());

        // Re-weighting reorders; a weight of 0 switches a module off.
        ai.Config = new AiConfig(new Dictionary<string, int> { ["a"] = 0, ["b"] = 300 });
        ai.Think(sim);
        Assert.Equal(["b/loser", "b/y", "b/a", "b/last"], ai.Last!.Proposals.Select(p => $"{p.Module}/{p.Key}"));
        Assert.Equal(75, ai.Last.Proposals[0].Score);
    }

    /// <summary>
    /// My tiles A (0,0) and D (0,1), their tile B (1,0) with a unit of theirs, unclaimed C (1,1); my
    /// <paramref name="units"/> scouts stand on D.
    /// </summary>
    static (Simulation Sim, TileState A, TileState B, TileState C, TileState D) Front(int units)
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, owner: Me);
        var b = Tile(sim, 1, 0, owner: Them);
        var d = Tile(sim, 0, 1, owner: Me);
        var c = Tile(sim, 1, 1, owner: TileState.Unowned);
        Scout(sim, b, 10, 10, Them);
        for (int i = 0; i < units; i++)
            Scout(sim, d, 3 + 7 * i, 10);
        return (sim, a, b, c, d);
    }

    [Fact]
    public void TheInfluenceMapSetsTileGoals()
    {
        var (sim, a, b, c, d) = Front(1);
        var ctx = AiContext.Observe(sim, Me);
        Assert.Equal(a.Id, ctx.Home);
        Assert.Equal(TileGoal.Reinforce, ctx.GoalOf(a.Id));
        Assert.Equal(TileGoal.Attack, ctx.GoalOf(b.Id));
        Assert.Equal(TileGoal.Expand, ctx.GoalOf(c.Id));
        Assert.Equal(TileGoal.Hold, ctx.GoalOf(d.Id));
        Assert.True(ctx.Influence[a.Id].Threat > 0);
        Assert.True(ctx.Influence[d.Id].Own > 0);
    }

    [Fact]
    public void DefenceSendsIdleUnitsToAThreatenedTile()
    {
        var (sim, a, _, _, _) = Front(1);
        Assert.Empty(new DefenceModule().Propose(AiContext.Observe(sim, Me)));   // a lone unit is the next scout

        (sim, a, _, _, _) = Front(2);
        var p = Assert.Single(new DefenceModule().Propose(AiContext.Observe(sim, Me)));
        var move = Assert.IsType<AttackMoveCommand>(Assert.Single(p.Commands));
        Assert.Equal(a.Id, move.TileId);
        Assert.Equal(2, move.UnitIds.Length);
    }

    [Fact]
    public void AttackFormsASquadAndSendsItAtTheEnemyCore()
    {
        var (sim, _, b, _, _) = Front(AttackModule.Group);
        var attack = new AttackModule();
        var first = Assert.Single(attack.Propose(AiContext.Observe(sim, Me)));
        Assert.IsType<CreateSquadCommand>(Assert.Single(first.Commands));

        AttackMoveCommand? sent = null;
        for (int i = 0; i < 4 && sent == null; i++)
        {
            foreach (var p in attack.Propose(AiContext.Observe(sim, Me)))
            {
                foreach (var command in p.Commands)
                {
                    sent ??= command as AttackMoveCommand;
                    sim.Enqueue(command);
                }
            }
            sim.Step();
        }
        Assert.NotNull(sent);
        Assert.Equal(b.Id, sent.TileId);
        Assert.Equal(AttackModule.Group, sent.UnitIds.Length);
        Assert.Single(sim.Squads.OwnedBy(Me));
    }

    [Fact]
    public void AFreshStartBuildsAHomeCoreFirstAndWaitsToExpand()
    {
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, 3037));
        var c = AiContext.Observe(sim, 0);
        Assert.Contains(new EconomyModule().Propose(c), p => p.Key == $"base@{c.Home}");
        Assert.Empty(new ExpansionModule().Propose(c));     // no ore lines yet
        Assert.Empty(new ProductionModule().Propose(c));    // no home core off the frame yet, no tile with the missing deposit
        Assert.Equal("scout", c.Design?.Name);
    }

    [Fact]
    public void ThePlanChainsWhatTheDesignNeedsBackToDeposits()
    {
        // MP-007: the scout's thrusters want steel; steel wants plates and coal; the buildings on the way want ores and
        // plates. Home has no coal, so the steel chain misses it.
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, 3037));
        var plan = AiContext.Observe(sim, 0).Plan;
        var steel = plan.Of("steel")!.Chain;
        Assert.False(steel.IsLinear);
        Assert.Equal(["coal", "iron_ore"], steel.Leaves);
        Assert.Equal(["coal"], plan.Missing);
        var plate = plan.Of("iron_plate")!.Chain;
        Assert.True(plate.IsLinear);
        Assert.Equal("iron_ore", plate.Deposit);
        Assert.Equal("smelter", Assert.Single(plate.Stages).BuildingId);
        Assert.Equal(["copper_ore", "iron_ore"], plan.Raw.Select(d => d.Item).Order(StringComparer.Ordinal));
        Assert.Equal("drill", plan.Of("copper_ore")!.Chain.Building.Id);
        Assert.Null(plan.Of("coal"));     // only the mill wants it, not the core
    }

    [Fact]
    public void TheFirstScoutCarriesWhatTheMissingDepositsTileNeeds()
    {
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, 3037));
        var c = AiContext.Observe(sim, 0);
        var (target, item) = ExpansionModule.Target(c);
        Assert.True(target >= 0);
        Assert.Equal("coal", item);
        Assert.Contains(target, c.Tiles[c.Home].Neighbors);
        Assert.True(c.HasDeposit(target, item!));

        var (belt, wire, x, y) = ProductionModule.ExportRun(c, target, c.Home, item!);
        Assert.True(x >= 0 && belt >= 0 && wire >= 0);
        var carry = ProductionModule.Carry(c);
        var defs = sim.Content.Buildings;
        foreach (var (id, n) in ProductionModule.MinCarry(c))
            Assert.True(carry[id] >= n);
        Assert.True(carry["copper_ore"] >= defs["export_port"].Cost["copper_ore"] + defs["drill"].Cost["copper_ore"] + belt + wire);
    }

    [Fact]
    public void ReachComesByVehicleWhenACoreCantBePaidFor()
    {
        // WM-015: home's only core is empty, so a core toward the far cell can't be paid for; the idle scout flies there.
        var sim = NewSim();
        var tile = sim.AddTile(0, 0, Me, 64);
        var core = PutCore(sim, tile, Me, 2, 2);
        var bank = Bank(sim, tile, Me, 63, 63);
        var scout = Scout(sim, tile, 6, 8);
        Combat.DestroyBuilding(sim, tile, bank);

        var p = AiContext.Observe(sim, Me).Reach("far", 100, tile.Id, [(50, 50, (1, 1))], (3, 3))!;
        var move = Assert.IsType<MoveUnitsCommand>(Assert.Single(p.Commands));
        Assert.Equal([scout.Id], move.UnitIds);
        Assert.Equal((tile.Id, 50, 50), (move.TileId, move.X, move.Y));

        // Once the core can be paid for, a core is built toward it instead.
        Stock(core, "copper_ore", 100);
        Stock(core, "iron_ore", 100);
        p = AiContext.Observe(sim, Me).Reach("far", 100, tile.Id, [(50, 50, (1, 1))], (3, 3))!;
        Assert.Equal("core", Assert.IsType<PlaceBuildingCommand>(Assert.Single(p.Commands)).BuildingId);
    }

    /// <summary>My home (0,0) with a bank and three gun ships; an unowned tile east (1,0) and south (0,1).</summary>
    static (Simulation Sim, TileState Home, TileState East, TileState South) SiteFront()
    {
        var sim = new Simulation(TestContent.WithSites());
        var home = Tile(sim, 0, 0, 64);
        var east = sim.AddTile(1, 0, TileState.Unowned, 64);
        var south = sim.AddTile(0, 1, TileState.Unowned, 64);
        for (int i = 0; i < AttackModule.Group; i++)
        {
            LayUnit(sim, home, 6, 6 + 6 * i, Me, "gun_turret", "gun_turret");
            LiftOff(sim, home, 6, 6 + 6 * i);
        }
        return (sim, home, east, south);
    }

    [Fact]
    public void AttackTakesOnASiteItOutmatchesButNeverACitadel()
    {
        var (sim, _, east, south) = SiteFront();
        var weak = sim.BuildSite(east, sim.Content.Sites["outpost"], 20, 20);
        var citadel = sim.BuildSite(south, sim.Content.Sites["citadel"], 20, 20);
        var q = new Api.GameQuery(sim, Me);
        long dps = sim.Units.Where(u => u.Owner == Me).Sum(u => (long)q.UnitStatsOf(u.Id)!.Dps);
        long hp = q.Units(Me).Sum(u => (long)u.Hp);
        Assert.False(AttackModule.Outmatches(dps, hp, q.SiteOf(weak.Player)!));    // four cannons are too many
        Assert.False(AttackModule.Outmatches(dps, hp, q.SiteOf(citadel.Player)!));

        foreach (var t in east.Grid.Buildings.Where(b => b.Owner == weak.Player && b.Kind == Content.BuildingKind.Weapon).ToList())
            Combat.DestroyBuilding(sim, east, t);
        var c = AiContext.Observe(sim, Me);
        Assert.Equal([weak.Id], AttackModule.SiteTargets(c, dps, hp, 32, 32).Select(s => s.Id));

        AttackMoveCommand? sent = null;
        var attack = new AttackModule();
        for (int i = 0; i < 4 && sent == null; i++)
        {
            foreach (var p in attack.Propose(AiContext.Observe(sim, Me)))
            {
                foreach (var command in p.Commands)
                {
                    sent ??= command as AttackMoveCommand;
                    sim.Enqueue(command);
                }
            }
            sim.Step();
        }
        Assert.NotNull(sent);
        Assert.Equal(east.Id, sent.TileId);
        Assert.Equal(AttackModule.Group, sent.UnitIds.Length);

        // With only the citadel left the squad stays home: no enemy seat to attack either.
        Eliminate(sim, weak.Player);
        Assert.DoesNotContain(attack.Propose(AiContext.Observe(sim, Me)), p => p.Commands.Any(x => x is AttackMoveCommand));
    }

    [Fact]
    public void AFallenSiteIsClaimedByLandingBesideIt()
    {
        var sim = new Simulation(TestContent.WithSites());
        var home = Tile(sim, 0, 0, 64);
        var east = sim.AddTile(1, 0, TileState.Unowned, 64);
        Scout(sim, home, 50, 30);
        var site = sim.BuildSite(east, sim.Content.Sites["outpost"], 20, 20);
        Eliminate(sim, site.Player);
        Assert.NotEmpty(Territory.UnclaimedOn(east));

        var claim = new ClaimModule();
        var target = Assert.Single(ClaimModule.Targets(AiContext.Observe(sim, Me)));
        Assert.Equal(east.Id, target.Tile);
        for (int i = 0; i < 60 && Territory.UnclaimedOn(east).Count > 0; i++)
        {
            foreach (var p in claim.Propose(AiContext.Observe(sim, Me)))
            {
                foreach (var command in p.Commands)
                    sim.Enqueue(command);
            }
            Run(sim, 60);
        }
        Assert.Empty(Territory.UnclaimedOn(east));
        Assert.Contains(east.Grid.Buildings, b => b.Owner == Me && b.Kind == Content.BuildingKind.Weapon);
    }

    [Theory]
    [InlineData("nomads", true)]
    [InlineData("charter", false)]
    public void ACaptureToBuildFactionPrefersACaptureTarget(string faction, bool factoryFirst)
    {
        // MP-010: their fallen base leaves a unit factory east and seven gun turrets (worth more) south, as far from home.
        var sim = FactionSim(faction);
        Tile(sim, 0, 0, 64);
        var east = Tile(sim, 1, 0, 64, owner: Them);
        var south = sim.AddTile(0, 1, Them, 64);
        Bank(sim, south, Them, 63, 63);
        sim.Enqueue(new PlaceBuildingCommand(Them, east.Id, "unit_factory", 9, 29));
        for (int i = 0; i < 7; i++)
            sim.Enqueue(new PlaceBuildingCommand(Them, south.Id, "gun_turret", 30 + i, 10));
        sim.Step();
        Eliminate(sim, Them);
        Assert.Equal(8, Territory.UnclaimedOn(east).Count + Territory.UnclaimedOn(south).Count);

        var best = ClaimModule.Targets(AiContext.Observe(sim, Me)).First();
        Assert.Equal(factoryFirst ? east.Id : south.Id, best.Tile);
        Assert.Equal(factoryFirst, best.Capture);
    }
}

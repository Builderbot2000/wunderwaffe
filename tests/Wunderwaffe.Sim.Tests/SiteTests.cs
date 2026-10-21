using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>WM-010 / WM-018 / OQ-54 / OQ-57: neutral guardian sites, placed by generation, won by destroying their cores.</summary>
public class SiteTests
{
    static readonly ContentDb Content = TestContent.WithSites();
    static readonly GameSetup Duel = GameSetup.Default with { Players = 2 };

    // Generated once for the read-only tests: a duel with sites, and the same seed without.
    static readonly Lazy<Simulation> Match = new(() => Simulation.NewGame(Content, Duel));
    static readonly Lazy<Simulation> Bare = new(() => Simulation.NewGame(Content, Duel with { SitesPercent = 0 }));

    static long Dist(Simulation sim, TileState t) =>
        sim.StartTileIds.Min(s => Sq(t.CenterX - sim.Tiles[s].CenterX) + Sq(t.CenterY - sim.Tiles[s].CenterY));

    static long Sq(long v) => v * v;

    [Fact]
    public void GenerationPlacesSitesAwayFromStartsAndCirclesStrongerFartherOut()
    {
        var (sim, bare) = (Match.Value, Bare.Value);
        Assert.Empty(bare.Sites);
        Assert.InRange(sim.Sites.Count, 3, (sim.Tiles.Count * GameSetup.DefaultSitesPercent + 50) / 100);

        var near = sim.StartTileIds.SelectMany(s => sim.Tiles[s].Neighbors.Append(s)).ToHashSet();
        int c = Balance.SiteCircleClearance;
        foreach (var s in sim.Sites)
        {
            var tile = sim.Tiles[s.TileId];
            var def = Content.Sites[s.Template];
            Assert.DoesNotContain(s.TileId, near);
            Assert.False(tile.InCircle(s.X - c, s.Y - c, s.Width + 2 * c, s.Height + 2 * c));
            Assert.True(s.Player >= Duel.Players && sim.IsNeutral(s.Player) && !Duel.IsAi(s.Player));
            Assert.Equal(TileState.Unowned, tile.Owner);
            Assert.Contains(tile.Grid.Cores, b => b.Owner == s.Player && b.Def.Neutral);
            Assert.Equal(def.Guards, sim.Units.Count(u => u.Owner == s.Player && u.Stance == Stance.Defensive));

            // Its deposit patch holds the template's richness times what the same seed gives without sites.
            var patch = tile.Grid.Patches.ById(s.Patch)!;
            var plain = bare.Tiles[s.TileId].Grid.Patches.ById(s.Patch)!;
            Assert.Equal(plain.Start * def.Richness / 100, patch.Start);

            // What it holds: tech no player faction builds, and stock.
            var view = new GameQuery(sim, Me).SiteOf(s.Player)!;
            Assert.NotEmpty(view.Tech);
            Assert.All(view.Tech, id => Assert.True(Content.Buildings[id].Neutral));
            Assert.NotEmpty(view.Stock);
            Assert.True(view.Dps > 0);
        }

        // Nearest the starts the weakest template, farthest the strongest.
        var ranks = sim.Sites.OrderBy(s => Dist(sim, sim.Tiles[s.TileId])).Select(s => Content.SiteList.ToList().FindIndex(d => d.Id == s.Template)).ToList();
        Assert.Equal(ranks.Order(), ranks);
        Assert.True(ranks[^1] > ranks[0]);
        Assert.Equal(Duel.Players, new GameQuery(sim, Me).Players().Count);
    }

    [Fact]
    public void TheSameSeedGivesTheSameSites()
    {
        var a = Match.Value;
        var b = Simulation.NewGame(Content, Duel);
        var c = Simulation.NewGame(Content, Duel with { Seed = 99 });
        Assert.Equal(a.Sites, b.Sites);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.Sites.Select(s => (s.TileId, s.X, s.Y)), c.Sites.Select(s => (s.TileId, s.X, s.Y)));
        Assert.NotEqual(a.StateHash(), Bare.Value.StateHash());
    }

    [Fact]
    public void PlayerFactionsCannotBuildNeutralTech()
    {
        var neutral = Content.BuildingList.Where(b => b.Neutral).ToList();
        Assert.NotEmpty(neutral);
        foreach (var f in Content.FactionList)
            Assert.All(neutral, b => Assert.Equal(f.Hidden, f.Has(b.Id)));
        // Content without factions (no guardians) can't build it either.
        Assert.All(neutral, b => Assert.False(TestContent.Load().DefaultFaction.Has(b.Id)));

        // Templates must rise in strength: reversed, they are refused.
        var sites = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "sites.json"));
        var list = System.Text.Json.Nodes.JsonNode.Parse(sites, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsArray();
        var reversed = new System.Text.Json.Nodes.JsonArray([.. list.Reverse().Select(n => n!.DeepClone())]);
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"),
            Read("goals.json"), Read("terrain.json"), Read("layers.json"), factions: Read("factions.json"), sites: reversed.ToJsonString()));
        Assert.Contains(e.Errors, x => x.Contains("must rise"));
    }

    static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));

    [Fact]
    public void ANeutralSiteNeverClaimsItsCircle()
    {
        var sim = new Simulation(Content);
        var tile = sim.AddTile(0, 0, TileState.Unowned, 64);
        Bank(sim, tile, Me, 63, 63);
        var site = sim.AddSite(tile, Content.SiteList[0], tile.CircleX - 1, tile.CircleY - 1);
        PutCore(sim, tile, site.Player, tile.CircleX - 1, tile.CircleY - 1, "keeper_core");
        Assert.Contains(site.Player, Expansion.CircleOccupants(tile));

        Run(sim, Expansion.CaptureTicks(sim) + 10);
        Assert.Equal(TileState.Unowned, tile.Owner);
        Assert.Equal(0, Expansion.Progress(tile, site.Player));
        Assert.Equal("keepers", sim.FactionOf(site.Player).Id);
    }

    [Fact]
    public void DestroyingASitesCoresLeavesItUnclaimedAndClaimableWhileOthersStand()
    {
        var sim = Simulation.NewGame(Content, Duel);
        var a = sim.Sites.First(s => Content.Sites[s.Template].Guards > 0);
        var b = sim.Sites.First(s => s != a);
        var tile = sim.Tiles[a.TileId];
        int held = tile.Grid.Buildings.Count(x => x.Owner == a.Player);

        Eliminate(sim, a.Player);
        Assert.True(sim.HasFallen(a));
        Assert.False(sim.HasFallen(b));
        Assert.DoesNotContain(tile.Grid.Buildings, x => x.Owner == a.Player);
        Assert.Equal(held - 1, Territory.UnclaimedOn(tile).Count); // all but the destroyed core
        Assert.Contains(sim.Tiles[b.TileId].Grid.Buildings, x => x.Owner == b.Player);
        Assert.Contains(sim.Events, e => e.Kind == GameEventKind.SiteFallen && e.Player == a.Player && e.ItemId == a.Name);
        var view = new GameQuery(sim, Me).SiteOf(a.Player)!;
        Assert.True(view.Fallen);
        Assert.Contains(view.Tech, id => Content.Buildings[id].Neutral);

        // Building beside it claims it: neutral tech becomes mine (capture to build, MP-010).
        Territory.ClaimNear(sim, tile, Me, a.X, a.Y, a.Width, a.Height);
        Assert.Empty(Territory.UnclaimedOn(tile));
        Assert.Contains(sim.Player(Me).Acquired, id => Content.Buildings[id].Neutral);
    }

    [Fact]
    public void NeutralSitesNeitherEndNorHoldUpTheMatch()
    {
        var sim = Simulation.NewGame(Content, Duel);
        foreach (var s in sim.Sites)
            Eliminate(sim, s.Player);
        Run(sim, Victory.CheckEvery + 1);
        Assert.False(sim.IsOver);
        Assert.DoesNotContain(sim.Events, e => e.Kind == GameEventKind.PlayerEliminated);

        // With every site still standing, the last seat left wins: sites aren't players.
        var other = Simulation.NewGame(Content, Duel);
        Eliminate(other, Them);
        Run(other, Victory.CheckEvery + 1);
        Assert.Equal(Me, other.Winner);
        Assert.Equal(VictoryReason.LastStanding, other.EndReason);
        Assert.All(other.Sites, s => Assert.False(other.HasFallen(s)));
    }

    [Fact]
    public void PlayerAisKeepPlayingBesideSitesWhichNeverAct()
    {
        var sim = Simulation.NewGame(Content, Duel with { AiPlayers = 2 });
        var director = new Ai.AiDirector(sim.Setup!);
        var guards = sim.Units.Where(u => sim.IsNeutral(u.Owner)).Select(u => (u.Id, u.AnchorX, u.AnchorY)).ToList();
        for (int i = 0; i < 5 * 60 * Simulation.TicksPerSecond; i++)
        {
            director.BeforeStep(sim);
            sim.Step();
        }
        Assert.All(new[] { Me, Them }, p => Assert.Contains(sim.CommandLog, c => c.Command.Player == p));
        Assert.DoesNotContain(sim.CommandLog, c => sim.IsNeutral(c.Command.Player));
        Assert.DoesNotContain(sim.Tiles, t => sim.IsNeutral(t.Owner));
        // Guards that live stay within their leash of where they stood.
        long leash = (long)(UnitSim.LeashCells + 16) * Unit.CellScale;
        foreach (var (id, ax, ay) in guards)
        {
            if (sim.UnitById(id) is { } u)
                Assert.True(Sq(u.X - ax) + Sq(u.Y - ay) <= leash * leash);
        }
    }

    [Fact]
    public void AMapSavedAsAScenarioKeepsItsSites()
    {
        var content = TestContent.WithSites();
        var a = Simulation.NewGame(content, Duel);
        var def = content.AddScenario(Scenarios.ToJson(a.ToScenario("sites", "Sites")));
        Assert.Equal(a.Sites.Count, def.Sites!.Count);
        Assert.Equal(Duel.Players, def.Seats.Count);

        var b = Simulation.NewGame(content, GameSetup.ForScenario(def));
        Assert.Equal(a.Sites, b.Sites);
        Assert.Equal(a.StateHash(), b.StateHash());
        Run(a, 120);
        Run(b, 120);
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}

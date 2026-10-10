using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

public class WorldGenTests
{
    static bool Has(TileState tile, string item) => tile.Grid.DepositCounts().ContainsKey(item);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3037UL)]
    [InlineData(123456789UL)]
    public void RegionalItemsAreMissingAtHomeButFoundNextDoor(ulong seed)
    {
        // RS-003: coal forces expansion, so it must never be at home and always be one claim away.
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, seed));
        var home = sim.TileAt(2, 2)!;
        Assert.Equal(0, home.Owner);
        Assert.False(Has(home, "coal"));
        Assert.True(Has(home, "copper_ore"));
        Assert.True(Has(home, "iron_ore"));
        Assert.Contains(sim.Neighbors(home), n => Has(n, "coal"));
        Assert.Contains(sim.Tiles, t => !Has(t, "coal"));
    }

    [Fact]
    public void SameSetupGivesSameWorld()
    {
        var a = Simulation.NewGame(TestContent.Load(), new GameSetup(7, 5, 99));
        var b = Simulation.NewGame(TestContent.Load(), new GameSetup(7, 5, 99));
        var c = Simulation.NewGame(TestContent.Load(), new GameSetup(7, 5, 100));
        Assert.Equal(35, a.Tiles.Count);
        // Deposits never change, so the state hash leaves them out: compare them directly.
        static string Deposits(Simulation s) =>
            string.Join(";", s.Tiles.Select(t => string.Join(",", t.Grid.DepositCounts().Select(kv => $"{kv.Key}={kv.Value}"))));
        Assert.Equal(Deposits(a), Deposits(b));
        Assert.NotEqual(Deposits(a), Deposits(c));
        Assert.Equal((3, 2), (a.Tiles.Single(t => t.Owner == 0).X, a.Tiles.Single(t => t.Owner == 0).Y));
    }

    [Theory]
    [InlineData("3037", 3037UL)]
    [InlineData(" 42 ", 42UL)]
    public void NumericSeedsAreUsedAsIs(string text, ulong seed) => Assert.Equal(seed, GameSetup.ParseSeed(text));

    [Fact]
    public void TextSeedsHashStably()
    {
        Assert.Equal(GameSetup.ParseSeed("wunderwaffe"), GameSetup.ParseSeed("wunderwaffe"));
        Assert.NotEqual(GameSetup.ParseSeed("wunderwaffe"), GameSetup.ParseSeed("wunderwaffe2"));
    }
}

using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

public class WorldGenTests
{
    static bool Has(TileState tile, string item) => tile.Grid.DepositCounts().ContainsKey(item);

    static Simulation World(ulong seed = 3037, int w = 5, int h = 5) =>
        Simulation.NewGame(TestContent.Load(), new GameSetup(w, h, seed));

    static IEnumerable<(int X, int Y)> Cells(TileState t)
    {
        for (int y = 0; y < t.Grid.Height; y++)
        {
            for (int x = 0; x < t.Grid.Width; x++)
            {
                if (t.Grid.InBounds(x, y))
                    yield return (x, y);
            }
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3037UL)]
    [InlineData(123456789UL)]
    public void RegionalItemsAreMissingAtHomeButFoundNextDoor(ulong seed)
    {
        // RS-003: coal forces expansion, so it must never be at home and always be one claim away.
        var sim = World(seed);
        var home = sim.Tiles[sim.StartTileId];
        Assert.Equal(0, home.Owner);
        Assert.False(Has(home, "coal"));
        Assert.True(Has(home, "copper_ore"));
        Assert.True(Has(home, "iron_ore"));
        Assert.Contains(sim.Neighbors(home), n => Has(n, "coal"));
        Assert.Contains(sim.Tiles, t => !Has(t, "coal"));
    }

    [Fact]
    public void TilesHaveUniquePlainNamesFromTheSeed()
    {
        var a = World(99, 12, 10);
        var names = a.Tiles.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // A bare word: no number, and no terrain name stuck on.
        Assert.All(names, n => Assert.Matches("^[A-Z][a-z]+$", n));
        Assert.Equal(names, World(99, 12, 10).Tiles.Select(t => t.Name));
        Assert.NotEqual(names, World(100, 12, 10).Tiles.Select(t => t.Name));
    }

    [Fact]
    public void SameSetupGivesSameWorld()
    {
        var a = World(99, 7, 5);
        var b = World(99, 7, 5);
        var c = World(100, 7, 5);
        // Terrain, borders and deposits never change, so the state hash leaves them out: compare them directly.
        static string Layout(Simulation s)
        {
            var map = new System.Text.StringBuilder();
            for (int y = 0; y < s.Map.Height; y += 3)
            {
                for (int x = 0; x < s.Map.Width; x += 3)
                    map.Append(s.Map.TerrainAt(x, y)).Append(':').Append(s.Map.TileAtCell(x, y)).Append(',');
            }
            foreach (var t in s.Tiles)
                map.Append(string.Join(" ", t.Neighbors)).Append('|').Append(string.Join(",", t.Grid.DepositCounts().Select(kv => $"{kv.Key}={kv.Value}")));
            return map.ToString();
        }
        Assert.Equal(Layout(a), Layout(b));
        Assert.NotEqual(Layout(a), Layout(c));
        Assert.Equal(a.StartTileId, b.StartTileId);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Theory]
    [InlineData(3, 3, 5UL)]
    [InlineData(5, 5, 3037UL)]
    [InlineData(7, 5, 42UL)]
    public void TilesAreIrregularConnectedAndAboutTheNominalCount(int w, int h, ulong seed)
    {
        var sim = World(seed, w, h);
        int nominal = w * h;
        Assert.InRange(sim.Tiles.Count, nominal * 6 / 10, nominal * 16 / 10);
        // Irregular: tiles differ in size and in how many neighbours they have.
        Assert.True(sim.Tiles.Select(t => t.Grid.Area).Distinct().Count() > sim.Tiles.Count / 2);
        Assert.True(sim.Tiles.Select(t => t.Neighbors.Count).Distinct().Count() > 2);

        long covered = 0;
        foreach (var t in sim.Tiles)
        {
            var cells = Cells(t).ToHashSet();
            covered += cells.Count;
            Assert.Equal(t.Grid.Area, cells.Count);
            Assert.All(cells, c => Assert.Equal(t.Id, sim.Map.TileAtCell(t.OriginX + c.X, t.OriginY + c.Y)));

            // Connected: a flood fill from any cell reaches all of them.
            var seen = new HashSet<(int, int)> { cells.First() };
            var queue = new Queue<(int X, int Y)>(seen);
            while (queue.TryDequeue(out var c))
            {
                foreach (var n in new[] { (c.X + 1, c.Y), (c.X - 1, c.Y), (c.X, c.Y + 1), (c.X, c.Y - 1) })
                {
                    if (cells.Contains(n) && seen.Add(n))
                        queue.Enqueue(n);
                }
            }
            Assert.Equal(cells.Count, seen.Count);

            Assert.NotEmpty(t.Neighbors);
            Assert.All(t.Neighbors, n => Assert.Contains(t.Id, sim.Tiles[n].Neighbors));
        }
        // Every cell of the world belongs to exactly one tile.
        Assert.Equal((long)sim.Map.Width * sim.Map.Height, covered);
    }

    [Theory]
    [InlineData(5, 5, 3037UL)]
    [InlineData(7, 7, 11UL)]
    public void TheWorldHasLandSeaAndCoast(int w, int h, ulong seed)
    {
        var sim = World(seed, w, h);
        var content = sim.Content;
        long water = 0, total = (long)sim.Map.Width * sim.Map.Height;
        for (int y = 0; y < sim.Map.Height; y++)
        {
            for (int x = 0; x < sim.Map.Width; x++)
            {
                if (content.TerrainList[sim.Map.TerrainAt(x, y)] is { Water: true, River: false })
                    water++;
            }
        }
        Assert.InRange(water * 100 / total, 35, 55);

        // Ocean tiles are ordinary tiles: some are (nearly) all water, others are coastal mixes.
        static int Wet(TileState t) => 100 - t.Grid.BuildableCount * 100 / t.Grid.Area;
        Assert.Contains(sim.Tiles, t => Wet(t) >= 90);
        Assert.Contains(sim.Tiles, t => Wet(t) is >= 20 and <= 80);
        // Several kinds of land, not one.
        Assert.True(sim.Tiles.Select(t => t.Biome).Distinct().Count() >= 3);
    }

    [Theory]
    [InlineData(5, 5, 3037UL)]
    [InlineData(4, 6, 9UL)]
    [InlineData(5, 5, 1UL)]
    [InlineData(5, 5, 2UL)]
    [InlineData(5, 5, 7UL)]
    [InlineData(6, 6, 12345UL)]
    [InlineData(8, 8, 99UL)]
    public void EachTileHasOnePortZonePerNeighbourFacingIt(int w, int h, ulong seed)
    {
        var sim = World(seed, w, h);
        foreach (var t in sim.Tiles)
        {
            var zones = Cells(t).Select(c => t.Grid.ZoneAt(c.X, c.Y)).Where(z => z >= 0).ToHashSet();
            Assert.Equal(t.Neighbors.Order(), zones.Order());

            foreach (var n in t.Neighbors)
            {
                // Every zone cell is within the zone's depth of the border it faces...
                var border = sim.Map.BorderCells(t.Id, n);
                foreach (var (x, y) in Cells(t).Where(c => t.Grid.ZoneAt(c.X, c.Y) == n))
                {
                    var (gx, gy) = t.ToGlobal(x, y);
                    Assert.Contains(border, b => Math.Abs(b.X - gx) + Math.Abs(b.Y - gy) < PortRules.ZoneDepth);
                }
                // ...and the border cells touching only that neighbour are in its zone.
                foreach (var (gx, gy) in border)
                {
                    var touching = new[] { (gx + 1, gy), (gx - 1, gy), (gx, gy + 1), (gx, gy - 1) }
                        .Select(c => sim.Map.TileAtCell(c.Item1, c.Item2))
                        .Where(id => id >= 0 && id != t.Id && t.Neighbors.Contains(id))
                        .Distinct().ToList();
                    if (touching.Count == 1)
                        Assert.Equal(n, t.Grid.ZoneAt(gx - t.OriginX, gy - t.OriginY));
                }
            }
        }
    }

    [Fact]
    public void DepositsFollowBuildableGroundAndDifferBetweenTiles()
    {
        var sim = World();
        foreach (var t in sim.Tiles)
        {
            Assert.All(Cells(t).Where(c => t.Grid.DepositAt(c.X, c.Y) != null), c => Assert.True(t.Grid.Buildable(c.X, c.Y)));
            if (t.Grid.BuildableCount > 0)
                Assert.NotEmpty(t.Grid.DepositCounts());
        }
        var layouts = sim.Tiles.Select(t => string.Join(",", t.Grid.DepositCounts().Select(kv => $"{kv.Key}{kv.Value}")));
        Assert.True(layouts.Distinct().Count() > sim.Tiles.Count / 2);
    }

    [Theory]
    [InlineData(3, 3, 1UL)]
    [InlineData(5, 5, 3037UL)]
    [InlineData(9, 9, 77UL)]
    public void TheStartIsLandWithRoomToGrow(int w, int h, ulong seed)
    {
        var sim = World(seed, w, h);
        var home = sim.Tiles[sim.StartTileId];
        Assert.True(home.Grid.BuildableCount * 100 >= home.Grid.Area * 60);
        Assert.True(sim.Neighbors(home).Count(n => n.Grid.BuildableCount * 100 >= n.Grid.Area * 30) >= 2);
        var core = Assert.Single(home.Grid.Buildings);
        Assert.Equal("core", core.DefId);
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

    [Theory]
    [InlineData(1UL)]
    [InlineData(3037UL)]
    [InlineData(123456789UL)]
    public void EachPlayerGetsTheirOwnStartDeterministically(ulong seed)
    {
        var a = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, seed, Players: 2));
        var b = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, seed, Players: 2));
        Assert.Equal(2, a.StartTileIds.Count);
        Assert.NotEqual(a.StartTileIds[0], a.StartTileIds[1]);
        Assert.Equal(a.StartTileIds, b.StartTileIds);
        Assert.Equal(a.StateHash(), b.StateHash());
        // Player 0 starts where a single-player game would.
        Assert.Equal(World(seed).StartTileId, a.StartTileId);
        for (int p = 0; p < 2; p++)
        {
            var home = a.Tiles[a.StartTileIds[p]];
            Assert.Equal(p, home.Owner);
            Assert.Equal(p, home.Grid.Buildings.Single(x => x.DefId == Simulation.CoreBuildingId).Owner);
            Assert.False(Has(home, "coal"));
        }
        Assert.DoesNotContain(a.StartTileIds[1], a.Tiles[a.StartTileIds[0]].Neighbors);
        Assert.Equal(2, a.Players.Count);
    }
}

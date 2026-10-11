using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// A host's booster bonuses (FB-016), each summed over the boosters it counts. Percent stats are added to 100%
/// (<see cref="Speed"/> 25 = 125% speed); <see cref="Radius"/> and <see cref="BlockCap"/> are added to the host's
/// own base values.
/// </summary>
public readonly record struct BoostStats(
    int Speed, int Output, int Power, int BuildSpeed, int InputBuffer,
    int Radius, int BlockCap, int InputSides, bool Precision, bool Cooling)
{
    public static BoostStats Base => default;

    /// <summary>A stat by its JSON name (see <see cref="BoosterDef.Stats"/>); flags are 1 or 0.</summary>
    public int Get(string stat) => stat switch
    {
        "speed" => Speed,
        "output" => Output,
        "power" => Power,
        "build_speed" => BuildSpeed,
        "input_buffer" => InputBuffer,
        "radius" => Radius,
        "block_cap" => BlockCap,
        "input_sides" => InputSides,
        "precision" => Precision ? 1 : 0,
        "cooling" => Cooling ? 1 : 0,
        _ => throw new ArgumentException($"unknown booster stat '{stat}'", nameof(stat)),
    };

    internal BoostStats Add(string stat, int n) => stat switch
    {
        "speed" => this with { Speed = Speed + n },
        "output" => this with { Output = Output + n },
        "power" => this with { Power = Power + n },
        "build_speed" => this with { BuildSpeed = BuildSpeed + n },
        "input_buffer" => this with { InputBuffer = InputBuffer + n },
        "radius" => this with { Radius = Radius + n },
        "block_cap" => this with { BlockCap = BlockCap + n },
        "input_sides" => this with { InputSides = InputSides + n },
        "precision" => this with { Precision = Precision || n > 0 },
        "cooling" => this with { Cooling = Cooling || n > 0 },
        _ => this,
    };

    /// <summary>A base value scaled by a percent bonus, never below zero: <c>Scaled(30, Power)</c> is the boosted draw.</summary>
    public static int Scaled(int value, int bonusPercent) => Math.Max(0, value * (100 + bonusPercent) / 100);
}

/// <summary>
/// Booster rules (FB-016, OQ-25). Read-only queries shared by the sim, the hover panel and unit factories:
/// <list type="bullet">
/// <item>A booster boosts every host it touches whose <see cref="BuildingDef.Category"/> it lists, up to its
/// <see cref="BoosterDef.MaxHosts"/> (lowest host ids first).</item>
/// <item>A host counts at most <see cref="BoosterDef.MaxPerHost"/> boosters of one type, lowest ids first.</item>
/// <item>Chain boosters (Gantry Rail) count only in a straight line running outward from the host: the first touches
/// the host end-on, each next one continues the line end to end.</item>
/// <item>A booster with <see cref="BoosterDef.HalfWithout"/> gives half unless it is itself boosted with that stat;
/// a booster that draws power gives its effect scaled by its network's satisfaction.</item>
/// </list>
/// </summary>
public static class Boosters
{
    /// <summary>The host's boosted stats right now.</summary>
    public static BoostStats StatsOf(Simulation sim, TileState tile, PlacedBuilding host) => Stats(tile.Grid, host, 0);

    /// <summary>
    /// One stat's additive bonus (see <see cref="BoostStats.Get"/>): <c>Bonus(sim, tile, factory, "radius")</c> is the
    /// cells to add to the factory's base radius.
    /// </summary>
    public static int Bonus(Simulation sim, TileState tile, PlacedBuilding host, string stat) =>
        StatsOf(sim, tile, host).Get(stat);

    /// <summary>The boosters the host counts, in id order.</summary>
    public static IReadOnlyList<PlacedBuilding> Of(Simulation sim, TileState tile, PlacedBuilding host) =>
        Counted(tile.Grid, host);

    // Servo Bank needs its own stats (cooling); a deeper booster-of-booster chain stops here.
    const int MaxDepth = 3;

    internal static BoostStats Stats(TileGrid grid, PlacedBuilding host, int depth)
    {
        var stats = BoostStats.Base;
        if (host.Def.Category == null || depth > MaxDepth)
            return stats;
        foreach (var b in Counted(grid, host))
        {
            var def = b.Def.Booster!;
            int percent = 100;
            if (def.HalfWithout is { } need && Stats(grid, b, depth + 1).Get(need) == 0)
                percent = 50;
            long scale = (long)percent * (b.Def.Power > 0 ? grid.Power.Satisfaction(b) : 1000);
            foreach (var (stat, n) in def.Effects)
                stats = stats.Add(stat, n > 0 && stat is "precision" or "cooling" ? n : (int)(n * scale / 100_000));
        }
        return stats;
    }

    static List<PlacedBuilding> Counted(TileGrid grid, PlacedBuilding host)
    {
        var counted = new List<PlacedBuilding>();
        if (host.Def.Category is not { } category)
            return counted;

        foreach (var b in grid.Touching(host))
        {
            if (b.Def.Booster is { Chain: false } def && def.Hosts.Contains(category) && Serves(grid, b, host, category))
                counted.Add(b);
        }
        counted.AddRange(Chains(grid, host, category));
        counted.Sort((p, q) => p.Id.CompareTo(q.Id));

        // Per-type cap, lowest ids first.
        var perType = new Dictionary<string, int>(StringComparer.Ordinal);
        return counted.Where(b =>
        {
            int n = perType[b.DefId] = perType.GetValueOrDefault(b.DefId) + 1;
            return b.Def.Booster!.MaxPerHost == 0 || n <= b.Def.Booster.MaxPerHost;
        }).ToList();
    }

    /// <summary>A booster with a host limit serves only its first hosts, by id.</summary>
    static bool Serves(TileGrid grid, PlacedBuilding booster, PlacedBuilding host, string category)
    {
        int max = booster.Def.Booster!.MaxHosts;
        if (max == 0)
            return true;
        var hosts = grid.Touching(booster).Where(h => h.Def.Category is { } c && booster.Def.Booster.Hosts.Contains(c));
        return hosts.Take(max).Contains(host);
    }

    /// <summary>Chain boosters in straight lines running outward from each side of the host.</summary>
    static IEnumerable<PlacedBuilding> Chains(TileGrid grid, PlacedBuilding host, string category)
    {
        foreach (var dir in new[] { Direction.East, Direction.South, Direction.West, Direction.North })
        {
            int dx = dir.Dx(), dy = dir.Dy();
            bool vertical = dy != 0;
            // Cells just outside this side of the host.
            int n = vertical ? host.Width : host.Height;
            var seen = new List<PlacedBuilding>();
            for (int i = 0; i < n; i++)
            {
                int x = vertical ? host.X + i : dx > 0 ? host.X + host.Width : host.X - 1;
                int y = vertical ? (dy > 0 ? host.Y + host.Height : host.Y - 1) : host.Y + i;
                if (grid.BuildingAt(x, y) is not { } first || seen.Contains(first)
                    || first.Def.Booster is not { Chain: true } def || !def.Hosts.Contains(category)
                    || !Lengthwise(first, vertical))
                    continue;
                seen.Add(first);
                for (var link = first; link != null; link = Next(grid, link, dx, dy, vertical))
                    yield return link;
            }
        }
    }

    /// <summary>Its long side runs along the chain's direction.</summary>
    static bool Lengthwise(PlacedBuilding b, bool vertical) => vertical ? b.Height > b.Width : b.Width > b.Height;

    /// <summary>The same booster continuing the line end to end beyond <paramref name="b"/>, or null.</summary>
    static PlacedBuilding? Next(TileGrid grid, PlacedBuilding b, int dx, int dy, bool vertical)
    {
        int x = dx > 0 ? b.X + b.Width : dx < 0 ? b.X - 1 : b.X;
        int y = dy > 0 ? b.Y + b.Height : dy < 0 ? b.Y - 1 : b.Y;
        if (grid.BuildingAt(x, y) is not { } next || next.DefId != b.DefId || !Lengthwise(next, vertical))
            return null;
        // Lined up exactly, starting right at the end of b.
        bool aligned = vertical
            ? next.X == b.X && (dy > 0 ? next.Y == y : next.Y + next.Height - 1 == y)
            : next.Y == b.Y && (dx > 0 ? next.X == x : next.X + next.Width - 1 == x);
        return aligned ? next : null;
    }
}

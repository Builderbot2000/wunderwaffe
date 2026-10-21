using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Claiming tiles by victory circle (WM-006, CB-003). Each tick, every player with a building in a tile's circle who
/// doesn't own the tile counts up; a player with none there drops back to zero. The first to reach
/// <see cref="CaptureTicks"/> (lowest id on a tie) takes the tile, and every other player's building in the circle is
/// destroyed. Buildings elsewhere on the tile keep their owner. Capturing an enemy tile is the same rule, which in
/// practice means destroying their buildings in the circle first, since nobody may build or land in an enemy-owned
/// circle while one stands (WM-015).
/// </summary>
public static class Expansion
{
    /// <summary>Ticks a player must hold a building in a circle to claim the tile: the match's capture time (MP-005).</summary>
    public static int CaptureTicks(Simulation sim) => (sim.Setup?.CaptureSeconds ?? Balance.CaptureSeconds) * Simulation.TicksPerSecond;

    /// <summary>Players with a building in the tile's circle, ascending. Frames and platforms belong to nobody.</summary>
    public static IReadOnlyList<int> CircleOccupants(TileState tile)
    {
        var grid = tile.Grid;
        if (tile.CircleCache.Revision == grid.Revision)
            return tile.CircleCache.Players;

        // By building, not by cell: a 32-cell circle (OQ-35) is ~3,300 cells, rescanned whenever the grid changes.
        var players = new SortedSet<int>();
        foreach (var b in grid.Buildings)
        {
            if (b.Owner >= 0 && InCircle(tile, b))
                players.Add(b.Owner);
        }
        tile.CircleCache = (grid.Revision, [.. players]);
        return tile.CircleCache.Players;
    }

    /// <summary>Whether a building has any cell in its tile's circle.</summary>
    public static bool InCircle(TileState tile, PlacedBuilding b) => tile.InCircle(b.X, b.Y, b.Width, b.Height);

    /// <summary>Claim progress of <paramref name="player"/> on the tile, 0 to <see cref="CaptureTicks"/>.</summary>
    public static int Progress(TileState tile, int player) => tile.ClaimTicks.GetValueOrDefault(player);

    /// <summary>Phase 2 of the tick: advances every tile's claim timers and hands over tiles whose timer finished.</summary>
    internal static void Step(Simulation sim)
    {
        int capture = CaptureTicks(sim);
        foreach (var tile in sim.Tiles)
        {
            var occupants = CircleOccupants(tile);
            if (occupants.Count == 0 && tile.Claims.Count == 0)
                continue;

            int winner = TileState.Unowned;
            foreach (int p in occupants)
            {
                if (p == tile.Owner || sim.IsNeutral(p)) // neutral sites never claim (WM-010)
                    continue;
                int ticks = tile.Claims.GetValueOrDefault(p) + 1;
                tile.Claims[p] = ticks;
                if (ticks >= capture && winner == TileState.Unowned)
                    winner = p;
            }
            foreach (int p in tile.Claims.Keys.Where(p => p == tile.Owner || !occupants.Contains(p)).ToList())
                tile.Claims.Remove(p);

            if (winner != TileState.Unowned)
                Claim(sim, tile, winner);
        }
    }

    /// <summary>Hands the tile to <paramref name="player"/>, destroys everyone else's buildings in the circle and resets the timers.</summary>
    internal static void Claim(Simulation sim, TileState tile, int player)
    {
        int previous = tile.Owner;
        tile.Owner = player;
        sim.Player(player);
        tile.Claims.Clear();
        var rivals = tile.Grid.Buildings.Where(b => b.Owner != player && InCircle(tile, b)).ToList();
        foreach (var b in rivals)
            Territory.DestroyBuilding(sim, tile, b);
        sim.Raise(GameEventKind.TileClaimed, player, tile.Id);
        if (previous != TileState.Unowned)
            sim.Raise(GameEventKind.TileLost, previous, tile.Id);
    }
}

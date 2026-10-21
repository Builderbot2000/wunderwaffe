using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// The NF-001 benchmark world, shared by <c>StressTests</c> and the game's <c>-- --stress</c>: <see cref="FullTiles"/>
/// hand-built tiles of <see cref="BuildingsPerTile"/> producers, each viewed by its own player so it runs the full sim,
/// and <see cref="RateColumns"/> x <see cref="RateRows"/> small unviewed factories in rate mode (FB-012), half of them
/// shipping ore to the other half.
/// </summary>
public static class StressScene
{
    public const int FullTiles = 8;
    public const int BuildingsPerTile = 2000;
    public const int RateColumns = 20, RateRows = 10;
    const int SmallSize = 16;

    /// <summary>A built scene; every placement has been applied (the sim has stepped past it).</summary>
    public sealed record Scene(Simulation Sim, IReadOnlyList<TileState> Full, IReadOnlyList<TileState> Rate);

    /// <summary>Builds the scene. Player 0 owns every tile; players 0..7 each view one full tile.</summary>
    public static Scene Build(ContentDb content)
    {
        var sim = new Simulation(content) { RateModeEnabled = true };
        var full = new List<TileState>();
        for (int t = 0; t < FullTiles; t++)
        {
            var tile = sim.AddStartingTile(t, 0, owner: 0, seed: (ulong)t);
            var core = tile.Grid.Cores[0].Stock!;
            core.Add("copper_ore", 1_000_000);
            core.Add("iron_ore", 1_000_000);
            full.Add(tile);
        }
        var rate = new List<TileState>();
        int top = Simulation.DefaultTileSize / SmallSize;
        for (int r = 0; r < RateRows; r++)
        {
            for (int c = 0; c < RateColumns; c++)
            {
                var tile = sim.AddTile(c, top + r, owner: 0, size: SmallSize);
                var core = sim.AddCore(tile, 0, 6, 6, "core_large").Stock!;
                core.Add("copper_ore", 1_000);
                core.Add("iron_ore", 1_000);
                rate.Add(tile);
            }
        }
        for (int i = 0; i < rate.Count; i++)
            SmallFactory(sim, rate[i], exporter: i % 2 == 0);
        for (int p = 0; p < FullTiles; p++)
            sim.Enqueue(new SetViewedTileCommand(p, full[p].Id));
        sim.Step();
        foreach (var tile in full)
            Fill(sim, tile);
        sim.Step();
        return new Scene(sim, full, rate);
    }

    /// <summary>
    /// Queues <see cref="BuildingsPerTile"/> 2 x 2 producers on the tile: drills on ore, iron smelters elsewhere. The game
    /// passes its own <paramref name="send"/> (lockstep); default <see cref="Simulation.Enqueue"/>. Building needs a build
    /// radius (WM-015), so it fills outward from the player's first radius, nearest first, and puts a core on any spot in
    /// reach but at least two thirds of a core's radius from every radius so far; the queued cores extend the reach of
    /// the spots after them (commands apply in order).
    /// </summary>
    public static void Fill(Simulation sim, TileState tile, int player = 0, Action<Command>? send = null)
    {
        send ??= sim.Enqueue;
        var g = tile.Grid;
        var core = sim.Content.DefaultCore!;
        int spacing = core.BuildRadius * 2 / 3;
        var radii = Territory.BuildRadii(sim, player, tile);
        var (cx, cy) = radii.Count > 0 ? (radii[0].X, radii[0].Y) : (g.Width / 2, g.Height / 2);
        var spots = new List<(int X, int Y)>();
        for (int y = 0; y < g.Height - 1; y += 2)
        {
            for (int x = 0; x < g.Width - 1; x += 2)
            {
                if (g.IsFree(x, y, 2, 2))
                    spots.Add((x, y));
            }
        }
        int placed = 0;
        foreach (var (x, y) in spots.OrderBy(p => (long)(p.X - cx) * (p.X - cx) + (long)(p.Y - cy) * (p.Y - cy)).ThenBy(p => p.Y).ThenBy(p => p.X))
        {
            if (placed >= BuildingsPerTile)
                break;
            if (!Territory.Covered(radii, x, y, 2, 2))
                continue;
            placed++;
            if (!radii.Any(r => (r with { Radius = spacing }).Covers(x, y))
                && Placement.Check(sim, tile, player, core.Id, x, y, 0, null, out _, ignoreCost: true, ignoreRadius: true) == PlaceResult.Ok)
            {
                send(new PlaceBuildingCommand(player, tile.Id, core.Id, x, y));
                radii.Add(new BuildRadius(x, y, core.Width, core.Height, core.BuildRadius, -1, -1));
                continue;
            }
            bool drill = Placement.Check(sim, tile, player, "drill", x, y, 0, null, out _, ignoreRadius: true) == PlaceResult.Ok;
            send(drill
                ? new PlaceBuildingCommand(player, tile.Id, "drill", x, y)
                : new PlaceBuildingCommand(player, tile.Id, "smelter", x, y, RecipeId: "smelt_iron"));
        }
    }

    /// <summary>
    /// A small factory on a 16-cell tile: copper drill into the core, iron drill through a smelter into it, and either an
    /// iron drill wired to the core's power and shipping east (exporters) or an import port belting into the core.
    /// </summary>
    static void SmallFactory(Simulation sim, TileState t, bool exporter)
    {
        Ore(t, "copper_ore", 9, 6);
        Ore(t, "iron_ore", 6, 11);
        Place(sim, t, "drill", 9, 6);
        Place(sim, t, "smelter", 6, 9, "smelt_iron");
        Place(sim, t, "drill", 6, 11);
        if (exporter)
        {
            Ore(t, "iron_ore", 11, 4);
            Place(sim, t, "drill", 11, 4);
            Place(sim, t, "export_port", 13, 4);
            Place(sim, t, "wire", 10, 5);              // Joins it to the copper drill, so to the core's power.
        }
        else
        {
            Place(sim, t, "import_port", 2, 7);
            for (int x = 3; x < 6; x++)
                Place(sim, t, "conveyor", x, 7);
        }
    }

    static void Ore(TileState tile, string item, int x, int y)
    {
        for (int cy = y; cy < y + 2; cy++)
            for (int cx = x; cx < x + 2; cx++)
                tile.Grid.SetDeposit(cx, cy, item);
    }

    static void Place(Simulation sim, TileState t, string building, int x, int y, string? recipe = null) =>
        sim.Enqueue(new PlaceBuildingCommand(0, t.Id, building, x, y, 0, recipe));
}

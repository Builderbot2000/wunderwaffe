using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;

using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// Plays the whole goal chain with real content, starting items and costs, using only the commands a player has
/// (no debug grants). If a content change makes the demo loop impossible, this fails.
/// </summary>
public class DemoLoopTests(ITestOutputHelper output)
{
    const int Me = 0;
    const int East = 0, West = 2;

    static void Ore(TileState tile, string item, int x, int y)
    {
        for (int cy = y; cy < y + 2; cy++)
            for (int cx = x; cx < x + 2; cx++)
                tile.Grid.SetDeposit(cx, cy, item);
    }

    [Fact]
    public void EveryGoalCanBeReachedWithoutCheats()
    {
        // Home (0,0) has copper and iron; its east neighbour (1,0) has coal. Deposits are laid by hand so the layout
        // can be fixed: machines next to each other pass items directly, which keeps the factory small.
        var sim = new Simulation(TestContent.Load());
        var home = sim.AddTile(0, 0, size: 96);
        var coal = sim.AddTile(1, 0, size: 96);
        // The starting unit lands in the circle: thruster (45, 46), core (46..47, 46..47), thruster (48, 46), on frame
        // (45..48, 46..47). Its core holds the starting items, which pay for everything until it lifts off (FB-019).
        sim.SettleStartingTile(home, Me);
        Ore(home, "copper_ore", 62, 46);    // East of the base core: copper ore straight in.
        Ore(home, "iron_ore", 60, 43);      // North: iron drill -> smelter -> core.
        Ore(home, "iron_ore", 58, 47);      // West: iron ore straight in, for buildings that cost ore.
        Ore(home, "copper_ore", 60, 51);    // South: copper drill -> smelter -> core.
        Ore(home, "iron_ore", 92, 51);      // East edge: plates for the steel mill.
        Ore(home, "copper_ore", 94, 42);    // East edge: copper for the fuel press that powers the mill.
        Ore(coal, "coal", 1, 46);

        void Do(Command c) => sim.Enqueue(c);
        void Place(TileState t, string b, int x, int y, int rot = 0, string? recipe = null) =>
            Do(new PlaceBuildingCommand(Me, t.Id, b, x, y, rot, recipe));
        bool RunUntil(Func<bool> done, int maxSeconds)
        {
            for (int i = 0; i < maxSeconds * Simulation.TicksPerSecond && !done(); i++)
                sim.Step();
            return done();
        }
        var me = () => sim.PlayerOrNull(Me)!;
        var scoutCore = home.Grid.BuildingAt(46, 46)!;
        Assert.Equal("core", scoutCore.DefId);
        Assert.Equal(StartItems(sim)["copper_ore"], scoutCore.Stock!.Get("copper_ore"));

        // Goals 1-2: a base core off the frame, so home keeps funds once the scout leaves, and copper ore and iron
        // plates into it. The scout's core pays first (lowest id, OQ-38).
        Place(home, "core", 60, 47);         // (60..61, 47..48)
        Place(home, "drill", 62, 46);
        Place(home, "drill", 60, 43);
        Place(home, "smelter", 60, 45, recipe: "smelt_iron");
        Place(home, "drill", 58, 47);
        Place(home, "drill", 60, 51);
        Place(home, "smelter", 60, 49, recipe: "smelt_copper");
        sim.Step();
        Assert.Equal(7 + 3, home.Grid.BuildingCount); // and the starting unit
        var baseCore = home.Grid.BuildingAt(60, 47)!;
        long carried = scoutCore.Stock!.Get("copper_ore");
        Assert.True(carried >= 100, $"the scout keeps {carried} copper ore");

        // Goal 3: lift the starting unit off; its core's stock goes with it as cargo.
        Do(new ActivateUnitCommand(Me, home.Id, 46, 46));
        sim.Step();
        var scout = Assert.Single(sim.Units);
        Assert.Equal(carried, scout.Cargo.Get("copper_ore"));
        Assert.Equal([baseCore], Funds.Cores(home, Me));

        // Goal 4: fly it into the coal tile's victory circle, land, and hold the circle until the tile is ours.
        Do(new MoveUnitsCommand(Me, [scout.Id], coal.Id, coal.CircleX, coal.CircleY));
        sim.Step();
        Assert.True(RunUntil(() => !scout.Moving, 120), $"the scout never arrived: at {scout.X / Unit.CellScale},{scout.Y / Unit.CellScale} tile {scout.TileId} speed {scout.MaxSpeed} thrust {scout.TotalThrust} mass {scout.Mass} unsupplied {scout.Unsupplied}");
        Assert.Equal(coal.Id, scout.TileId);
        Do(new DeployUnitCommand(Me, scout.Id));
        sim.Step();
        Assert.Empty(sim.Units);
        Assert.Equal(3, coal.Grid.BuildingCount);
        var landed = Assert.Single(coal.Grid.Cores);
        Assert.Equal(carried, landed.Stock!.Get("copper_ore"));
        Assert.True(RunUntil(() => coal.Owner == Me, Balance.CaptureSeconds + 1), "the coal tile was never claimed");

        // Goal 5: ship coal home through a port pair in lane 47, paid from what the landed core brought. The landed core's
        // build radius (WM-015) doesn't reach the west edge: a second core on the way does.
        Place(coal, "core", landed.X - 24, landed.Y);
        Place(coal, "drill", 1, 46);
        Place(coal, "export_port", 0, 47, West);
        // The landed core powers the tile (FB-005). Wire the coal drill to the cell under the core: down column 1,
        // along row 55, then up the core's column (the scout landed upright, two rows high, clear of row 55).
        var wires = new List<(int X, int Y)>();
        for (int y = 48; y <= 55; y++)
            wires.Add((1, y));
        for (int x = 2; x <= landed.X; x++)
            wires.Add((x, 55));
        for (int y = 54; y >= landed.Y + landed.Height; y--)
            wires.Add((landed.X, y));
        foreach (var (x, y) in wires)
            Place(coal, "wire", x, y);
        // A third core at home reaches the east edge, where the port and the steel mill go.
        Place(home, "core", 76, 49);
        Place(home, "import_port", 95, 47, East);
        // A belt from beside the future steel mill back to the base core carries steel (and some copper ore) home.
        for (int x = 62; x <= 91; x++)
            Place(home, "conveyor", x, 48, West);
        sim.Step();
        Assert.Equal(6, coal.Grid.BuildingCount);
        Assert.Equal(wires.Count, coal.Grid.CountOn(BuildingKind.Wire));
        Assert.Same(coal.Grid.Power.NetworkOf(landed), coal.Grid.Power.NetworkOf(coal.Grid.BuildingAt(1, 46)!));

        // Goal 6: the steel mill, once affordable; it needs plates from its own smelter and coal from the port. It
        // draws more than a core gives and stands far from it, so a fuel generator on top of it powers the corner:
        // copper drill -> fuel press -> generator, all touching (FB-005). A wire along row 46 from the base core's
        // east drill to the mill lets core power start the fuel line, which then carries the mill.
        string[] corner = ["steel_mill", "smelter", "drill", "drill", "fuel_press", "fuel_generator"];
        bool CanBuildCorner() => corner.SelectMany(b => sim.Content.Buildings[b].Cost)
            .GroupBy(kv => kv.Key).All(g => Items(home, g.Key) >= g.Sum(kv => kv.Value) + (g.Key == "copper_ore" ? 28 : 0));
        Assert.True(RunUntil(CanBuildCorner, 900),
            $"steel mill not reached: {me().ProducedOf("iron_plate")} plates, inventory {Inv(home)}");
        Place(home, "steel_mill", 92, 46);
        Place(home, "smelter", 92, 49, recipe: "smelt_iron");
        Place(home, "drill", 92, 51);
        Place(home, "fuel_generator", 92, 44);
        Place(home, "fuel_press", 94, 44, recipe: "press_fuel");
        Place(home, "drill", 94, 42);
        for (int x = 64; x <= 91; x++)
            Place(home, "wire", x, 46);
        sim.Step();
        Assert.Equal("steel_mill", home.Grid.BuildingAt(92, 46)?.DefId);
        Assert.Equal("fuel_press", home.Grid.BuildingAt(94, 44)?.DefId);

        // Goal 7: a core on frame, lifted off once the steel goal is met and the core is affordable.
        var unitCost = sim.Content.Buildings["core"].Cost.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (id, n) in sim.Content.Frame!.Cost)
            unitCost[id] = unitCost.GetValueOrDefault(id) + 6 * n;
        Assert.True(RunUntil(() => me().ProducedOf("steel") >= 20 && Funds.CanPay(home, Me, unitCost), 900),
            $"unit not reached: mill {home.Grid.BuildingAt(92, 46)!.Machine!.Status} {home.Grid.BuildingAt(92, 46)!.Machine!.MissingInput}, gen {home.Grid.BuildingAt(92, 44)!.Machine!.Status}, press {home.Grid.BuildingAt(94, 44)!.Machine!.Status} {home.Grid.BuildingAt(94, 44)!.Machine!.PowerPermille}, nets {string.Join(";", home.Grid.Power.Networks.Select(n => $"{n.Members.Count}:{n.Supply}/{n.Demand}"))}; {me().ProducedOf("steel")} steel, shipped {me().Shipped}, inventory {Inv(home)}");
        for (int y = 38; y <= 39; y++)
            for (int x = 70; x <= 72; x++)
                Do(new PlaceFrameCommand(Me, home.Id, x, y));
        Place(home, "core", 70, 38);
        sim.Step();
        // Lift-off needs a way to move (UD-003): one thruster.
        var thrusterCost = sim.Content.Buildings["thruster"].Cost;
        Assert.True(RunUntil(() => Funds.CanPay(home, Me, thrusterCost), 900), $"thruster not reached: inventory {Inv(home)}");
        Place(home, "thruster", 72, 38);
        sim.Step();
        Do(new ActivateUnitCommand(Me, home.Id, 70, 38));
        sim.Step();

        Assert.Equal(sim.Content.Goals.Count, me().GoalsDone);
        var times = me().GoalTicks.Select((t, i) => $"{sim.Content.Goals[i].Id} at {t / Simulation.TicksPerSecond / 60}:{t / Simulation.TicksPerSecond % 60:00}");
        output.WriteLine(string.Join(", ", times));
    }

    static string Inv(TileState t) => string.Join(", ", Funds.Total(t, Me).Select(kv => $"{kv.Key} {kv.Value}"));
}

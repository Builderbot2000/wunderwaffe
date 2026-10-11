using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;

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
        var home = sim.AddTile(0, 0);
        var coal = sim.AddTile(1, 0);
        sim.SettleStartingTile(home, Me);   // Core at (46..48, 46..48).
        Ore(home, "copper_ore", 49, 46);    // East of the core: copper ore straight in.
        Ore(home, "iron_ore", 46, 42);      // North: iron drill -> smelter -> core.
        Ore(home, "iron_ore", 42, 46);      // West: the same.
        Ore(home, "copper_ore", 46, 51);    // South: copper drill -> smelter -> core.
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

        // Goals 1-2: copper ore and iron plates into the core.
        Place(home, "drill", 49, 46);
        Place(home, "drill", 46, 42);
        Place(home, "smelter", 46, 44, recipe: "smelt_iron");
        Place(home, "drill", 42, 46);
        Place(home, "smelter", 44, 46, recipe: "smelt_iron");
        Place(home, "drill", 46, 51);
        Place(home, "smelter", 46, 49, recipe: "smelt_copper");
        sim.Step();
        Assert.Equal(8 + 3, home.Grid.BuildingCount); // and the starting scout

        // Goal 3: lift the starting scout off (unit core at (47, 38), above the core).
        Assert.Equal("unit_core", home.Grid.BuildingAt(47, 38)?.DefId);
        Do(new ActivateUnitCommand(Me, home.Id, 47, 38));
        sim.Step();
        var scout = Assert.Single(sim.Units);

        // Goal 4: fly it into the coal tile's victory circle, land, and hold the circle until the tile is ours.
        Do(new MoveUnitsCommand(Me, [scout.Id], coal.Id, coal.CircleX, coal.CircleY));
        sim.Step();
        Assert.True(RunUntil(() => !scout.Moving, 120), "the scout never arrived");
        Assert.Equal(coal.Id, scout.TileId);
        Do(new DeployUnitCommand(Me, scout.Id));
        sim.Step();
        Assert.Empty(sim.Units);
        Assert.Equal(3, coal.Grid.BuildingCount);
        Assert.True(RunUntil(() => coal.Owner == Me, Balance.CaptureSeconds + 1), "the coal tile was never claimed");

        // Goal 5: ship coal home through a port pair in lane 47. The coal tile has no stock, so home pays.
        Place(coal, "drill", 1, 46);
        Place(coal, "export_port", 0, 47, West);
        // The claimed tile has no core: the landed scout's unit core powers it (FB-005). Wire the coal drill to the
        // cell west of it, along row 46 and then along that column (the scout landed upright, clear of it).
        var unitCore = coal.Grid.Buildings.Single(b => b.DefId == "unit_core");
        var wires = new List<(int X, int Y)>();
        int wx = unitCore.X - 1;
        for (int x = 3; x <= wx; x++)
            wires.Add((x, 46));
        for (int y = 46; y != unitCore.Y; y += Math.Sign(unitCore.Y - 46))
            wires.Add((wx, y + Math.Sign(unitCore.Y - 46)));
        foreach (var (x, y) in wires)
            Place(coal, "wire", x, y);
        Place(home, "import_port", 95, 47, East);
        // A belt from beside the future steel mill back to the core carries steel (and some copper ore) home.
        for (int x = 49; x <= 91; x++)
            Place(home, "conveyor", x, 48, West);
        sim.Step();
        Assert.Equal(5 + wires.Count, coal.Grid.BuildingCount);
        Assert.Same(coal.Grid.Power.NetworkOf(unitCore), coal.Grid.Power.NetworkOf(coal.Grid.BuildingAt(1, 46)!));

        // Goal 6: the steel mill, once affordable; it needs plates from its own smelter and coal from the port. It
        // draws more than a core gives and stands far from it, so a fuel generator on top of it powers the corner:
        // copper drill -> fuel press -> generator, all touching (FB-005). A wire along row 47 from the core's east
        // drill to the mill lets core power start the fuel line, which then carries the mill.
        string[] corner = ["steel_mill", "smelter", "drill", "drill", "fuel_press", "fuel_generator"];
        bool CanBuildCorner() => corner.SelectMany(b => sim.Content.Buildings[b].Cost)
            .GroupBy(kv => kv.Key).All(g => home.Inventory.Get(g.Key) >= g.Sum(kv => kv.Value) + (g.Key == "copper_ore" ? 41 : 0));
        Assert.True(RunUntil(CanBuildCorner, 900),
            $"steel mill not reached: {me().ProducedOf("iron_plate")} plates, inventory {Inv(home)}");
        Place(home, "steel_mill", 92, 46);
        Place(home, "smelter", 92, 49, recipe: "smelt_iron");
        Place(home, "drill", 92, 51);
        Place(home, "fuel_generator", 92, 44);
        Place(home, "fuel_press", 94, 44, recipe: "press_fuel");
        Place(home, "drill", 94, 42);
        for (int x = 51; x <= 91; x++)
            Place(home, "wire", x, 47);
        sim.Step();
        Assert.Equal("steel_mill", home.Grid.BuildingAt(92, 46)?.DefId);
        Assert.Equal("fuel_press", home.Grid.BuildingAt(94, 44)?.DefId);

        // Goal 7: a unit core on frame, lifted off once the steel goal is met and the core is affordable.
        var unitCost = sim.Content.Buildings["unit_core"].Cost;
        Assert.True(RunUntil(() => me().ProducedOf("steel") >= 20 && home.Inventory.Has(unitCost), 900),
            $"unit core not reached: mill {home.Grid.BuildingAt(92, 46)!.Machine!.Status} {home.Grid.BuildingAt(92, 46)!.Machine!.MissingInput}, gen {home.Grid.BuildingAt(92, 44)!.Machine!.Status}, press {home.Grid.BuildingAt(94, 44)!.Machine!.Status} {home.Grid.BuildingAt(94, 44)!.Machine!.PowerPermille}, nets {string.Join(";", home.Grid.Power.Networks.Select(n => $"{n.Members.Count}:{n.Supply}/{n.Demand}"))}; {me().ProducedOf("steel")} steel, shipped {me().Shipped}, inventory {Inv(home)}");
        for (int x = 60; x <= 62; x++)
            Do(new PlaceFrameCommand(Me, home.Id, x, 40));
        Place(home, "unit_core", 61, 40);
        sim.Step();
        // Lift-off needs a way to move (UD-003): one thruster.
        var thrusterCost = sim.Content.Buildings["thruster"].Cost;
        Assert.True(RunUntil(() => home.Inventory.Has(thrusterCost), 900), $"thruster not reached: inventory {Inv(home)}");
        Place(home, "thruster", 62, 40);
        sim.Step();
        Do(new ActivateUnitCommand(Me, home.Id, 61, 40));
        sim.Step();

        Assert.Equal(sim.Content.Goals.Count, me().GoalsDone);
        var times = me().GoalTicks.Select((t, i) => $"{sim.Content.Goals[i].Id} at {t / Simulation.TicksPerSecond / 60}:{t / Simulation.TicksPerSecond % 60:00}");
        output.WriteLine(string.Join(", ", times));
    }

    static string Inv(TileState t) => string.Join(", ", t.Inventory.Entries.Select(kv => $"{kv.Key} {kv.Value}"));
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Wunderwaffe.Sim.Api;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// NF-009: an external program can play the demo loop end to end through <see cref="JsonDriver"/> alone, sending JSON
/// text and reading JSON text back, with no access to the sim's objects.
/// </summary>
public class JsonDriverTests(ITestOutputHelper output)
{
    /// <summary>A client that only ever sees strings, like a program on the other end of stdin/stdout.</summary>
    sealed class Client(JsonDriver driver)
    {
        public int Requests { get; private set; }

        public JsonNode? Ask(object request)
        {
            Requests++;
            var response = JsonNode.Parse(driver.Handle(JsonSerializer.Serialize(request)))!;
            Assert.True((bool)response["ok"]!, response["error"]?.ToString());
            return response["result"];
        }

        public JsonNode? Query(string name, object? args = null) => Ask(new { op = "query", name, args });

        public void Command(JsonObject command) => Ask(new { op = "command", command });

        public void Step(int ticks) => Ask(new { op = "step", ticks });
    }

    [Fact]
    public void AnExternalDriverPlaysTheDemoLoopThroughJson()
    {
        var c = new Client(new JsonDriver(TestContent.Load()));
        c.Ask(new { op = "new", setup = new { Width = 5, Height = 5, Seed = 3037 } });
        int home = (int)c.Query("StartTile")!;
        int Core(int tile) => (int)c.Query("FindBuilding", new { tileId = tile, defId = "core" })!["Id"]!;
        var costs = c.Query("BuildingTypes")!.AsArray().ToDictionary(b => (string)b!["Id"]!, b => b!["Cost"]!.AsObject());
        int goal() => c.Query("Goal") is { } g ? (int)g["Index"]! : int.MaxValue;

        // Places a planned layout once the tile can pay for all of it, running the sim while it can't.
        bool Build(int tile, JsonArray? plan, int maxSeconds = 600)
        {
            if (plan == null)
                return false;
            var cost = new Dictionary<string, int>();
            foreach (var p in plan)
            {
                foreach (var (item, n) in costs[(string)p!["BuildingId"]!])
                    cost[item] = cost.GetValueOrDefault(item) + (int)n!;
            }
            for (int s = 0; !(bool)c.Query("CanAfford", new { tileId = tile, cost })!; s += 10)
            {
                if (s > maxSeconds)
                    return false;
                c.Step(600);
            }
            foreach (var p in plan)
                c.Command(new JsonObject
                {
                    ["type"] = "place", ["Player"] = 0, ["TileId"] = tile, ["BuildingId"] = p!["BuildingId"]!.GetValue<string>(),
                    ["X"] = (int)p["X"]!, ["Y"] = (int)p["Y"]!, ["Rotation"] = (int)p["Rotation"]!, ["RecipeId"] = p["RecipeId"]?.GetValue<string>(),
                });
            c.Step(1);
            return true;
        }
        JsonArray? Line(int tile, string? deposit, object[] stages, int sink, int zone = -1) =>
            c.Query("PlanLine", new { tileId = tile, depositItem = deposit, source = -1, stages, sink, zone })?.AsArray();
        void RunUntil(Func<bool> done, int maxSeconds)
        {
            for (int s = 0; s < maxSeconds && !done(); s += 10)
                c.Step(600);
        }

        // Goals 1-2: a base core off the starting unit's frame, so home keeps funds when the scout leaves (FB-019),
        // then copper ore, iron ore and iron plates into it.
        int scoutCoreId = Core(home);
        var baseSpot = c.Query("FindSpot", new { tileId = home, buildingId = "core" })!;
        Assert.True(Build(home, [new JsonObject { ["BuildingId"] = "core", ["X"] = (int)baseSpot["X"]!, ["Y"] = (int)baseSpot["Y"]!, ["Rotation"] = 0 }]));
        int core = c.Query("Buildings", new { tileId = home })!.AsArray().Where(b => (string)b!["DefId"]! == "core").Max(b => (int)b!["Id"]!);
        Assert.NotEqual(scoutCoreId, core);
        Assert.True(Build(home, Line(home, "copper_ore", [], core)));
        Assert.True(Build(home, Line(home, "iron_ore", [], core)));
        Assert.True(Build(home, Line(home, "iron_ore", [new { BuildingId = "smelter", RecipeId = "smelt_iron" }], core)));
        Assert.True(Build(home, Line(home, "copper_ore", [], core)));
        RunUntil(() => goal() >= 2, 600);
        Assert.Equal(2, goal());

        // Goal 3: lift off the starting scout, which stands on its frame on the home tile.
        var scoutCore = c.Query("FindBuilding", new { tileId = home, defId = "core" })!;
        Assert.Equal(scoutCoreId, (int)scoutCore["Id"]!);
        int sx = (int)scoutCore["X"]!, sy = (int)scoutCore["Y"]!;
        Assert.Equal("Ok", (string)c.Query("CheckActivate", new { tileId = home, x = sx, y = sy })!);
        c.Command(new JsonObject { ["type"] = "activate_unit", ["Player"] = 0, ["TileId"] = home, ["X"] = sx, ["Y"] = sy });
        c.Step(1);
        Assert.Equal(3, goal());

        // Goal 4: fly it to the victory circle of a neighbour with coal, land it there and hold the circle.
        int scout = (int)c.Query("Units", new { owner = 0 })!.AsArray().Single()!["Id"]!;
        var neighbours = c.Query("Tile", new { tileId = home })!["Neighbors"]!.AsArray().Select(n => (int)n!);
        var coalTile = neighbours.Select(n => c.Query("Tile", new { tileId = n })!)
            .First(t => t["Deposits"]!.AsObject().ContainsKey("coal")
                && c.Query("Travel", new { unitId = scout, tileId = (int)t["Id"]!, x = (int)t["CircleX"]!, y = (int)t["CircleY"]! }) != null);
        int coal = (int)coalTile["Id"]!;
        c.Command(new JsonObject
        {
            ["type"] = "move_units", ["Player"] = 0, ["UnitIds"] = new JsonArray(scout), ["TileId"] = coal,
            ["X"] = (int)coalTile["CircleX"]!, ["Y"] = (int)coalTile["CircleY"]!,
        });
        c.Step(1);
        for (int s = 0; s < 120 && (bool)c.Query("Unit", new { unitId = scout })!["Moving"]!; s++)
            c.Step(60);
        Assert.Equal("Ok", (string)c.Query("CheckDeploy", new { unitId = scout })!);
        c.Command(new JsonObject { ["type"] = "deploy_unit", ["Player"] = 0, ["UnitId"] = scout });
        RunUntil(() => (int)c.Query("Tile", new { tileId = coal })!["Owner"]! == 0, 120);
        Assert.Equal(4, goal());

        // Goal 5: coal drill into an export port facing home, paid from what the landed scout's core brought; import
        // port and steel mill at home, belt to the base core.
        var spot = c.Query("FindSpot", new { tileId = coal, buildingId = "export_port", zone = home })!;
        Assert.True(Build(coal, [new JsonObject { ["BuildingId"] = "export_port", ["X"] = (int)spot["X"]!, ["Y"] = (int)spot["Y"]!, ["Rotation"] = 0 }]));
        int export = (int)c.Query("FindBuilding", new { tileId = coal, defId = "export_port" })!["Id"]!;
        Assert.True(Build(coal, Line(coal, "coal", [], export)));
        Assert.True(Build(home, Line(home, "copper_ore", [new { BuildingId = "smelter", RecipeId = "smelt_copper" }], core)));
        Assert.True(Build(home, Line(home, null, [new { BuildingId = "import_port" }, new { BuildingId = "steel_mill" }], core, zone: coal), 900));
        int mill = (int)c.Query("FindBuilding", new { tileId = home, defId = "steel_mill" })!["Id"]!;

        // Goal 6: plates into the mill, and steel out of it.
        Assert.True(Build(home, Line(home, "iron_ore", [new { BuildingId = "smelter", RecipeId = "smelt_iron" }], mill)));
        RunUntil(() => goal() >= 6, 1200);
        Assert.Equal(6, goal());

        // Goal 7: thruster, core, thruster on a 4x2 frame, then lift off.
        var frame = c.Query("FindFrame", new { tileId = home, width = 4, height = 2 })!;
        int fx = (int)frame["X"]!, fy = (int)frame["Y"]!;
        var unit = new JsonArray(
            new JsonObject { ["BuildingId"] = "thruster", ["X"] = fx, ["Y"] = fy, ["Rotation"] = 0 },
            new JsonObject { ["BuildingId"] = "core", ["X"] = fx + 1, ["Y"] = fy, ["Rotation"] = 0 },
            new JsonObject { ["BuildingId"] = "thruster", ["X"] = fx + 3, ["Y"] = fy, ["Rotation"] = 0 });
        RunUntil(() => (bool)c.Query("CanAfford", new { tileId = home, cost = new Dictionary<string, int> { ["steel"] = 10, ["iron_plate"] = 20, ["iron_ore"] = 26 } })!, 1200);
        for (int y = fy; y < fy + 2; y++)
            for (int x = fx; x < fx + 4; x++)
                c.Command(new JsonObject { ["type"] = "place_frame", ["Player"] = 0, ["TileId"] = home, ["X"] = x, ["Y"] = y });
        Assert.True(Build(home, unit));
        Assert.Equal("Ok", (string)c.Query("CheckActivate", new { tileId = home, x = fx + 1, y = fy })!);
        c.Command(new JsonObject { ["type"] = "activate_unit", ["Player"] = 0, ["TileId"] = home, ["X"] = fx + 1, ["Y"] = fy });
        c.Step(1);

        Assert.Null(c.Query("Goal"));
        output.WriteLine($"demo loop done at tick {(long)c.Query("Tick")!} in {c.Requests} requests");
    }

    [Fact]
    public void BadRequestsGetErrorsNotExceptions()
    {
        var d = new JsonDriver(TestContent.Load());
        foreach (string bad in new[]
                 {
                     "{nonsense", """{"op":"query","name":"Tiles"}""", """{"op":"fly"}""",
                     """{"op":"new","setup":{"Width":99,"Height":5,"Seed":1}}""",
                 })
            Assert.False((bool)JsonNode.Parse(d.Handle(bad))!["ok"]!, bad);

        d.Handle("""{"op":"new","setup":{"Width":4,"Height":4,"Seed":1,"Players":2}}""");
        string[] refused =
        [
            """{"op":"query","name":"Tile","args":{"wrong":1}}""",
            """{"op":"command","command":{"type":"place","Player":1,"TileId":0,"BuildingId":"drill","X":0,"Y":0}}""",
            """{"op":"command","command":{"type":"warp","Player":0}}""",
            """{"op":"step","ticks":-5}""",
        ];
        foreach (string bad in refused)
            Assert.False((bool)JsonNode.Parse(d.Handle(bad))!["ok"]!, bad);
        var list = JsonNode.Parse(d.Handle("""{"op":"queries"}"""))!["result"]!.AsArray().Select(n => (string)n!).ToList();
        Assert.Contains(list, q => q.StartsWith("PlanLine("));
    }
}

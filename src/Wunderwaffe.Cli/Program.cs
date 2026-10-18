using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;

// Plays Wunderwaffe headless through JSON lines (NF-009); see JsonDriver for the protocol.
// Usage: dotnet run --project src/Wunderwaffe.Cli [-- --data <dir with the game's JSON>]
int at = Array.IndexOf(args, "--data");
string dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : FindData();
string Read(string name) => File.ReadAllText(Path.Combine(dir, name));
ContentDb content;
try
{
    content = ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("goals.json"), Read("terrain.json"), Read("layers.json"),
        Directory.GetFiles(Path.Combine(dir, "prefabs"), "*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText));
}
catch (Exception e) when (e is IOException or ContentException)
{
    Console.Error.WriteLine($"can't load content from {dir}: {e.Message}");
    return 1;
}
new JsonDriver(content).Run(Console.In, Console.Out);
return 0;

// game/data, from the repo root or anywhere below it.
static string FindData()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
    {
        string candidate = Path.Combine(d.FullName, "game", "data");
        if (Directory.Exists(candidate))
            return candidate;
    }
    return Path.Combine("game", "data");
}

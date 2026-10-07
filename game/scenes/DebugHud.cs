using System.Text;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Sim;

namespace Wunderwaffe.Game;

/// <summary>Placeholder HUD: performance, build palette, tile inventory and production lines.</summary>
public partial class DebugHud : Label
{
    [Export] public BuildTool? BuildTool;

    SimHost _host = null!;

    public override void _Ready() => _host = GetNode<SimHost>("/root/SimHost");

    public override void _Process(double delta)
    {
        var sim = _host.Sim;
        var tile = _host.ViewedTile;
        var sb = new StringBuilder();
        sb.AppendLine($"{Engine.GetFramesPerSecond()} fps   sim {_host.StepMs:F3} ms/tick   tick {sim.Tick} ({sim.Tick / Simulation.TicksPerSecond}s)");
        sb.AppendLine($"Tile {tile.Id} ({tile.X},{tile.Y})  {tile.Mode}  buildings {tile.Grid.BuildingCount}");
        sb.AppendLine();
        if (BuildTool != null)
            sb.AppendLine(BuildTool.Status).AppendLine();
        sb.AppendLine("Inventory");
        foreach (var (id, n) in tile.Inventory.Entries)
            sb.AppendLine($"  {id}: {n}");
        sb.AppendLine("Production");
        foreach (var line in tile.Lines.Values)
            sb.AppendLine($"  {line.RecipeId} x{line.Machines}");
        sb.AppendLine();
        sb.Append("WASD pan · wheel zoom · MMB drag · R rotate · LMB place · RMB remove · F3 stress");
        Text = sb.ToString();
    }
}

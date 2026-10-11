using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Alerts the sim derives from state rather than from one moment (UI-003): a tile whose machines have gone without
/// inputs. Runs once a second at the end of the tick, so an alert shows within a second of the condition. The tiles
/// already reported are output-only state: rebuilt on replay, never hashed, never read by the rules.
/// </summary>
public static class Alerts
{
    /// <summary>A machine counts as starved after waiting this long, so gaps between batches don't alert.</summary>
    public const int StarvedTicks = 5 * Simulation.TicksPerSecond;

    /// <summary>Ticks between scans.</summary>
    public const int CheckEvery = Simulation.TicksPerSecond;

    /// <summary>The first input a starved machine on the tile lacks, or null if none is starved.</summary>
    public static (PlacedBuilding Machine, string Item)? StarvedOn(TileState tile)
    {
        foreach (var b in tile.Grid.Machines)
        {
            var m = b.Machine!;
            if (m.Recipe is { Inputs.Count: > 0 } && m.Status == MachineStatus.Starved && m.IdleTicks >= StarvedTicks
                && m.MissingInput is { } item)
                return (b, item);
        }
        return null;
    }

    internal static void Step(Simulation sim)
    {
        if (sim.Tick % CheckEvery != 0)
            return;
        foreach (var tile in sim.Tiles)
        {
            if (tile.Owner == TileState.Unowned || tile.Grid.Machines.Count == 0)
            {
                sim.StarvedTiles.Remove(tile.Id);
                continue;
            }
            if (StarvedOn(tile) is not { } starved)
            {
                sim.StarvedTiles.Remove(tile.Id);
                continue;
            }
            if (sim.StarvedTiles.Add(tile.Id))
                sim.Raise(GameEventKind.InputsStarved, tile.Owner, tile.Id, itemId: starved.Item);
        }
    }
}

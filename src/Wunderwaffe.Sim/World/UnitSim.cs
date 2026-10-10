namespace Wunderwaffe.Sim.World;

public enum MoveResult
{
    Ok,
    NoSuchTile,
    OutOfBounds,
    NoUnits,

    /// <summary>None of the units has an engine strong enough to move.</summary>
    CannotMove,
    Unreachable,
}

/// <summary>
/// Move orders. An order is a route of tiles, found by breadth-first search, plus a point in the last one; the physics
/// step (<see cref="UnitPhysics"/>) flies the unit along it.
/// </summary>
internal static class UnitSim
{
    /// <summary>Spacing between units sent to the same point, in sub-cells.</summary>
    const int FormationSpacing = 2 * Unit.CellScale;
    const int FormationColumns = 4;

    public static MoveResult Order(Simulation sim, int player, IReadOnlyList<int> unitIds, int tileId, int cellX, int cellY)
    {
        if (sim.TryGetTile(tileId) is not { } dest)
            return MoveResult.NoSuchTile;
        if (!dest.Grid.InBounds(cellX, cellY))
            return MoveResult.OutOfBounds;

        var units = unitIds.Select(sim.UnitById).Where(u => u != null && u.Owner == player).Select(u => u!).ToList();
        int columns = Math.Min(units.Count, FormationColumns);
        int rows = (units.Count + FormationColumns - 1) / FormationColumns;

        int slot = 0;
        var result = MoveResult.NoUnits;
        foreach (var unit in units)
        {
            if (unit.MaxSpeed <= 0)
            {
                if (result == MoveResult.NoUnits)
                    result = MoveResult.CannotMove;
                continue;
            }
            var route = FindRoute(sim, sim.Tiles[unit.TileId], dest);
            if (route == null)
            {
                if (result is MoveResult.NoUnits or MoveResult.CannotMove)
                    result = MoveResult.Unreachable;
                continue;
            }

            // A grid centred on the target, so a lone unit lands exactly on it.
            long x = (long)cellX * Unit.CellScale + Unit.CellScale / 2 + (slot % columns * 2 - (columns - 1)) * (long)FormationSpacing / 2;
            long y = (long)cellY * Unit.CellScale + Unit.CellScale / 2 + (slot / columns * 2 - (rows - 1)) * (long)FormationSpacing / 2;
            slot++;
            unit.Route = route;
            unit.RouteIndex = 0;
            unit.PathKey = null;
            unit.DestTileId = dest.Id;
            unit.DestX = (int)Math.Clamp(x, 0, (long)dest.Grid.Width * Unit.CellScale - 1);
            unit.DestY = (int)Math.Clamp(y, 0, (long)dest.Grid.Height * Unit.CellScale - 1);
            result = MoveResult.Ok;
        }
        return result;
    }

    /// <summary>Tiles to cross to get from <paramref name="from"/> to <paramref name="to"/>, excluding the start; empty if already there.</summary>
    public static int[]? FindRoute(Simulation sim, TileState from, TileState to)
    {
        if (from == to)
            return [];

        var parent = new Dictionary<int, int> { [from.Id] = -1 };
        var queue = new Queue<TileState>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var next in sim.Neighbors(tile))
            {
                if (!parent.TryAdd(next.Id, tile.Id))
                    continue;
                if (next == to)
                {
                    var path = new List<int>();
                    for (int id = next.Id; id != from.Id; id = parent[id])
                        path.Add(id);
                    path.Reverse();
                    return [.. path];
                }
                queue.Enqueue(next);
            }
        }
        return null;
    }
}

using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// The tile's entity simulation: machines work and dump output, belts carry items cell by cell, and ports move
/// items between tiles (FB-004, FB-006, IO-002). Everything is integer and visits buildings in id order, so the
/// same commands give the same state on every machine (NF-004).
/// </summary>
internal static class TileEntities
{
    const int DumpRetryTicks = 8;

    public static void Step(Simulation sim, TileState tile)
    {
        var grid = tile.Grid;
        if (grid.BuildingCount == 0)
            return;

        foreach (var b in grid.Machines)
        {
            if (Work(b.Machine!) is { } made && tile.Owner != TileState.Unowned)
                sim.Player(tile.Owner).AddProduced(made);
            Dump(sim, tile, b);
        }
        foreach (var b in grid.ImportPorts)
            Dump(sim, tile, b);

        // Items advance within their cells first, then front items cross into the next building. Crossing
        // checks the state after everyone has moved, so a belt hands over as soon as the next cell has room.
        foreach (var b in grid.Belts)
            Advance(b.Belt!);
        foreach (var b in grid.Belts)
            Handoff(sim, tile, b);

        foreach (var b in grid.ExportPorts)
            Deliver(sim, tile, b);
    }

    /// <summary>Advances the machine's recipe. Returns the outputs of a batch that finished this tick, if one did.</summary>
    static IReadOnlyDictionary<string, int>? Work(MachineState m)
    {
        var recipe = m.Recipe;
        if (recipe == null)
            return null;

        if (!m.Working && !TryStart(m, recipe))
        {
            if (m.IdleTicks < int.MaxValue)
                m.IdleTicks++;
            return null;
        }
        m.IdleTicks = 0;

        if (++m.Progress < recipe.Ticks)
            return null;
        foreach (var (id, n) in recipe.Outputs)
            m.Output[id] = m.Output.GetValueOrDefault(id) + n;
        m.OutputCount += m.OutputPerBatch;
        m.Working = false;
        return recipe.Outputs;
    }

    static bool TryStart(MachineState m, RecipeDef recipe)
    {
        if (m.OutputCount + m.OutputPerBatch > MachineState.OutputCapacity)
            return false;
        if (m.Input.Count == 0 && recipe.Inputs.Count > 0)
            return false;
        foreach (var (id, need) in recipe.Inputs)
        {
            if (m.Input.GetValueOrDefault(id) < need)
                return false;
        }
        foreach (var (id, need) in recipe.Inputs)
            Take(m.Input, id, need);
        m.Working = true;
        m.Progress = 0;
        return true;
    }

    static void Take(SortedDictionary<string, int> buffer, string id, int n)
    {
        int left = buffer[id] - n;
        if (left == 0)
            buffer.Remove(id);
        else
            buffer[id] = left;
    }

    /// <summary>Pushes one output item into an adjacent building that takes it, neighbours in turn.</summary>
    static void Dump(Simulation sim, TileState tile, PlacedBuilding b)
    {
        var m = b.Machine!;
        if (m.OutputCount == 0)
            return;
        if (m.DumpWait > 0)
        {
            m.DumpWait--;
            return;
        }

        string item = m.Output.Keys.First();
        int around = 2 * (b.Width + b.Height);
        for (int k = 0; k < around; k++)
        {
            int i = (m.DumpCursor + k) % around;
            var (x, y) = Perimeter(b, i);
            if (tile.Grid.BuildingAt(x, y) is not { } target || target == b || !TryInsert(sim, tile, target, item, b))
                continue;

            Take(m.Output, item, 1);
            m.OutputCount--;
            m.DumpCursor = (i + 1) % around;
            return;
        }
        m.DumpWait = DumpRetryTicks;
    }

    /// <summary>The cells just outside a building, clockwise from the top-left.</summary>
    static (int X, int Y) Perimeter(PlacedBuilding b, int i)
    {
        if (i < b.Width)
            return (b.X + i, b.Y - 1);
        i -= b.Width;
        if (i < b.Height)
            return (b.X + b.Width, b.Y + i);
        i -= b.Height;
        if (i < b.Width)
            return (b.X + b.Width - 1 - i, b.Y + b.Height);
        i -= b.Width;
        return (b.X - 1, b.Y + b.Height - 1 - i);
    }

    static void Advance(BeltState belt)
    {
        for (int i = 0; i < belt.Count; i++)
        {
            int limit = i == 0 ? BeltState.CellLength : belt.Positions[i - 1] - BeltState.Spacing;
            belt.Positions[i] = Math.Min(belt.Positions[i] + belt.Speed, limit);
        }
    }

    static void Handoff(Simulation sim, TileState tile, PlacedBuilding b)
    {
        var belt = b.Belt!;
        if (belt.Count == 0 || belt.Positions[0] < BeltState.CellLength)
            return;
        var ahead = Directions.FromRotation(b.Rotation);
        if (tile.Grid.BuildingAt(b.X + ahead.Dx(), b.Y + ahead.Dy()) is { } target
            && TryInsert(sim, tile, target, belt.Items[0]!, b))
        {
            belt.PopFront();
        }
    }

    /// <summary>Offers an item to a building. True if it took it.</summary>
    static bool TryInsert(Simulation sim, TileState tile, PlacedBuilding target, string item, PlacedBuilding source)
    {
        switch (target.Kind)
        {
            case BuildingKind.Core:
                tile.Inventory.Add(item, 1);
                return true;

            case BuildingKind.Conveyor:
            {
                // A belt takes items from behind and from the sides, never from the cell it faces.
                var ahead = Directions.FromRotation(target.Rotation);
                if (tile.Grid.BuildingAt(target.X + ahead.Dx(), target.Y + ahead.Dy()) == source)
                    return false;
                var belt = target.Belt!;
                if (!belt.CanAccept)
                    return false;
                belt.Push(item);
                return true;
            }

            case BuildingKind.Machine:
            {
                var m = target.Machine!;
                if (m.Recipe == null || !m.Recipe.Inputs.TryGetValue(item, out int need))
                    return false;
                int have = m.Input.GetValueOrDefault(item);
                if (have >= need * MachineState.InputBatches)
                    return false;
                m.Input[item] = have + 1;
                return true;
            }

            case BuildingKind.ExportPort:
                return Send(sim, tile, target, item);

            case BuildingKind.Weapon when sim.Content.Buildings[target.DefId].Weapon is { } w && w.Ammo == item && target.Combat!.Ammo < w.Magazine:
                target.Combat.Ammo++;
                return true;

            case BuildingKind.UnitFactory:
                return UnitFactory.TryInsert(target, item);

            default:
                return false;
        }
    }

    /// <summary>
    /// An export port takes an item only if the tile its zone faces is yours and has an import port in its zone facing
    /// back, so a missing or full link backs the belt up instead of losing items.
    /// </summary>
    static bool Send(Simulation sim, TileState tile, PlacedBuilding port, string item)
    {
        var link = port.Export!;
        if (link.InFlight.Count >= ExportState.LinkCapacity)
            return false;

        if (sim.TryGetTile(PortRules.PartnerOf(tile.Grid, port)) is not { } neighbor
            || neighbor.Owner != tile.Owner || tile.Owner == TileState.Unowned)
            return false;
        if (FindImport(neighbor, tile.Id, 0, needRoom: false) < 0)
            return false;

        link.InFlight.Enqueue(new Shipment(sim.Tick + PortRules.TransitTicksPerTile, item));
        return true;
    }

    static void Deliver(Simulation sim, TileState tile, PlacedBuilding port)
    {
        var link = port.Export!;
        var neighbor = sim.TryGetTile(PortRules.PartnerOf(tile.Grid, port));

        while (link.InFlight.Count > 0 && link.InFlight.Peek().ArriveTick <= sim.Tick)
        {
            if (neighbor == null || neighbor.Owner != tile.Owner)
                return;
            int i = FindImport(neighbor, tile.Id, link.Cursor, needRoom: true);
            if (i < 0)
                return; // Landing is blocked: the item waits at the edge and the link backs up.

            var imports = neighbor.Grid.ImportPorts;
            var m = imports[i].Machine!;
            string item = link.InFlight.Dequeue().ItemId;
            m.Output[item] = m.Output.GetValueOrDefault(item) + 1;
            m.OutputCount++;
            link.Cursor = (i + 1) % imports.Count;
            if (neighbor.Owner != TileState.Unowned)
                sim.Player(neighbor.Owner).Shipped++;
        }
    }

    /// <summary>
    /// Index of an import port in <paramref name="tile"/>'s zone for <paramref name="from"/>, searching from
    /// <paramref name="start"/>, or -1.
    /// </summary>
    static int FindImport(TileState tile, int from, int start, bool needRoom)
    {
        var imports = tile.Grid.ImportPorts;
        for (int k = 0; k < imports.Count; k++)
        {
            int i = (start + k) % imports.Count;
            var p = imports[i];
            if (PortRules.PartnerOf(tile.Grid, p) == from
                && (!needRoom || p.Machine!.OutputCount < MachineState.OutputCapacity))
                return i;
        }
        return -1;
    }

    /// <summary>Everything a building holds, so removing it doesn't destroy items.</summary>
    public static void Drain(TileState tile, PlacedBuilding b)
    {
        var inv = tile.Inventory;
        if (b.Belt is { } belt)
        {
            for (int i = 0; i < belt.Count; i++)
                inv.Add(belt.Items[i]!, 1);
        }
        if (b.Machine is { } m)
        {
            foreach (var (id, n) in m.Input)
                inv.Add(id, n);
            foreach (var (id, n) in m.Output)
                inv.Add(id, n);
            // Inputs already taken for a batch in progress.
            if (m.Working && m.Recipe != null)
            {
                foreach (var (id, n) in m.Recipe.Inputs)
                    inv.Add(id, n);
            }
        }
        if (b.Export is { } link)
        {
            foreach (var s in link.InFlight)
                inv.Add(s.ItemId, 1);
        }
        if (b.Combat is { Ammo: > 0, AmmoItem: { } ammo } c)
            inv.Add(ammo, c.Ammo);
        if (b.Factory is { } factory)
            UnitFactory.Drain(tile, factory);
    }
}

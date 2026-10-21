using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// The tile's entity simulation: power networks balance, machines work and dump output, storage and relays pass
/// items on, belts carry items cell by cell, and ports move items between tiles (FB-004..FB-007, FB-016, IO-002).
/// Everything is integer and visits buildings in id order, so the same commands give the same state on every
/// machine (NF-004).
/// </summary>
internal static class TileEntities
{
    const int DumpRetryTicks = 8;

    /// <summary>Most sorters an item may pass through in one tick, so a ring of sorters can't loop forever.</summary>
    const int MaxSorterChain = 16;

    public static void Step(Simulation sim, TileState tile)
    {
        tile.Stats.Advance(sim.Tick);
        var grid = tile.Grid;
        if (grid.BuildingCount == 0)
            return;

        PowerNet.Step(sim, tile);

        // Machines that can't do anything sleep until something changes around them (MachineSleep, NF-001).
        var machines = grid.Machines;
        var sleep = grid.Sleep;
        sleep.Begin(grid);
        for (int i = sleep.NextAwake(0); i >= 0; i = sleep.NextAwake(i + 1))
        {
            sleep.Visit(i);
            var b = machines[i];
            var m = b.Machine!;
            if (b.Owner < 0)
                continue; // unclaimed (WM-019): stopped, see Territory.AfterCoreLost
            // Draw and supply change only when these flip (PowerNet.DrawOf, SupplyOf): report only then (NF-001).
            bool drew = m.Working || m.IdleTicks == 0, worked = m.Working;
            if (Work(tile, b, m) is { } made && tile.Owner != TileState.Unowned)
                sim.Player(tile.Owner).AddProduced(made);
            if ((m.Working || m.IdleTicks == 0) != drew || m.Working != worked)
                m.Net?.Report(m);
            Dump(sim, tile, b, m);
            // Idle with nothing to dump: every later tick would do the same until an input, power or the layout changes.
            if (m.Recipe != null && m.IdleTicks > 0 && m.OutputCount == 0 && !sleep.Off)
                sleep.Sleep(m);
        }
        sleep.End();
        // Unclaimed buildings (WM-019, Owner < 0) don't run: nothing moves on or out of them.
        foreach (var b in grid.ImportPorts)
        {
            if (b.Owner >= 0)
                Dump(sim, tile, b, b.Machine!);
        }
        foreach (var b in grid.Storages)
        {
            if (b.Owner >= 0)
                Dump(sim, tile, b, b.Storage!);
        }
        foreach (var b in grid.Relays)
        {
            if (b.Owner >= 0)
                Relay(sim, tile, b);
        }

        // Items advance within their cells first, then front items cross into the next building. Crossing
        // checks the state after everyone has moved, so a belt hands over as soon as the next cell has room.
        foreach (var b in grid.Belts)
        {
            if (b.Owner >= 0)
                Advance(b.Belt!);
        }
        foreach (var b in grid.Belts)
        {
            if (b.Owner >= 0)
                Handoff(sim, tile, b);
        }

        foreach (var b in grid.ExportPorts)
        {
            if (b.Owner >= 0)
                Deliver(sim, tile, b);
        }
    }

    /// <summary>
    /// Advances the machine's recipe at its boosted speed, scaled by its power (FB-005, FB-016). Returns the outputs
    /// of a batch that finished this tick, if one did, with any booster extra included. An extractor's batch comes out
    /// of the deposit patch under it (RS-004).
    /// </summary>
    static IReadOnlyDictionary<string, int>? Work(TileState tile, PlacedBuilding b, MachineState m)
    {
        var recipe = m.Recipe;
        if (recipe == null)
            return null;

        int rate = m.Working || TryStart(tile, b, m, recipe)
            ? MachineState.ProgressPerTick * m.SpeedPercent / 100 * m.PowerPermille / 1000
            : 0;
        if (rate <= 0)
        {
            if (m.IdleTicks < int.MaxValue)
                m.IdleTicks++;
            return null;
        }
        m.IdleTicks = 0;

        m.Progress += rate;
        if (m.Progress < recipe.Ticks * MachineState.ProgressPerTick)
            return null;
        m.Working = false;

        int batches = 1;
        m.OutputCredit += m.OutputBonus;
        for (; m.OutputCredit >= 100; m.OutputCredit -= 100)
            batches++;
        IReadOnlyDictionary<string, int> made = batches == 1 ? recipe.Outputs : recipe.Outputs.ToDictionary(kv => kv.Key, kv => kv.Value * batches);
        if (m.Mines is { } ore && tile.Grid.Patches.Extract(b, m, ore, made) is { } mined)
            made = mined;
        foreach (var (id, n) in made)
        {
            m.AddOutput(id, n);
            tile.Stats.RecordMade(id, n);
        }
        return made.Count == 0 ? null : made;
    }

    static bool TryStart(TileState tile, PlacedBuilding b, MachineState m, RecipeDef recipe)
    {
        if (m.OutputCount + m.OutputPerBatch > MachineState.OutputCapacity)
            return false;
        if (m.Mines is { } ore)
        {
            m.Depleted = tile.Grid.Patches.SourceOf(b, m, ore) < 0;
            if (m.Depleted)
                return false;
        }
        if (m.Input.Count == 0 && recipe.Inputs.Count > 0)
            return false;
        foreach (var (id, need) in recipe.Inputs)
        {
            if (m.Input.GetValueOrDefault(id) < need)
                return false;
        }
        foreach (var (id, need) in recipe.Inputs)
        {
            Take(m.Input, id, need);
            tile.Stats.RecordUsed(id, need);
        }
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

    /// <summary>Pushes one held item into an adjacent building that takes it, neighbours in turn.</summary>
    static void Dump(Simulation sim, TileState tile, PlacedBuilding b, OutputBuffer m)
    {
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
            if (tile.Grid.BuildingAt(x, y) is not { } target || target == b
                // Storage unloads only onto belts leading away, so it never churns items with its neighbours.
                || (b.Kind == BuildingKind.Storage && target.Kind != BuildingKind.Conveyor)
                || !TryInsert(sim, tile, target, item, b))
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

    /// <summary>The way an item moves going from <paramref name="source"/> into the adjacent <paramref name="target"/>.</summary>
    static Direction TravelDir(PlacedBuilding source, PlacedBuilding target) =>
        source.X + source.Width <= target.X ? Direction.East
        : source.X >= target.X + target.Width ? Direction.West
        : source.Y + source.Height <= target.Y ? Direction.South
        : Direction.North;

    static PlacedBuilding? Beside(TileState tile, PlacedBuilding b, Direction d) =>
        tile.Grid.BuildingAt(b.X + d.Dx(), b.Y + d.Dy());

    /// <summary>Offers an item to a building. True if it took it.</summary>
    static bool TryInsert(Simulation sim, TileState tile, PlacedBuilding target, string item, PlacedBuilding source, int depth = 0)
    {
        if (target.Owner < 0)
            return false; // unclaimed (WM-019) takes nothing
        switch (target.Kind)

        {
            case BuildingKind.Core:
                if (!Funds.Accepts(target, item))
                    return false;
                target.Stock!.Add(item, 1);
                return true;

            case BuildingKind.Conveyor:
            {
                // A belt takes items from behind and from the sides, never from the cell it faces.
                if (Beside(tile, target, Directions.FromRotation(target.Rotation)) == source)
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
                if (m.Bed is { } bed)
                    bed.Wake(m);
                return true;
            }

            case BuildingKind.ExportPort:
                return Send(sim, tile, target, item);

            case BuildingKind.Weapon when sim.Content.Buildings[target.DefId].Weapon is { } w && w.Ammo == item && target.Combat!.Ammo < w.Magazine:
                target.Combat.Ammo++;
                return true;

            case BuildingKind.UnitFactory:
                return UnitFactory.TryInsert(target, item);

            case BuildingKind.Storage:
            {
                var s = target.Storage!;
                if (s.OutputCount >= s.Capacity)
                    return false;
                s.AddOutput(item, 1);
                return true;
            }

            case BuildingKind.Junction:
            {
                var dir = TravelDir(source, target);
                var lane = target.Relay!.Lanes[(int)dir];
                if (lane.Count >= RelayState.JunctionCapacity)
                    return false;
                lane.Add(new RelayItem(item, sim.Tick + RelayState.PassTicks, (int)dir));
                return true;
            }

            case BuildingKind.Router:
            {
                var lane = target.Relay!.Lanes[0];
                if (lane.Count >= RelayState.RouterCapacity)
                    return false;
                lane.Add(new RelayItem(item, sim.Tick + RelayState.PassTicks, source.Id));
                return true;
            }

            case BuildingKind.Bridge:
            {
                // Never from the building it feeds, or the two would pass items back and forth.
                var lane = target.Relay!.Lanes[0];
                if (lane.Count >= RelayState.BridgeCapacity
                    || Beside(tile, target, Directions.FromRotation(target.Rotation)) == source)
                    return false;
                lane.Add(new RelayItem(item, sim.Tick + 1, 0));
                return true;
            }

            case BuildingKind.Sorter:
                return Sort(sim, tile, target, item, source, depth);

            default:
                return false;
        }
    }

    /// <summary>
    /// A sorter passes items on at once: its filter item straight ahead, anything else to the left or right,
    /// alternating, whichever takes it (FB-004). Straight ahead is the way the item was travelling.
    /// </summary>
    static bool Sort(Simulation sim, TileState tile, PlacedBuilding sorter, string item, PlacedBuilding source, int depth)
    {
        if (depth >= MaxSorterChain)
            return false;
        var s = sorter.Sorter!;
        var dir = TravelDir(source, sorter);
        if (item == s.Filter)
            return Beside(tile, sorter, dir) is { } ahead && TryInsert(sim, tile, ahead, item, sorter, depth + 1);

        var left = (Direction)(((int)dir + 3) & 3);
        var right = (Direction)(((int)dir + 1) & 3);
        foreach (var side in s.Flip ? [right, left] : new[] { left, right })
        {
            if (Beside(tile, sorter, side) is { } next && TryInsert(sim, tile, next, item, sorter, depth + 1))
            {
                s.Flip = !s.Flip;
                return true;
            }
        }
        return false;
    }

    /// <summary>Moves the ready front item of a junction, router or bridge on.</summary>
    static void Relay(Simulation sim, TileState tile, PlacedBuilding b)
    {
        var relay = b.Relay!;
        switch (b.Kind)
        {
            case BuildingKind.Junction:
                for (int d = 0; d < 4; d++)
                {
                    var lane = relay.Lanes[d];
                    if (lane.Count > 0 && lane[0].ReadyTick <= sim.Tick
                        && Beside(tile, b, (Direction)d) is { } target && TryInsert(sim, tile, target, lane[0].ItemId, b))
                        lane.RemoveAt(0);
                }
                break;

            case BuildingKind.Router:
            {
                var lane = relay.Lanes[0];
                if (lane.Count == 0 || lane[0].ReadyTick > sim.Tick)
                    return;
                for (int k = 0; k < 4; k++)
                {
                    int i = (relay.Cursor + k) & 3;
                    if (Beside(tile, b, Directions.All[i]) is not { } target || target.Id == lane[0].From
                        || !TryInsert(sim, tile, target, lane[0].ItemId, b))
                        continue;
                    lane.RemoveAt(0);
                    relay.Cursor = (i + 1) & 3;
                    return;
                }
                break;
            }

            case BuildingKind.Bridge:
            {
                var lane = relay.Lanes[0];
                if (lane.Count == 0 || lane[0].ReadyTick > sim.Tick)
                    return;
                if (Logistics.BridgeLink(tile.Grid, b) is { } link)
                {
                    var far = link.Target.Relay!.Lanes[0];
                    if (far.Count >= RelayState.BridgeCapacity)
                        return;
                    far.Add(lane[0] with { ReadyTick = sim.Tick + link.Distance * RelayState.BridgeTicksPerCell });
                    lane.RemoveAt(0);
                }
                else if (Beside(tile, b, Directions.FromRotation(b.Rotation)) is { } target
                    && TryInsert(sim, tile, target, lane[0].ItemId, b))
                {
                    lane.RemoveAt(0);
                }
                break;
            }
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
        tile.Stats.RecordSent(neighbor.Id, 1);
        return true;
    }

    /// <summary>Lands an export port's arrived shipments in the partner tile. Rate mode calls it for tiles it steps.</summary>
    internal static void Deliver(Simulation sim, TileState tile, PlacedBuilding port)
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
            imports[i].Machine!.AddOutput(link.InFlight.Dequeue().ItemId, 1);
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

    /// <summary>
    /// Moves everything a building holds into <paramref name="inv"/>, so removing it or lifting it off doesn't destroy
    /// items. Battery charge is lost.
    /// </summary>
    public static void Drain(PlacedBuilding b, Core.Inventory inv)
    {
        if (b.Stock is { } stock)
            inv.TakeAll(stock);
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
            UnitFactory.Drain(factory, inv);
        if (b.Storage is { } store)
        {
            foreach (var (id, n) in store.Output)
                inv.Add(id, n);
        }
        if (b.Relay is { } relay)
        {
            foreach (var lane in relay.Lanes)
            {
                foreach (var r in lane)
                    inv.Add(r.ItemId, 1);
            }
        }
    }
}

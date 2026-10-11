using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>Per-tile rate-mode state (FB-012, FB-013). Lives in <see cref="Simulation"/>, indexed by tile id.</summary>
internal sealed class TileRateState
{
    /// <summary>Runs the full entity sim. Decided at the end of each tick for the next one.</summary>
    public bool Full = true;

    /// <summary>The tile's computed rates, or null until next needed (after the tile or a linked one changed).</summary>
    public TileRates? Rates;

    /// <summary>
    /// Fractional items owed per flow, in <see cref="RateModel.One"/>ths, aligned with <see cref="AccKeys"/>. Kept across
    /// promotion and demotion, so switching modes neither loses nor adds a part-made item.
    /// </summary>
    public long[] Acc = [];

    /// <summary>The flows <see cref="Acc"/> belongs to: the last rates stepped, or the keys a snapshot restored.</summary>
    public IReadOnlyList<Flow> AccKeys = [];

    /// <summary>The tile's (revision, owner) when its rates were last known good; a change re-solves it and its links.</summary>
    public (int Revision, int Owner) Seen = (-1, int.MinValue);
}

/// <summary>
/// Rate mode (FB-012 to FB-014): a tile no player views and that isn't Contested skips the entity sim and instead moves
/// items at its computed rates (<see cref="RateModel"/>): into its core, out through its export ports as real shipments,
/// and out of its import ports' buffers. Its belts and machines keep what they held, frozen, and carry on from there
/// when it is promoted again, so nothing is lost or made twice. If an import buffer can't cover a tick's draw or an
/// export link is full, the tile waits that tick, which is how a shortfall the rates didn't foresee backs it up.
/// Runs as tick phase 6, after unit physics: steps rate tiles, moves abstract units (<see cref="AbstractTravel"/>),
/// then decides each tile's mode for the next tick.
/// </summary>
public static class RateMode
{
    /// <summary>True if the tile runs the full entity sim this tick (always, unless the sim has rate mode on).</summary>
    public static bool IsFullSim(Simulation sim, TileState tile) => sim.RateState(tile.Id).Full;

    /// <summary>The tile's computed rates (solving them now if needed), whatever mode it is in.</summary>
    public static TileRates RatesOf(Simulation sim, TileState tile)
    {
        Invalidate(sim);
        return Ensure(sim, tile);
    }

    internal static void Step(Simulation sim)
    {
        if (!sim.RateModeEnabled)
            return;
        Invalidate(sim);
        foreach (var tile in sim.Tiles)
        {
            var state = sim.RateState(tile.Id);
            if (!state.Full && tile.Grid.BuildingCount > 0)
                Advance(sim, tile, state);
        }
        AbstractTravel.Step(sim);
        UpdateModes(sim);
    }

    /// <summary>One tick of a rate-mode tile.</summary>
    static void Advance(Simulation sim, TileState tile, TileRateState state)
    {
        var rates = Ensure(sim, tile);
        if (!ReferenceEquals(state.AccKeys, rates.Flows))
            Remap(state, rates.Flows);

        var flows = rates.Flows;
        var grid = tile.Grid;
        bool stall = false;
        for (int i = 0; i < flows.Count && !stall; i++)
        {
            var f = flows[i];
            long owed = (state.Acc[i] + f.Rate) >> RateModel.Shift;
            if (owed == 0)
                continue;
            if (f.Kind == FlowKind.Draw)
                stall = Buffered(grid, f.Item) < owed;
            else if (f.Kind == FlowKind.Export)
                stall = grid.BuildingById(f.PortId)?.Export is not { } link || link.InFlight.Count + owed > ExportState.LinkCapacity;
        }

        if (!stall)
        {
            for (int i = 0; i < flows.Count; i++)
            {
                var f = flows[i];
                long acc = state.Acc[i] + f.Rate;
                long n = acc >> RateModel.Shift;
                state.Acc[i] = acc & (RateModel.One - 1);
                if (n > 0)
                    Apply(sim, tile, f, (int)n);
            }
        }
        foreach (var port in grid.ExportPorts)
            TileEntities.Deliver(sim, tile, port);
    }

    static void Apply(Simulation sim, TileState tile, Flow f, int n)
    {
        switch (f.Kind)
        {
            case FlowKind.Core:
                tile.Inventory.Add(f.Item, n);
                break;
            case FlowKind.Produced:
                if (tile.Owner != TileState.Unowned)
                    sim.Player(tile.Owner).AddProduced(new Dictionary<string, int> { [f.Item] = n });
                break;
            case FlowKind.Draw:
                foreach (var port in tile.Grid.ImportPorts)
                {
                    var m = port.Machine!;
                    int take = Math.Min(n, m.Output.GetValueOrDefault(f.Item));
                    if (take == 0)
                        continue;
                    if (m.Output[f.Item] == take)
                        m.Output.Remove(f.Item);
                    else
                        m.Output[f.Item] -= take;
                    m.OutputCount -= take;
                    n -= take;
                    if (n == 0)
                        break;
                }
                break;
            case FlowKind.Export:
                var link = tile.Grid.BuildingById(f.PortId)!.Export!;
                for (int i = 0; i < n; i++)
                    link.InFlight.Enqueue(new Shipment(sim.Tick + PortRules.TransitTicksPerTile, f.Item));
                break;
        }
    }

    static long Buffered(TileGrid grid, string item)
    {
        long n = 0;
        foreach (var port in grid.ImportPorts)
            n += port.Machine!.Output.GetValueOrDefault(item);
        return n;
    }

    /// <summary>Carries each flow's part-made item over to the new rates; flows that went away drop theirs.</summary>
    static void Remap(TileRateState state, IReadOnlyList<Flow> flows)
    {
        var acc = new long[flows.Count];
        int j = 0;
        for (int i = 0; i < flows.Count; i++)
        {
            while (j < state.AccKeys.Count && state.AccKeys[j].CompareTo(flows[i]) < 0)
                j++;
            if (j < state.AccKeys.Count && state.AccKeys[j].SameKey(flows[i]))
                acc[i] = state.Acc[j];
        }
        state.Acc = acc;
        state.AccKeys = flows;
    }

    /// <summary>
    /// Forgets the rates of every tile whose grid or owner changed, and of the tiles linked to it by ports (and its
    /// neighbours, in case a link just went away). Rates only depend on grids and owners, so this keeps them exact.
    /// </summary>
    static void Invalidate(Simulation sim)
    {
        List<TileState>? changed = null;
        foreach (var tile in sim.Tiles)
        {
            var state = sim.RateState(tile.Id);
            var key = (tile.Grid.Revision, tile.Owner);
            if (state.Seen == key)
                continue;
            state.Seen = key;
            (changed ??= []).Add(tile);
        }
        if (changed == null)
            return;
        var seeds = changed.Concat(changed.SelectMany(sim.Neighbors));
        foreach (var tile in Component(sim, seeds))
            sim.RateState(tile.Id).Rates = null;
    }

    static TileRates Ensure(Simulation sim, TileState tile)
    {
        var state = sim.RateState(tile.Id);
        if (state.Rates is { } rates)
            return rates;
        var component = Component(sim, [tile]);
        foreach (var (id, r) in RateModel.Solve(sim, component))
            sim.RateState(id).Rates = r;
        return state.Rates!;
    }

    /// <summary>The seeds and every tile linked to them through ports, transitively, in id order.</summary>
    static List<TileState> Component(Simulation sim, IEnumerable<TileState> seeds)
    {
        var seen = new SortedSet<int>();
        var queue = new Queue<TileState>();
        foreach (var t in seeds)
        {
            if (seen.Add(t.Id))
                queue.Enqueue(t);
        }
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var n in sim.Neighbors(t))
            {
                if (!seen.Contains(n.Id) && (HasPortTo(t, n.Id) || HasPortTo(n, t.Id)))
                {
                    seen.Add(n.Id);
                    queue.Enqueue(n);
                }
            }
        }
        return seen.Select(id => sim.Tiles[id]).ToList();
    }

    static bool HasPortTo(TileState tile, int partner)
    {
        foreach (var p in tile.Grid.ExportPorts)
        {
            if (PortRules.PartnerOf(tile.Grid, p) == partner)
                return true;
        }
        foreach (var p in tile.Grid.ImportPorts)
        {
            if (PortRules.PartnerOf(tile.Grid, p) == partner)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Full sim where a player views the tile or it is Contested, rate mode elsewhere (FB-013). Units on rate-mode tiles
    /// become abstract nodes; units on full-sim tiles materialise (UC-009).
    /// </summary>
    static void UpdateModes(Simulation sim)
    {
        var full = Territory.ContestedTiles(sim);
        foreach (var p in sim.Players)
        {
            if (p.ViewedTile >= 0 && p.ViewedTile < full.Length)
                full[p.ViewedTile] = true;
        }
        foreach (var tile in sim.Tiles)
            sim.RateState(tile.Id).Full = full[tile.Id];
        AbstractTravel.Update(sim, full);
    }
}

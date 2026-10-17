namespace Wunderwaffe.Sim.World;

/// <summary>Where a rate-mode flow of items goes (FB-012).</summary>
public enum FlowKind
{
    /// <summary>Into one core (<see cref="Flow.PortId"/>), so into its owner's funds on the tile (FB-019).</summary>
    Core,

    /// <summary>Made by the tile's machines; counts toward the owner's goals, like the full sim's batches.</summary>
    Produced,

    /// <summary>Taken out of the tile's import port buffers.</summary>
    Draw,

    /// <summary>Sent through one export port (<see cref="Flow.PortId"/>) to the neighbouring tile.</summary>
    Export,

    /// <summary>Taken in by the tile's machines as recipe inputs, a fuel generator's fuel included; for the stats.</summary>
    Used,
}

/// <summary>
/// One steady-state item flow of a tile, in items per tick times <see cref="RateModel.One"/>.
/// <paramref name="PortId"/> is the export port's building id for <see cref="FlowKind.Export"/>, the core's for
/// <see cref="FlowKind.Core"/> (so per player per core), else 0.
/// </summary>
public readonly record struct Flow(FlowKind Kind, int PortId, string Item, long Rate) : IComparable<Flow>
{
    /// <summary>Items per second, for display.</summary>
    public double PerSecond => (double)Rate * Simulation.TicksPerSecond / RateModel.One;

    /// <summary>Order by kind, port, then item, which is the order flows are stepped, hashed and saved in.</summary>
    public int CompareTo(Flow other) =>
        Kind != other.Kind ? Kind.CompareTo(other.Kind)
        : PortId != other.PortId ? PortId.CompareTo(other.PortId)
        : string.CompareOrdinal(Item, other.Item);

    internal bool SameKey(Flow other) => CompareTo(other) == 0;
}

/// <summary>
/// A tile's computed steady state (OQ-14): what reaches its core, what its machines make, what it draws from its
/// import ports, what its machines use and what each export port sends. Worked out from the belt and building graph by <see cref="RateModel"/>,
/// never measured, so it is a pure function of the grids of the tiles linked to it by ports.
/// </summary>
public sealed class TileRates
{
    /// <summary>Non-zero flows, sorted (see <see cref="Flow.CompareTo"/>).</summary>
    public IReadOnlyList<Flow> Flows { get; internal set; } = [];

    /// <summary>Per neighbour tile id, per item: what this tile's export ports send it.</summary>
    internal SortedDictionary<int, long[]> LinkOut { get; } = [];

    /// <summary>Per neighbour tile id, per item: how much this tile's import ports in that zone could pass on.</summary>
    internal SortedDictionary<int, long[]> LinkCap { get; } = [];

    /// <summary>The rate of one flow, or 0.</summary>
    public long RateOf(FlowKind kind, string item, int portId = 0)
    {
        foreach (var f in Flows)
        {
            if (f.Kind == kind && f.PortId == portId && f.Item == item)
                return f.Rate;
        }
        return 0;
    }

    /// <summary>Sum of one kind of flow for an item, over every port.</summary>
    public long Total(FlowKind kind, string item) => Flows.Where(f => f.Kind == kind && f.Item == item).Sum(f => f.Rate);
}

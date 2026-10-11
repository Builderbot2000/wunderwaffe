using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Items riding on one conveyor cell, front first. Positions run from 0 (entered) to <see cref="CellLength"/>
/// (ready to leave), in integers so the sim stays deterministic. Throughput is
/// <c>speed / Spacing</c> items a tick, which is what limits a belt (FB-004).
/// </summary>
public sealed class BeltState(int speed)
{
    public const int CellLength = 128;

    /// <summary>Minimum gap between two items on a belt.</summary>
    public const int Spacing = 32;

    public const int Capacity = CellLength / Spacing;

    public int Speed { get; } = speed;
    public int Count { get; internal set; }
    public string?[] Items { get; } = new string?[Capacity];
    public int[] Positions { get; } = new int[Capacity];

    /// <summary>Room for an item at the back of the cell.</summary>
    internal bool CanAccept => Count < Capacity && (Count == 0 || Positions[Count - 1] >= Spacing);

    internal void Push(string item)
    {
        Items[Count] = item;
        Positions[Count] = 0;
        Count++;
    }

    internal string PopFront()
    {
        string item = Items[0]!;
        for (int i = 1; i < Count; i++)
        {
            Items[i - 1] = Items[i];
            Positions[i - 1] = Positions[i];
        }
        Count--;
        Items[Count] = null;
        return item;
    }
}

/// <summary>Why a machine is or isn't running, for the starved indicator (FB-006).</summary>
public enum MachineStatus
{
    /// <summary>No recipe: import ports.</summary>
    Idle,
    Working,

    /// <summary>Waiting for inputs.</summary>
    Starved,

    /// <summary>The output buffer is full: nothing next to it takes what it makes.</summary>
    OutputFull,

    /// <summary>Has its inputs but its power network gives it nothing (FB-005).</summary>
    NoPower,
}

/// <summary>
/// Items waiting to be pushed into neighbouring buildings, one at a time and neighbours in turn. Machines, import
/// ports and storage share it, so one dump routine serves all three.
/// </summary>
public abstract class OutputBuffer
{
    public SortedDictionary<string, int> Output { get; } = new(StringComparer.Ordinal);
    public int OutputCount { get; internal set; }

    /// <summary>Ticks to wait before looking for somewhere to put output again, after finding nowhere.</summary>
    public int DumpWait { get; internal set; }

    /// <summary>Where the next output search starts around the perimeter, so neighbours take turns.</summary>
    public int DumpCursor { get; internal set; }

    internal void AddOutput(string item, int n)
    {
        Output[item] = Output.GetValueOrDefault(item) + n;
        OutputCount += n;
    }
}

/// <summary>
/// A machine's working state: input buffer, recipe progress and output buffer. Import ports use the output
/// buffer too (they have no recipe), so one dump routine feeds belts from both.
/// </summary>
public sealed class MachineState(RecipeDef? recipe) : OutputBuffer
{
    /// <summary>Most items a machine holds before it stops taking input or producing.</summary>
    public const int OutputCapacity = 10;

    /// <summary>A machine buffers this many batches of each input.</summary>
    public const int InputBatches = 2;

    /// <summary><see cref="Progress"/> units per tick at base speed and full power.</summary>
    public const int ProgressPerTick = 100;

    public RecipeDef? Recipe { get; } = recipe;

    /// <summary>Items one finished batch adds to the output buffer.</summary>
    public int OutputPerBatch { get; } = recipe?.Outputs.Values.Sum() ?? 0;

    public bool Working { get; internal set; }

    /// <summary>
    /// Recipe progress in hundredths of a tick: each tick adds <see cref="ProgressPerTick"/> scaled by booster speed
    /// (FB-016) and power (FB-005); the batch finishes at <c>Recipe.Ticks * ProgressPerTick</c>.
    /// </summary>
    public int Progress { get; internal set; }

    /// <summary>Percent of the current batch done.</summary>
    public int ProgressPercent => Recipe == null ? 0 : Progress / Recipe.Ticks;

    /// <summary>
    /// Booster output bonus (FB-016) carried between batches, in percent of a batch: each batch adds the bonus and
    /// every full 100 makes one extra batch of output.
    /// </summary>
    public int OutputCredit { get; internal set; }

    public SortedDictionary<string, int> Input { get; } = new(StringComparer.Ordinal);

    /// <summary>Consecutive ticks the recipe hasn't advanced, so the UI can ignore short gaps between batches.</summary>
    public int IdleTicks { get; internal set; }

    /// <summary>
    /// Power this machine got last tick, in permille of what it asked for (FB-005). 1000 for machines that draw none.
    /// Read from its network, so not part of the state hash.
    /// </summary>
    public int PowerPermille => Draws ? Net!.Satisfaction : 1000;

    /// <summary>Its power network, if it has a part in power; set when the tile's networks are rebuilt.</summary>
    internal PowerNetwork? Net;

    /// <summary>It draws power, so it runs at its network's satisfaction.</summary>
    internal bool Draws;

    // Power figures cached when networks are rebuilt (draw after boosters, supply while burning), and the ones last
    // reported to the network (PowerNetwork.Report).
    internal int BoostedDraw, MaxSupply, ReportedDraw, ReportedSupply;

    /// <summary>Its boosted stats (FB-016), refreshed whenever the tile's layout changes. Derived, not hashed.</summary>
    public BoostStats Boost
    {
        get => _boost;
        internal set
        {
            _boost = value;
            SpeedPercent = 100 + value.Speed;
            OutputBonus = value.Output;
        }
    }
    BoostStats _boost = BoostStats.Base;

    /// <summary>Boosted recipe speed and extra output, in percent, cached from <see cref="Boost"/> for the per-tick work.</summary>
    internal int SpeedPercent = 100, OutputBonus;

    public MachineStatus Status =>
        Recipe == null ? MachineStatus.Idle
        : Working ? (PowerPermille == 0 ? MachineStatus.NoPower : MachineStatus.Working)
        : OutputCount + OutputPerBatch > OutputCapacity ? MachineStatus.OutputFull
        : MachineStatus.Starved;

    /// <summary>The first input the recipe is short of, or null.</summary>
    public string? MissingInput =>
        Recipe?.Inputs.Where(kv => Input.GetValueOrDefault(kv.Key) < kv.Value).Select(kv => kv.Key).FirstOrDefault();
}

/// <summary>A storage building's contents (FB-007): any items up to <see cref="Capacity"/>, unloaded onto adjacent belts.</summary>
public sealed class StorageState(int capacity) : OutputBuffer
{
    public int Capacity { get; } = capacity;
}

/// <summary>A battery's charge (FB-005), in power-ticks.</summary>
public sealed class BatteryState(long capacity)
{
    /// <summary>Full charge, in power-ticks (the def's power-seconds times <c>Simulation.TicksPerSecond</c>).</summary>
    public long Capacity { get; } = capacity;
    public long Charge { get; internal set; }
}

/// <summary>An item held by a junction, router or bridge until <see cref="ReadyTick"/>.</summary>
/// <param name="From">Junctions: the direction it travels. Routers: the building id it came from. Bridges: unused.</param>
public readonly record struct RelayItem(string ItemId, long ReadyTick, int From);

/// <summary>
/// Items passing through a junction, router or bridge (FB-004). Junctions keep one lane per travel direction;
/// routers and bridges use lane 0.
/// </summary>
public sealed class RelayState(int lanes)
{
    public const int JunctionCapacity = 4, RouterCapacity = 1, BridgeCapacity = 8;

    /// <summary>Ticks an item spends crossing a junction or router.</summary>
    public const int PassTicks = 6;

    /// <summary>Ticks per cell an item spends flying between bridges.</summary>
    public const int BridgeTicksPerCell = 4;

    /// <summary>Oldest first.</summary>
    public List<RelayItem>[] Lanes { get; } = [.. Enumerable.Range(0, lanes).Select(_ => new List<RelayItem>())];

    /// <summary>Routers: which neighbour the next search starts at, so they take turns.</summary>
    public int Cursor { get; internal set; }

    public int Count => Lanes.Sum(l => l.Count);
}

/// <summary>A sorter's setting (FB-004): which item goes straight on, and which side the next other item tries first.</summary>
public sealed class SorterState
{
    /// <summary>The item let through straight ahead; null lets nothing through, so everything turns aside.</summary>
    public string? Filter { get; internal set; }

    /// <summary>Other items alternate between turning left and right.</summary>
    public bool Flip { get; internal set; }
}

/// <summary>One item travelling between tiles; it lands at <see cref="ArriveTick"/>.</summary>
public readonly record struct Shipment(long ArriveTick, string ItemId);

/// <summary>An export port's link to the neighbouring tile (IO-002).</summary>
public sealed class ExportState
{
    /// <summary>Items in flight at once. Transit is long enough that this never limits a busy belt.</summary>
    public const int LinkCapacity = 64;

    /// <summary>Oldest first. Transit time is fixed, so arrival order is send order.</summary>
    public Queue<Shipment> InFlight { get; } = new();

    /// <summary>Index into the neighbour's import ports where the next delivery search starts.</summary>
    public int Cursor { get; internal set; }
}

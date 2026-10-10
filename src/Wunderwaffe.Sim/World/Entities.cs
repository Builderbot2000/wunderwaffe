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
}

/// <summary>
/// A machine's working state: input buffer, recipe progress and output buffer. Import ports use the output
/// buffer too (they have no recipe), so one dump routine feeds belts from both.
/// </summary>
public sealed class MachineState(RecipeDef? recipe)
{
    /// <summary>Most items a machine holds before it stops taking input or producing.</summary>
    public const int OutputCapacity = 10;

    /// <summary>A machine buffers this many batches of each input.</summary>
    public const int InputBatches = 2;

    public RecipeDef? Recipe { get; } = recipe;

    /// <summary>Items one finished batch adds to the output buffer.</summary>
    public int OutputPerBatch { get; } = recipe?.Outputs.Values.Sum() ?? 0;

    public bool Working { get; internal set; }
    public int Progress { get; internal set; }
    public SortedDictionary<string, int> Input { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Output { get; } = new(StringComparer.Ordinal);
    public int OutputCount { get; internal set; }

    /// <summary>Consecutive ticks the recipe hasn't run, so the UI can ignore short gaps between batches.</summary>
    public int IdleTicks { get; internal set; }

    public MachineStatus Status =>
        Recipe == null ? MachineStatus.Idle
        : Working ? MachineStatus.Working
        : OutputCount + OutputPerBatch > OutputCapacity ? MachineStatus.OutputFull
        : MachineStatus.Starved;

    /// <summary>The first input the recipe is short of, or null.</summary>
    public string? MissingInput =>
        Recipe?.Inputs.Where(kv => Input.GetValueOrDefault(kv.Key) < kv.Value).Select(kv => kv.Key).FirstOrDefault();

    /// <summary>Ticks to wait before looking for somewhere to put output again, after finding nowhere.</summary>
    public int DumpWait { get; internal set; }

    /// <summary>Where the next output search starts around the perimeter, so neighbours take turns.</summary>
    public int DumpCursor { get; internal set; }
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

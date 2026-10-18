using System.Numerics;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// The machines of a tile that the entity step skips (NF-001). A machine falls asleep after a tick in which its recipe
/// didn't advance and it held no output: it couldn't start (no inputs, mined out), or it is mid-batch with no power.
/// Until something changes, every later tick would only count it idle. It wakes when an item goes into it, when its
/// power changes, or when the tile's layout changes (which covers boosters, deposits and snapshot restores).
/// A sleeper's <see cref="MachineState.IdleTicks"/> keep counting from <see cref="Steps"/>, so the state and its hash are
/// exactly what stepping every machine every tick gives. Derived, not hashed or saved: a fresh one is all awake.
/// </summary>
internal sealed class MachineSleep
{
    // One bit per slot (index in TileGrid.Machines), set while awake.
    ulong[] _awake = [];

    // Sleepers that are mid-batch, checked each step for a change of power.
    readonly List<MachineState> _unpowered = [];

    int _revision = -1;

    // The slot being stepped, or int.MaxValue between steps: a sleeper after it hasn't had this step's turn yet.
    int _cursor = int.MaxValue;

    /// <summary>Steps every machine every tick, for tests that check sleeping changes nothing.</summary>
    internal bool Off;

    /// <summary>Set when the machine list is replaced without a revision change (snapshot restore).</summary>
    internal bool Dirty = true;

    /// <summary>Entity steps this tile has taken in full sim, since the grid was made.</summary>
    internal long Steps { get; private set; }

    /// <summary>Starts a step: wakes everything if the layout changed, then wakes sleepers whose power changed.</summary>
    internal void Begin(TileGrid grid)
    {
        if (Dirty || _revision != grid.Revision)
            Reset(grid);
        Steps++;
        _cursor = -1;
        int kept = 0;
        for (int i = 0; i < _unpowered.Count; i++)
        {
            var m = _unpowered[i];
            if (m.Bed != this)
                m.InUnpowered = false;
            else if (m.PowerPermille != m.SleptPower)
            {
                m.InUnpowered = false;
                Wake(m);
            }
            else
                _unpowered[kept++] = m;
        }
        _unpowered.RemoveRange(kept, _unpowered.Count - kept);
    }

    internal void End() => _cursor = int.MaxValue;

    /// <summary>The first awake slot at or after <paramref name="from"/>, or -1. Reads the bits live, so a machine woken
    /// later in the order this step still gets its turn, as it would have without sleeping.</summary>
    internal int NextAwake(int from)
    {
        for (int w = from >> 6; w < _awake.Length; w++)
        {
            ulong bits = _awake[w];
            if (w == from >> 6)
                bits &= ~0UL << (from & 63);
            if (bits != 0)
                return (w << 6) + BitOperations.TrailingZeroCount(bits);
        }
        return -1;
    }

    /// <summary>Marks the slot being stepped; see <see cref="IdleOf"/>.</summary>
    internal void Visit(int slot) => _cursor = slot;

    /// <summary>Puts the machine just stepped to sleep.</summary>
    internal void Sleep(MachineState m)
    {
        m.Bed = this;
        m.SleptAt = Steps;
        m.SleptPower = m.PowerPermille;
        _awake[m.Slot >> 6] &= ~(1UL << (m.Slot & 63));
        if (m.Working && !m.InUnpowered)
        {
            m.InUnpowered = true;
            _unpowered.Add(m);
        }
    }

    /// <summary>Wakes a sleeper, fixing its idle count to what it is now.</summary>
    internal void Wake(MachineState m)
    {
        m.SetIdle(IdleOf(m));
        m.Bed = null;
        _awake[m.Slot >> 6] |= 1UL << (m.Slot & 63);
    }

    /// <summary>
    /// A sleeper's idle ticks: one more for every step since it fell asleep in which it has had its turn.
    /// </summary>
    internal int IdleOf(MachineState m) =>
        (int)Math.Min(int.MaxValue, m.SleptIdle + Steps - m.SleptAt - (m.Slot > _cursor ? 1 : 0));

    void Reset(TileGrid grid)
    {
        var machines = grid.Machines;
        foreach (var b in machines)
        {
            var m = b.Machine!;
            if (m.Bed == this)
                m.SetIdle(IdleOf(m));
            m.Bed = null;
            m.InUnpowered = false;
        }
        _unpowered.Clear();
        _awake = new ulong[(machines.Count + 63) >> 6];
        for (int i = 0; i < machines.Count; i++)
        {
            machines[i].Machine!.Slot = i;
            _awake[i >> 6] |= 1UL << (i & 63);
        }
        _revision = grid.Revision;
        Dirty = false;
    }
}

namespace Wunderwaffe.Sim.World;

/// <summary>Why a weapon is or isn't firing, for the warning the UI shows (CB-004).</summary>
public enum WeaponStatus
{
    /// <summary>Loaded and waiting for something to shoot at.</summary>
    NoTarget,
    Firing,

    /// <summary>Has a target but no ammo loaded or in reach.</summary>
    NoAmmo,

    /// <summary>A power weapon (no ammo item) on a tile whose power doesn't reach it.</summary>
    NoPower,

    /// <summary>The unit's orders or stance forbid firing.</summary>
    HoldFire,
}

/// <summary>
/// Runtime combat state of one weapon, shield or repair block, on a tile or riding on a unit. Which fields matter
/// depends on the block's kind.
/// </summary>
public sealed class CombatState
{
    /// <summary>The item this weapon loads as ammo, or null (a power weapon, or not a weapon).</summary>
    public string? AmmoItem { get; init; }

    /// <summary>Shots loaded (weapons with an ammo item).</summary>
    public int Ammo { get; internal set; }

    /// <summary>Ticks until the weapon may fire again.</summary>
    public int Cooldown { get; internal set; }

    /// <summary>Shield charge: damage it can still absorb.</summary>
    public int Charge { get; internal set; }

    public WeaponStatus Status { get; internal set; }

    /// <summary>What it is aiming at, if anything (see <see cref="Target"/>).</summary>
    public Target Target { get; internal set; } = Target.None;

    /// <summary>Hit points restored but not yet paid for (repair blocks pay per <c>RepairDef.Per</c>).</summary>
    internal int RepairDebt { get; set; }
}

/// <summary>
/// Something a weapon can aim at: a block of a unit (<see cref="Unit"/> &gt; 0) or a building on a tile.
/// Held by id, so a destroyed target just stops resolving.
/// </summary>
public readonly record struct Target(int Unit, int Block, int Tile, int Building)
{
    public static readonly Target None = new(0, -1, -1, 0);

    public bool IsNone => Unit == 0 && Building == 0;
}

/// <summary>
/// The dead remains of a destroyed building or block (CB-006). Units pass over it, buildings can't be placed on it,
/// and removing it like a building returns <see cref="Balance.WreckRefundPercent"/> of its cost.
/// </summary>
public sealed record Wreck(int Id, string DefId, int Owner, int X, int Y, int Width, int Height, int Rotation);

/// <summary>A shot in flight, in world sub-cells (see <see cref="Unit.CellScale"/>). A beam lives for one tick.</summary>
public sealed class Projectile
{
    public int Id { get; init; }

    /// <summary>The firing player: their own blocks don't stop it.</summary>
    public int Owner { get; init; }

    /// <summary>The weapon's building id, for the view's colour.</summary>
    public string WeaponId { get; init; } = "";

    public long X { get; internal set; }
    public long Y { get; internal set; }

    /// <summary>Where it was fired from, for drawing beams and for shields (a shot fired inside a bubble isn't stopped).</summary>
    public long FromX { get; init; }
    public long FromY { get; init; }

    /// <summary>Movement per tick. For a beam, the whole way to its end.</summary>
    public long VX { get; init; }
    public long VY { get; init; }

    public int Damage { get; init; }
    public string Type { get; init; } = "";
    public bool Beam { get; init; }

    /// <summary>Ticks it still flies before it falls harmlessly.</summary>
    public int Life { get; internal set; }

    /// <summary>The unit that fired it, or 0 for a turret; for the attack alert.</summary>
    public int Shooter { get; init; }

    /// <summary>Spent: hit something or ran out. Removed at the end of the combat step; beams stay one tick to be drawn.</summary>
    public bool Done { get; internal set; }
}

using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim;

/// <summary>Partial: combat state that belongs to the whole world rather than a tile or unit (CB-001).</summary>
public sealed partial class Simulation
{
    readonly List<Projectile> _projectiles = [];
    int _nextProjectileId = 1;

    /// <summary>Last tick each (tile, victim) raised an under-attack alert; throttles the alert only, so it isn't hashed.</summary>
    readonly Dictionary<(int Tile, int Player), long> _attackAlerts = [];

    /// <summary>Shots in flight, in id order. Spent ones (hits, and beams fired this tick) stay until the next combat step so views can draw them.</summary>
    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    internal int NextProjectileId => _nextProjectileId;

    internal void AddProjectile(Projectile p)
    {
        _projectiles.Add(p);
        _nextProjectileId++;
    }

    internal void RemoveSpentProjectiles() => _projectiles.RemoveAll(p => p.Done);

    /// <summary>True at most once per <paramref name="ticks"/> per tile and player: whether to raise an alert now.</summary>
    internal bool ThrottleAlert(int tileId, int player, int ticks)
    {
        if (_attackAlerts.TryGetValue((tileId, player), out long last) && Tick - last < ticks)
            return false;
        _attackAlerts[(tileId, player)] = Tick;
        return true;
    }
}

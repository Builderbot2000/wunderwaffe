using System.Text;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Core;

/// <summary>
/// FNV-1a hash of a simulation's full state, for desync detection, save checks and determinism tests. Anything new
/// that is sim state must be mixed in here, or two diverged games can hash the same.
/// </summary>
internal sealed class StateHash
{
    const ulong Prime = 1099511628211UL;
    ulong _h = 14695981039346656037UL;

    public static ulong Of(Simulation sim)
    {
        var s = new StateHash();
        s.Mix(sim.Tick);
        foreach (var t in sim.Tiles)
            s.MixTile(t);
        foreach (var p in sim.Players)
            s.MixPlayer(p);
        foreach (var u in sim.Units)
            s.MixUnit(u);
        s.MixProjectiles(sim);
        return s._h;
    }

    void Mix(long v)
    {
        for (int i = 0; i < 8; i++)
        {
            _h ^= (byte)(v >> (i * 8));
            _h *= Prime;
        }
    }

    void Mix(string? s)
    {
        if (s == null)
        {
            Mix(-1);
            return;
        }
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            _h ^= b;
            _h *= Prime;
        }
        Mix(s.Length);
    }

    void Mix<TCount>(IEnumerable<KeyValuePair<string, TCount>> items) where TCount : IConvertible
    {
        foreach (var (id, n) in items)
        {
            Mix(id);
            Mix(n.ToInt64(null));
        }
    }

    void MixTile(TileState t)
    {
        Mix(t.Id);
        Mix(t.OriginX);
        Mix(t.OriginY);
        Mix(t.Owner);
        var g = t.Grid;
        Mix(g.FrameCount);
        for (int y = 0; g.FrameCount > 0 && y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (g.HasFrame(x, y))
                    Mix(y * g.Width + x);
            }
        }
        Mix(t.Inventory.Entries);
        foreach (var b in g.Buildings)
        {
            Mix(b.Id);
            Mix(b.DefId);
            Mix(b.X);
            Mix(b.Y);
            Mix(b.Rotation);
            Mix(b.RecipeId);
            Mix(b.Owner);
            Mix(b.Hp);
            MixEntity(b);
        }
        foreach (var w in g.Wrecks)
        {
            Mix(w.Id);
            Mix(w.DefId);
            Mix(w.Owner);
            Mix(w.Y * g.Width + w.X);
            Mix(w.Rotation);
        }
    }

    void MixEntity(PlacedBuilding b)
    {
        if (b.Belt is { } belt)
        {
            Mix(belt.Count);
            for (int i = 0; i < belt.Count; i++)
            {
                Mix(belt.Items[i]);
                Mix(belt.Positions[i]);
            }
        }
        if (b.Machine is { } m)
        {
            Mix(m.Working ? m.Progress + 1 : 0);
            Mix(m.IdleTicks);
            Mix(m.DumpWait);
            Mix(m.DumpCursor);
            Mix(m.Input);
            Mix(m.Output);
        }
        if (b.Export is { } link)
        {
            Mix(link.Cursor);
            foreach (var s in link.InFlight)
            {
                Mix(s.ArriveTick);
                Mix(s.ItemId);
            }
        }
        MixCombat(b.Combat);
    }

    void MixPlayer(PlayerState p)
    {
        Mix(p.Id);
        Mix(p.Produced);
        Mix(p.Shipped);
        Mix(p.Launched);
        foreach (long t in p.GoalTicks)
            Mix(t);
    }

    void MixUnit(Unit u)
    {
        Mix(u.Id);
        Mix(u.Owner);
        Mix(u.TileId);
        Mix(u.X);
        Mix(u.Y);
        Mix(u.VX);
        Mix(u.VY);
        Mix(u.Angle);
        Mix(u.AngVel);
        Mix(u.PathIndex);
        foreach (var blk in u.Blocks)
        {
            Mix(blk.DefId);
            Mix(blk.GX);
            Mix(blk.GY);
            Mix(blk.Rotation);
            Mix(blk.Hp);
        }
        foreach (var c in u.BlockCombat)
            MixCombat(c);
        Mix(u.Cargo.Entries);
        if (u.AttackOrder is { } order)
        {
            Mix((int)order.Kind);
            Mix(order.TileId);
            Mix(order.X);
            Mix(order.Y);
            Mix(order.TargetId);
        }
        if (u.Route is { } route)
        {
            Mix(u.RouteIndex);
            Mix(u.DestTileId);
            Mix(u.DestX);
            Mix(u.DestY);
            foreach (int id in route)
                Mix(id);
        }
        else
        {
            Mix(-1);
        }
    }

    void MixCombat(World.CombatState? c)
    {
        if (c == null)
            return;
        Mix(c.Ammo);
        Mix(c.Cooldown);
        Mix(c.Charge);
        Mix((int)c.Status);
        Mix(c.Target.Unit);
        Mix(c.Target.Block);
        Mix(c.Target.Tile);
        Mix(c.Target.Building);
        Mix(c.RepairDebt);
    }

    void MixProjectiles(Simulation sim)
    {
        Mix(sim.NextProjectileId);
        foreach (var p in sim.Projectiles)
        {
            Mix(p.Id);
            Mix(p.Owner);
            Mix(p.X);
            Mix(p.Y);
            Mix(p.VX);
            Mix(p.VY);
            Mix(p.FromX);
            Mix(p.FromY);
            Mix(p.Damage);
            Mix(p.Type);
            Mix(p.Life);
            Mix(p.Shooter);
            Mix(p.Done ? 1 : 0);
        }
    }
}

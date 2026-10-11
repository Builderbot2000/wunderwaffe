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
        Mix(t.Stats.TotalMade);
        Mix(t.Stats.TotalUsed);
        foreach (var (partner, n) in t.Stats.TotalSent)
        {
            Mix(partner);
            Mix(n);
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
        if (b.Machine is { } mc)
            Mix(mc.OutputCredit);
        if (b.Storage is { } store)
        {
            Mix(store.DumpWait);
            Mix(store.DumpCursor);
            Mix(store.Output);
        }
        if (b.Battery is { } bat)
            Mix(bat.Charge);
        if (b.Relay is { } relay)
        {
            Mix(relay.Cursor);
            foreach (var lane in relay.Lanes)
            {
                Mix(lane.Count);
                foreach (var r in lane)
                {
                    Mix(r.ItemId);
                    Mix(r.ReadyTick);
                    Mix(r.From);
                }
            }
        }
        if (b.Sorter is { } sorter)
        {
            Mix(sorter.Filter);
            Mix(sorter.Flip ? 1 : 0);
        }
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
}

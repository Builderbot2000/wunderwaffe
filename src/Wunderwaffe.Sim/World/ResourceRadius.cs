using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// The resource radius (FB-018, CB-004), From the Depths style: cores, storage buildings and units with storage share
/// stock with friendly units and weapons within <see cref="BuildingDef.SupplyRadius"/> cells, with no logistics. A tile
/// supplier shares its tile's inventory; a unit shares its <see cref="Unit.Cargo"/>. Today it carries ammo; power and
/// fuel join when they exist (silo B), and unit upkeep (silo D) pays through <see cref="SupplierFor"/>.
/// Outside every radius a unit lives off its cargo.
/// </summary>
public static class ResourceRadius
{
    const int S = Unit.CellScale;

    /// <summary>Ticks between resupply passes.</summary>
    public const int Interval = 30;

    /// <summary>
    /// The stock that feeds a unit: the first friendly supplier whose radius covers its centre. Tile suppliers come
    /// first (its own tile, then neighbours, in id order), then supplying units in id order. Null outside every radius.
    /// </summary>
    public static Inventory? SupplierFor(Simulation sim, Unit unit) => SupplierAt(sim, unit.Owner, unit.X, unit.Y, unit.TileId, unit);

    /// <summary>Whether any of <paramref name="player"/>'s suppliers reaches a point in world sub-cells (for the UI).</summary>
    public static bool Covers(Simulation sim, int player, long worldX, long worldY)
    {
        int gx = (int)IntMath.FloorDiv(worldX, S), gy = (int)IntMath.FloorDiv(worldY, S);
        return sim.TileAtCell(gx, gy) is { } tile && SupplierAt(sim, player, worldX, worldY, tile.Id, null) != null;
    }

    /// <summary>Centre of a building in world sub-cells.</summary>
    public static (long X, long Y) Centre(TileState tile, PlacedBuilding b) =>
        ((long)(tile.OriginX + b.X) * S + (long)b.Width * S / 2, (long)(tile.OriginY + b.Y) * S + (long)b.Height * S / 2);

    internal static Inventory? SupplierAt(Simulation sim, int player, long x, long y, int tileId, Unit? except)
    {
        var tile = sim.Tiles[tileId];
        if (TileSupplies(tile, player, x, y))
            return tile.Inventory;
        foreach (var n in sim.Neighbors(tile))
        {
            if (TileSupplies(n, player, x, y))
                return n.Inventory;
        }
        foreach (var u in sim.Units)
        {
            if (u != except && u.Owner == player && u.SupplyRadius > 0 && Within(u.X, u.Y, x, y, u.SupplyRadius))
                return u.Cargo;
        }
        return null;
    }

    /// <summary>The tile's stock is the owner's, so only an owned tile with a supplier of theirs in reach shares it.</summary>
    static bool TileSupplies(TileState tile, int player, long x, long y)
    {
        if (tile.Owner != player)
            return false;
        foreach (var b in tile.Grid.Suppliers)
        {
            var (cx, cy) = Centre(tile, b);
            if (b.Owner == player && Within(cx, cy, x, y, b.SupplyRadius))
                return true;
        }
        return false;
    }

    static bool Within(long ax, long ay, long bx, long by, int cells)
    {
        long dx = ax - bx, dy = ay - by, r = (long)cells * S;
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>
    /// Every <see cref="Interval"/> ticks: units top up their weapons' magazines from their cargo, then from a supplier
    /// in reach, and fill their cargo with the ammo their weapons use; turrets in reach of a supplier top up too.
    /// </summary>
    internal static void Step(Simulation sim)
    {
        if (sim.Tick % Interval != 0)
            return;
        var defs = sim.Content.Buildings;

        foreach (var u in sim.Units)
        {
            // Abstract units (UC-009) resupply once they materialise.
            if (sim.IsAbstract(u))
                continue;
            Inventory? source = null;
            bool looked = false;
            for (int i = 0; i < u.Blocks.Count; i++)
            {
                var blk = u.Blocks[i];
                if (blk.Hp <= 0 || defs[blk.DefId].Weapon is not { Ammo: { } ammo } w)
                    continue;
                var state = u.BlockCombat[i]!;
                state.Ammo += Take(u.Cargo, ammo, w.Magazine - state.Ammo);
                if (state.Ammo < w.Magazine)
                {
                    if (!looked)
                        (source, looked) = (SupplierFor(sim, u), true);
                    if (source != null)
                        state.Ammo += Take(source, ammo, w.Magazine - state.Ammo);
                }
            }

            if (u.CargoCapacity <= 0)
                continue;
            if (!looked)
                source = SupplierFor(sim, u);
            if (source == null)
                continue;
            // Fill the cargo with what the weapons fire, in block order, sharing the room.
            foreach (var blk in u.Blocks)
            {
                if (blk.Hp <= 0 || defs[blk.DefId].Weapon is not { Ammo: { } ammo })
                    continue;
                long room = u.CargoCapacity - u.Cargo.Entries.Sum(e => e.Value);
                if (room <= 0)
                    break;
                u.Cargo.Add(ammo, Take(source, ammo, room));
            }
        }

        foreach (var tile in sim.Tiles)
        {
            if (tile.Owner == TileState.Unowned)
                continue;
            foreach (var b in tile.Grid.CombatBuildings)
            {
                if (defs[b.DefId].Weapon is not { Ammo: { } ammo } w || b.Combat!.Ammo >= w.Magazine)
                    continue;
                var (cx, cy) = Centre(tile, b);
                if (SupplierAt(sim, b.Owner, cx, cy, tile.Id, null) is { } source)
                    b.Combat.Ammo += Take(source, ammo, w.Magazine - b.Combat.Ammo);
            }
        }
    }

    /// <summary>Takes up to <paramref name="want"/> of an item; returns how many it got.</summary>
    internal static int Take(Inventory from, string item, long want)
    {
        int n = (int)Math.Min(Math.Max(0, want), from.Get(item));
        if (n > 0)
            from.Add(item, -n);
        return n;
    }
}

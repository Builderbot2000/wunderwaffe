using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// One build radius (WM-015): cells at most <paramref name="Radius"/> from the area (nearest cells, straight-line, as a
/// power node's radius) lie inside. A major building's footprint (<paramref name="BuildingId"/>), or a flying unit's
/// centre cell (<paramref name="UnitId"/>, 1x1). Tile-local cells; only the tile's own cells count.
/// </summary>
public readonly record struct BuildRadius(int X, int Y, int Width, int Height, int Radius, int BuildingId, int UnitId)
{
    /// <summary>Whether local cell (x, y) lies within the radius.</summary>
    public bool Covers(int x, int y)
    {
        long dx = Math.Max(0, Math.Max(X - x, x - (X + Width - 1)));
        long dy = Math.Max(0, Math.Max(Y - y, y - (Y + Height - 1)));
        return dx * dx + dy * dy <= (long)Radius * Radius;
    }
}

/// <summary>
/// Who may act where (WM-015) and when a tile is Contested (WM-004); who pays is <see cref="Funds"/>. Read-only queries shared by placement,
/// landing, the UI and the other silos; <see cref="DestroyBuilding"/> is the one write.
/// </summary>
public static class Territory
{
    /// <summary>
    /// A player has presence on a tile they own or where they have a building or a unit (WM-015). Building and landing
    /// need it, so a player's first foothold on a new tile is a unit flying in.
    /// </summary>
    public static bool HasPresence(Simulation sim, int player, TileState tile)
    {
        if (tile.Owner == player)
            return true;
        foreach (var u in sim.Units)
        {
            if (u.Owner == player && u.TileId == tile.Id)
                return true;
        }
        foreach (var b in tile.Grid.Buildings)
        {
            if (b.Owner == player)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True if the footprint touches a circle owned by someone else while a building not of <paramref name="player"/>'s
    /// stands in it: nobody builds or lands there until those buildings are destroyed (WM-015, UD-011, CB-003).
    /// </summary>
    public static bool EnemyCircleBlocks(TileState tile, int player, int x, int y, int w, int h)
    {
        if (tile.Owner == TileState.Unowned || tile.Owner == player || !tile.InCircle(x, y, w, h))
            return false;
        return Expansion.CircleOccupants(tile).Any(p => p != player);
    }

    /// <summary>
    /// Every build radius <paramref name="player"/> has on the tile (WM-015, OQ-58): their major buildings there (a
    /// <see cref="BuildingDef.BuildRadius"/> above 0) in id order, then their flying units on the tile in id order, sized
    /// by <see cref="UnitStats.BuildRadius(Unit)"/>. Radii only cover their own tile's cells.
    /// </summary>
    public static List<BuildRadius> BuildRadii(Simulation sim, int player, TileState tile)
    {
        var radii = new List<BuildRadius>();
        foreach (var b in Majors(tile))
        {
            if (b.Owner == player)
                radii.Add(new BuildRadius(b.X, b.Y, b.Width, b.Height, b.Def.BuildRadius, b.Id, -1));
        }
        foreach (var u in sim.Units)
        {
            if (u.Owner != player || u.TileId != tile.Id || UnitStats.BuildRadius(u) is not (> 0 and var r))
                continue;
            int x = IntMath.FloorDiv(u.X, Unit.CellScale) - tile.OriginX, y = IntMath.FloorDiv(u.Y, Unit.CellScale) - tile.OriginY;
            radii.Add(new BuildRadius(x, y, 1, 1, r, -1, u.Id));
        }
        return radii;
    }

    /// <summary>
    /// Whether every cell of the w x h area lies within one of <paramref name="player"/>'s build radii on the tile
    /// (WM-015). Every building operation needs it: placing buildings, frames and blueprint blocks
    /// (<see cref="Placement.Check"/> → <see cref="PlaceResult.OutOfRange"/>), removing, and later cut, paste and graft
    /// (FB-023). Landing and lift-off need only presence.
    /// </summary>
    public static bool InBuildRadius(Simulation sim, int player, TileState tile, int x, int y, int w = 1, int h = 1) =>
        Covered(BuildRadii(sim, player, tile), x, y, w, h);

    /// <summary>Whether every cell of the area is covered by one of <paramref name="radii"/>, for callers testing many areas against one set.</summary>
    public static bool Covered(IReadOnlyList<BuildRadius> radii, int x, int y, int w = 1, int h = 1)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                bool any = false;
                foreach (var r in radii)
                {
                    if (r.Covers(cx, cy))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                    return false;
            }
        }
        return true;
    }

    /// <summary>Buildings on the tile with a build radius, cached by <see cref="TileGrid.Revision"/>.</summary>
    static PlacedBuilding[] Majors(TileState tile)
    {
        var grid = tile.Grid;
        if (tile.RadiusCache.Revision != grid.Revision)
            tile.RadiusCache = (grid.Revision, [.. grid.Buildings.Where(b => b.Def.BuildRadius > 0).OrderBy(b => b.Id)]);
        return tile.RadiusCache.Majors;
    }



    /// <summary>
    /// A tile is Contested while a player other than its owner has a unit on it (WM-004, OQ-20); an unowned tile is
    /// Contested while units of two or more players are on it. Exact at any moment: it reads unit positions.
    /// </summary>
    public static bool IsContested(Simulation sim, TileState tile)
    {
        int seen = TileState.Unowned;
        foreach (var u in sim.Units)
        {
            if (u.TileId != tile.Id || u.Owner == tile.Owner)
                continue;
            if (tile.Owner != TileState.Unowned || (seen != TileState.Unowned && seen != u.Owner))
                return true;
            seen = u.Owner;
        }
        return false;
    }

    /// <summary><see cref="IsContested"/> for every tile at once, indexed by tile id, in one pass over the units.</summary>
    public static bool[] ContestedTiles(Simulation sim)
    {
        var contested = new bool[sim.Tiles.Count];
        var seen = new int[sim.Tiles.Count];
        Array.Fill(seen, TileState.Unowned);
        foreach (var u in sim.Units)
        {
            var tile = sim.Tiles[u.TileId];
            if (u.Owner == tile.Owner)
                continue;
            if (tile.Owner != TileState.Unowned || (seen[tile.Id] != TileState.Unowned && seen[tile.Id] != u.Owner))
                contested[tile.Id] = true;
            seen[tile.Id] = u.Owner;
        }
        return contested;
    }

    /// <summary>
    /// Removes a building as destroyed: no refund, whatever it held is lost, and a wreck stays (CB-006, through
    /// <see cref="Combat.DestroyBuilding"/>). Used when a claim wipes a circle (WM-006) and by
    /// <see cref="DebugDestroyCommand"/>. Losing a last core eliminates the player (WM-016); the victory check sees
    /// that from the buildings left.
    /// </summary>
    internal static void DestroyBuilding(Simulation sim, TileState tile, PlacedBuilding b) =>
        Combat.DestroyBuilding(sim, tile, b);
}

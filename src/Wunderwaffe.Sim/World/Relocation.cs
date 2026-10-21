namespace Wunderwaffe.Sim.World;

/// <summary>
/// Building relocation (FB-023, OQ-53): cut a building the player owns and paste it elsewhere on the same tile, owned or
/// not, as itself: same id, type, HP, recipe, settings and held items, neither refunded nor paid for. One command does
/// both, so a refused paste leaves the building where it was and nothing is ever held off the map. A paste onto the
/// frame of one of the player's landed units is a graft: lift-off validation (UD-003, <see cref="UnitAssembly"/>)
/// decides whether the unit can fly it out, and after landing it is an ordinary building that can be cut off again.
/// </summary>
public static class Relocation
{
    /// <summary>
    /// Whether the player may cut the building covering (<paramref name="fromX"/>, <paramref name="fromY"/>) on
    /// <paramref name="tile"/> and paste it with its top-left at (<paramref name="x"/>, <paramref name="y"/>) on
    /// <paramref name="to"/>: theirs, inside one of their build radii where it stands, the same tile, and a spot that
    /// passes <see cref="Placement.Check"/> in its moving mode (radius, bounds, overlap, layers, terrain, port zones,
    /// deposits for an extractor's recipe; no cost). Read-only: the command, the UI ghost and <c>GameQuery</c> share it.
    /// </summary>
    public static PlaceResult Check(Simulation sim, TileState tile, int player, int fromX, int fromY,
        TileState to, int x, int y, int rotation, out PlacementPlan plan)
    {
        plan = default;
        if (tile.Grid.BuildingAt(fromX, fromY) is not { } b)
            return PlaceResult.NoSuchBuilding;
        if (b.Owner != player)
            return PlaceResult.NotOwner;
        if (to != tile)
            return PlaceResult.OtherTile;
        if (!Territory.InBuildRadius(sim, player, tile, b.X, b.Y, b.Width, b.Height))
            return PlaceResult.OutOfRange;
        return Placement.Check(sim, tile, player, b.DefId, x, y, rotation, b.RecipeId, out plan, moving: b);
    }

    internal static PlaceResult Move(Simulation sim, TileState tile, int player, int fromX, int fromY,
        TileState to, int x, int y, int rotation)
    {
        var result = Check(sim, tile, player, fromX, fromY, to, x, y, rotation, out var plan);
        if (result != PlaceResult.Ok)
            return result;
        var b = tile.Grid.BuildingAt(fromX, fromY)!;
        tile.Grid.Move(b, plan.X, plan.Y, plan.Width, plan.Height, plan.Rotation);
        // The Revision bump wakes every sleeper; waking this one says so where it matters.
        if (b.Machine is { } m)
            m.Bed?.Wake(m);
        return PlaceResult.Ok;
    }
}

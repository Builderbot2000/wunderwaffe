using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

public enum PlaceResult
{
    Ok,
    NotOwner,
    NoSuchBuilding,
    OutOfBounds,
    Blocked,
    NeedsDeposit,
    InvalidRecipe,
    CannotAfford,

    /// <summary>Ports only fit wholly inside one I/O zone (see <see cref="PortRules"/>).</summary>
    NotInPortZone,

    /// <summary>Some cell of the footprint is terrain buildings can't stand on, such as water or peaks (FB-002).</summary>
    BadTerrain,
}

/// <summary>A placement that passed every check, ready to apply.</summary>
public readonly record struct PlacementPlan(BuildingDef Def, int X, int Y, int Width, int Height, int Rotation, string? RecipeId);

/// <summary>
/// Building placement rules (FB-002, FB-003). Read-only, so the UI can use the same check to preview
/// placement as the command uses to apply it.
/// </summary>
public static class Placement
{
    public static PlaceResult Check(
        Simulation sim, TileState tile, int player, string buildingId, int x, int y, int rotation,
        string? recipeId, out PlacementPlan plan)
    {
        plan = default;
        var content = sim.Content;
        if (tile.Owner != player)
            return PlaceResult.NotOwner;
        if (!content.Buildings.TryGetValue(buildingId, out var def))
            return PlaceResult.NoSuchBuilding;
        rotation &= 3;
        var (w, h) = def.Footprint(rotation);

        TileGrid grid = tile.Grid;
        if (!grid.InBounds(x, y, w, h))
            return PlaceResult.OutOfBounds;
        if (!grid.IsFree(x, y, w, h))
            return PlaceResult.Blocked;
        if (!grid.Buildable(x, y, w, h))
            return PlaceResult.BadTerrain;

        if (def.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort && PortRules.ZoneOf(grid, x, y, w, h) < 0)
            return PlaceResult.NotInPortZone;

        var resolved = ResolveRecipe(sim, grid, def, x, y, w, h, recipeId);
        if (resolved.Error != PlaceResult.Ok)
            return resolved.Error;

        if (!tile.Inventory.Has(def.Cost))
            return PlaceResult.CannotAfford;

        plan = new PlacementPlan(def, x, y, w, h, rotation, resolved.RecipeId);
        return PlaceResult.Ok;
    }

    readonly record struct Resolved(PlaceResult Error, string? RecipeId);

    static Resolved ResolveRecipe(Simulation sim, TileGrid grid, BuildingDef def, int x, int y, int w, int h, string? recipeId)
    {
        var all = sim.Content.RecipesByBuilding.GetValueOrDefault(def.Id) ?? [];

        if (recipeId != null)
        {
            var recipe = all.FirstOrDefault(r => r.Id == recipeId);
            if (recipe == null)
                return new(PlaceResult.InvalidRecipe, null);
            if (recipe.Inputs.Count == 0 && CountDeposits(grid, x, y, w, h, recipe.Outputs.Keys.First()) == 0)
                return new(PlaceResult.NeedsDeposit, null);
            return new(PlaceResult.Ok, recipe.Id);
        }

        var extraction = all.Where(r => r.Inputs.Count == 0).ToList();
        if (extraction.Count > 0)
        {
            // Mine whichever extractable deposit covers most of the footprint; ties go to the lowest id.
            RecipeDef? best = null;
            int bestCount = 0;
            foreach (var r in extraction)
            {
                int n = CountDeposits(grid, x, y, w, h, r.Outputs.Keys.First());
                if (n > bestCount)
                {
                    best = r;
                    bestCount = n;
                }
            }
            return best == null ? new(PlaceResult.NeedsDeposit, null) : new(PlaceResult.Ok, best.Id);
        }

        return all.Count switch
        {
            0 => new(PlaceResult.Ok, null),
            1 => new(PlaceResult.Ok, all[0].Id),
            _ => new(PlaceResult.InvalidRecipe, null),
        };
    }

    static int CountDeposits(TileGrid grid, int x, int y, int w, int h, string itemId)
    {
        int n = 0;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (grid.DepositAt(cx, cy) == itemId)
                    n++;
            }
        }
        return n;
    }
}

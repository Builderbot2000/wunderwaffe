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
        ContentDb content, TileState tile, int player, string buildingId, int x, int y, int rotation,
        string? recipeId, out PlacementPlan plan)
    {
        plan = default;
        if (tile.Owner != player)
            return PlaceResult.NotOwner;
        if (!content.Buildings.TryGetValue(buildingId, out var def))
            return PlaceResult.NoSuchBuilding;

        rotation &= 3;
        bool turned = (rotation & 1) == 1;
        int w = turned ? def.Height : def.Width;
        int h = turned ? def.Width : def.Height;

        TileGrid grid = tile.Grid;
        if (!grid.InBounds(x, y, w, h))
            return PlaceResult.OutOfBounds;
        if (!grid.IsFree(x, y, w, h))
            return PlaceResult.Blocked;

        var resolved = ResolveRecipe(content, grid, def, x, y, w, h, recipeId);
        if (resolved.Error != PlaceResult.Ok)
            return resolved.Error;

        if (!tile.Inventory.Has(def.Cost))
            return PlaceResult.CannotAfford;

        plan = new PlacementPlan(def, x, y, w, h, rotation, resolved.RecipeId);
        return PlaceResult.Ok;
    }

    readonly record struct Resolved(PlaceResult Error, string? RecipeId);

    static Resolved ResolveRecipe(ContentDb content, TileGrid grid, BuildingDef def, int x, int y, int w, int h, string? recipeId)
    {
        var recipes = content.RecipesByBuilding.GetValueOrDefault(def.Id) ?? [];

        if (recipeId != null)
        {
            var recipe = recipes.FirstOrDefault(r => r.Id == recipeId);
            if (recipe == null)
                return new(PlaceResult.InvalidRecipe, null);
            if (recipe.Inputs.Count == 0 && CountDeposits(grid, x, y, w, h, recipe.Outputs.Keys.First()) == 0)
                return new(PlaceResult.NeedsDeposit, null);
            return new(PlaceResult.Ok, recipe.Id);
        }

        var extraction = recipes.Where(r => r.Inputs.Count == 0).ToList();
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

        return recipes.Count switch
        {
            0 => new(PlaceResult.Ok, null),
            1 => new(PlaceResult.Ok, recipes[0].Id),
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

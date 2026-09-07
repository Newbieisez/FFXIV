namespace EZBuddy.Core.Procurement;

public enum ProcurementAction
{
    Craft,
    Gather,
    Vendor,
    CurrencyExchange,
    Marketboard,
    Manual
}

public sealed record ProcurementIngredient(uint ItemId, int Quantity);

public sealed record ProcurementRecipe(
    uint OutputItemId,
    int OutputQuantity,
    IReadOnlyList<ProcurementIngredient> Ingredients)
{
    public void Validate()
    {
        if (OutputItemId == 0 || OutputQuantity <= 0 || Ingredients.Any(x => x.ItemId == 0 || x.Quantity <= 0))
        {
            throw new InvalidDataException("Invalid procurement recipe.");
        }
    }
}

public sealed record ProcurementSource(
    uint ItemId,
    string Key,
    ProcurementAction Action,
    int Priority,
    bool Enabled = true,
    bool RequiresExplicitSpendApproval = false);

public sealed record ProcurementPlanStep(
    uint ItemId,
    int Quantity,
    ProcurementAction Action,
    string SourceKey,
    string Reason);

public sealed record ProcurementPlan(
    uint TargetItemId,
    int TargetQuantity,
    IReadOnlyList<ProcurementPlanStep> Steps,
    IReadOnlyList<string> Blockers)
{
    public bool IsComplete => Blockers.Count == 0;
}

public static class ProcurementPlanner
{
    public static ProcurementPlan Build(
        uint targetItemId,
        int targetQuantity,
        IReadOnlyDictionary<uint, int> ownedQuantities,
        IEnumerable<ProcurementRecipe> recipes,
        IEnumerable<ProcurementSource> sources)
    {
        if (targetItemId == 0 || targetQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetItemId));
        }

        ArgumentNullException.ThrowIfNull(ownedQuantities);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(sources);

        var recipeMap = recipes.ToDictionary(recipe => recipe.OutputItemId);
        foreach (var recipe in recipeMap.Values)
        {
            recipe.Validate();
        }

        var sourceMap = sources
            .Where(source => source.Enabled)
            .GroupBy(source => source.ItemId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(source => source.Priority).ToArray());

        var remainingOwned = ownedQuantities.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value));
        var steps = new List<ProcurementPlanStep>();
        var blockers = new List<string>();
        var stack = new HashSet<uint>();

        Require(targetItemId, targetQuantity, remainingOwned, recipeMap, sourceMap, steps, blockers, stack);
        return new ProcurementPlan(targetItemId, targetQuantity, steps, blockers);
    }

    private static void Require(
        uint itemId,
        int quantity,
        Dictionary<uint, int> owned,
        IReadOnlyDictionary<uint, ProcurementRecipe> recipes,
        IReadOnlyDictionary<uint, ProcurementSource[]> sources,
        List<ProcurementPlanStep> steps,
        List<string> blockers,
        HashSet<uint> stack)
    {
        if (quantity <= 0)
        {
            return;
        }

        var available = owned.GetValueOrDefault(itemId);
        var use = Math.Min(available, quantity);
        if (use > 0)
        {
            owned[itemId] = available - use;
            quantity -= use;
        }

        if (quantity <= 0)
        {
            return;
        }

        if (!stack.Add(itemId))
        {
            blockers.Add($"Procurement recipe cycle detected at item {itemId}.");
            return;
        }

        try
        {
            if (recipes.TryGetValue(itemId, out var recipe))
            {
                var crafts = (int)Math.Ceiling(quantity / (double)recipe.OutputQuantity);
                foreach (var ingredient in recipe.Ingredients)
                {
                    Require(ingredient.ItemId, checked(ingredient.Quantity * crafts), owned, recipes, sources, steps, blockers, stack);
                }

                steps.Add(new ProcurementPlanStep(
                    itemId,
                    crafts,
                    ProcurementAction.Craft,
                    "recipe",
                    $"Craft {crafts} time(s) to produce at least {quantity} missing unit(s)."));
                return;
            }

            if (sources.TryGetValue(itemId, out var options) && options.Length > 0)
            {
                var source = options[0];
                steps.Add(new ProcurementPlanStep(
                    itemId,
                    quantity,
                    source.Action,
                    source.Key,
                    source.RequiresExplicitSpendApproval
                        ? "Missing quantity can be acquired here, but spending requires explicit approval."
                        : "Selected highest-priority enabled acquisition source."));
                return;
            }

            blockers.Add($"No verified recipe or enabled acquisition source exists for item {itemId} (missing {quantity}).");
        }
        finally
        {
            stack.Remove(itemId);
        }
    }
}

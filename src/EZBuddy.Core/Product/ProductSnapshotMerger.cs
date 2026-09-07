namespace EZBuddy.Core.Product;

/// <summary>
/// Merges a conservative live capture with the last persisted product snapshot. Live capture is
/// authoritative only for the domains it can verify directly (gear and current inventory counts).
/// Richer offline/provider-fed domains are preserved until a verified live collector owns them.
/// </summary>
public static class ProductSnapshotMerger
{
    public static ProductSnapshotBundle MergeLive(
        ProductSnapshotBundle live,
        ProductSnapshotBundle? previous)
    {
        ArgumentNullException.ThrowIfNull(live);
        live.Validate();

        if (previous is null)
        {
            return live;
        }

        previous.Validate();
        if (!string.Equals(live.CharacterKey, previous.CharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Cannot merge Product Snapshots for different characters ('{live.CharacterKey}' vs '{previous.CharacterKey}').");
        }

        return live with
        {
            Currencies = previous.Currencies,
            Collections = previous.Collections,
            Materia = previous.Materia,
            Recipes = previous.Recipes,
            Sources = previous.Sources,
            SchemaVersion = previous.SchemaVersion
        };
    }
}

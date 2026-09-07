namespace EZBuddy.Core.Product;

/// <summary>
/// Merges a conservative live capture with the last persisted product snapshot. Live capture is
/// authoritative only for domains it can verify directly. Richer offline/provider-fed domains are
/// preserved until a verified live collector owns them.
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
            Currencies = MergeCurrencies(previous.Currencies, live.Currencies),
            Collections = previous.Collections,
            Materia = RefreshMateriaQuantities(previous.Materia, live.OwnedItems),
            Recipes = previous.Recipes,
            Sources = previous.Sources,
            SchemaVersion = previous.SchemaVersion
        };
    }

    private static IReadOnlyList<EZBuddy.Core.Economy.CurrencySnapshot> MergeCurrencies(
        IReadOnlyList<EZBuddy.Core.Economy.CurrencySnapshot> previous,
        IReadOnlyList<EZBuddy.Core.Economy.CurrencySnapshot> live)
    {
        if (live.Count == 0)
        {
            return previous;
        }

        var liveKeys = live
            .Select(currency => currency.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return previous
            .Where(currency => !liveKeys.Contains(currency.Key))
            .Concat(live)
            .ToArray();
    }

    private static IReadOnlyList<EZBuddy.Core.Materia.MateriaStock> RefreshMateriaQuantities(
        IReadOnlyList<EZBuddy.Core.Materia.MateriaStock> previous,
        IReadOnlyDictionary<uint, int> liveOwnedItems)
        => previous
            .Select(stock => stock with
            {
                QuantityOwned = liveOwnedItems.GetValueOrDefault(stock.ItemId)
            })
            .ToArray();
}

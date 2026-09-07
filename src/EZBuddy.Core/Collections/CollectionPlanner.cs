namespace EZBuddy.Core.Collections;

public enum CollectionSourceKind
{
    Duty,
    Vendor,
    Craft,
    Gather,
    GoldSaucer,
    Achievement,
    Quest,
    Retainer,
    Manual
}

public sealed record CollectionItemSnapshot(
    string Key,
    string Name,
    string CollectionType,
    bool Owned,
    CollectionSourceKind SourceKind,
    bool HasVerifiedAutomation,
    int EstimatedMinutes,
    int Difficulty = 1,
    int Priority = 0);

public sealed record CollectionTarget(
    CollectionItemSnapshot Item,
    double Score,
    string Recommendation);

public static class CollectionCompletionPlanner
{
    public static IReadOnlyList<CollectionTarget> Rank(IEnumerable<CollectionItemSnapshot> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Where(item => !item.Owned)
            .Select(item =>
            {
                var minutes = Math.Max(1, item.EstimatedMinutes);
                var difficulty = Math.Max(1, item.Difficulty);
                var automationBonus = item.HasVerifiedAutomation ? 500d : 0d;
                var score = (item.Priority * 100d) + automationBonus + (1000d / minutes) - (difficulty * 10d);
                var recommendation = item.HasVerifiedAutomation
                    ? "Verified automation path available."
                    : "Missing item, but no verified EZBuddy automation path exists yet.";
                return new CollectionTarget(item, score, recommendation);
            })
            .OrderByDescending(target => target.Score)
            .ThenBy(target => target.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

namespace EZBuddy.Core.Gear;

public enum GearSlot
{
    Weapon,
    OffHand,
    Head,
    Body,
    Hands,
    Legs,
    Feet,
    Earrings,
    Necklace,
    Bracelets,
    Ring
}

public enum GearStorageLocation
{
    Equipped,
    ArmoryChest,
    Inventory,
    Retainer,
    Saddlebag,
    Unknown
}

public enum GearDisposition
{
    Protect,
    EquipBest,
    Keep,
    Retainer,
    ExpertDeliveryCandidate,
    DesynthesisCandidate,
    SellCandidate,
    ManualReview
}

public sealed record GearItemSnapshot(
    uint ItemId,
    string Name,
    GearSlot Slot,
    int ItemLevel,
    IReadOnlyCollection<string> SupportedJobs,
    GearStorageLocation Location,
    IReadOnlyDictionary<string, int>? Stats = null,
    bool IsEquipped = false,
    bool IsProtected = false,
    bool CanExpertDelivery = false,
    bool CanDesynthesize = false,
    bool CanSell = false,
    bool IsUnique = false,
    bool IsHighQuality = false,
    int MateriaCount = 0)
{
    public bool SupportsJob(string jobKey)
        => SupportedJobs.Any(job => string.Equals(job, jobKey, StringComparison.OrdinalIgnoreCase));
}

public sealed record GearScoreProfile(
    string JobKey,
    double ItemLevelWeight = 1000,
    IReadOnlyDictionary<string, double>? StatWeights = null)
{
    public double Score(GearItemSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var score = item.ItemLevel * ItemLevelWeight;
        if (item.Stats is null || StatWeights is null)
        {
            return score;
        }

        foreach (var (stat, weight) in StatWeights)
        {
            if (item.Stats.TryGetValue(stat, out var value))
            {
                score += value * weight;
            }
        }

        return score;
    }
}

public sealed record GearManagerOptions(
    int MinimumKeepItemLevel,
    bool AllowExpertDeliveryCandidates = true,
    bool AllowDesynthesisCandidates = false,
    bool AllowSellCandidates = false)
{
    public void Validate()
    {
        if (MinimumKeepItemLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumKeepItemLevel));
        }
    }
}

public sealed record GearRecommendation(
    GearItemSnapshot Item,
    GearDisposition Disposition,
    double Score,
    string Reason);

public sealed record GearPlan(
    string JobKey,
    IReadOnlyDictionary<GearSlot, GearItemSnapshot> BestBySlot,
    IReadOnlyList<GearRecommendation> Recommendations,
    IReadOnlyDictionary<GearSlot, GearItemSnapshot>? SecondaryBestBySlot = null)
{
    public IReadOnlyDictionary<GearSlot, GearItemSnapshot> EffectiveSecondaryBestBySlot
        => SecondaryBestBySlot ?? new Dictionary<GearSlot, GearItemSnapshot>();
}

public static class SmartGearManager
{
    public static GearPlan Build(
        IEnumerable<GearItemSnapshot> items,
        GearScoreProfile scoreProfile,
        GearManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scoreProfile);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var all = items.ToArray();
        var eligible = all
            .Where(item => item.SupportsJob(scoreProfile.JobKey))
            .ToArray();

        var rankedBySlot = eligible
            .GroupBy(item => item.Slot)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(scoreProfile.Score)
                    .ThenByDescending(item => item.ItemLevel)
                    .ThenBy(item => item.ItemId)
                    .ToArray());

        var bestBySlot = rankedBySlot
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0]);

        // Rings occupy two equipment slots. Preserve the existing single BestBySlot contract for
        // callers while exposing the second selected ring separately. Other slots remain single-slot.
        var secondaryBestBySlot = new Dictionary<GearSlot, GearItemSnapshot>();
        if (rankedBySlot.TryGetValue(GearSlot.Ring, out var rings) && rings.Length > 1)
        {
            secondaryBestBySlot[GearSlot.Ring] = rings[1];
        }

        // Keep exact selected snapshot instances rather than only ItemId. A player can own
        // multiple copies of the same item ID, and only the selected physical copies should be
        // labelled EquipBest.
        var selectedItems = bestBySlot.Values
            .Concat(secondaryBestBySlot.Values)
            .ToArray();
        var recommendations = new List<GearRecommendation>(all.Length);

        foreach (var item in all)
        {
            var score = scoreProfile.Score(item);
            if (item.IsProtected || item.IsEquipped)
            {
                recommendations.Add(new GearRecommendation(item, GearDisposition.Protect, score,
                    item.IsEquipped ? "Currently equipped; protected from automatic disposal." : "Explicitly protected item."));
                continue;
            }

            if (selectedItems.Any(best => ReferenceEquals(best, item)) && item.SupportsJob(scoreProfile.JobKey))
            {
                var reason = item.Slot == GearSlot.Ring
                    ? $"One of the two highest-scoring owned rings for {scoreProfile.JobKey}."
                    : $"Highest-scoring owned {item.Slot} for {scoreProfile.JobKey}.";
                recommendations.Add(new GearRecommendation(item, GearDisposition.EquipBest, score, reason));
                continue;
            }

            if (item.ItemLevel >= options.MinimumKeepItemLevel)
            {
                recommendations.Add(new GearRecommendation(item, GearDisposition.Keep, score,
                    $"Item level {item.ItemLevel} meets the keep floor of {options.MinimumKeepItemLevel}."));
                continue;
            }

            if (item.MateriaCount > 0)
            {
                recommendations.Add(new GearRecommendation(item, GearDisposition.ManualReview, score,
                    "Item contains materia; destructive recommendations are suppressed."));
                continue;
            }

            if (options.AllowExpertDeliveryCandidates && item.CanExpertDelivery)
            {
                recommendations.Add(new GearRecommendation(item, GearDisposition.ExpertDeliveryCandidate, score,
                    "Below keep floor and eligible for Grand Company Expert Delivery. Candidate only; explicit destructive approval still required."));
                continue;
            }

            if (options.AllowDesynthesisCandidates && item.CanDesynthesize)
            {
                recommendations.Add(new GearRecommendation(item, GearDisposition.DesynthesisCandidate, score,
                    "Below keep floor and eligible for desynthesis. Candidate only; allowlist confirmation is still required."));
                continue;
            }

            if (options.AllowSellCandidates && item.CanSell)
            {
                recommendations.Add(new GearRecommendation(item, GearDisposition.SellCandidate, score,
                    "Below keep floor and sellable. Candidate only; no automatic sale is implied."));
                continue;
            }

            recommendations.Add(new GearRecommendation(item, GearDisposition.Retainer, score,
                "Not selected as best-in-slot and no approved disposal rule applies."));
        }

        return new GearPlan(scoreProfile.JobKey, bestBySlot, recommendations, secondaryBestBySlot);
    }
}

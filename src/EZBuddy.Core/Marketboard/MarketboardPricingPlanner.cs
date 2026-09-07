namespace EZBuddy.Core.Marketboard;

public enum MarketboardRecommendationKind
{
    NoAction,
    KeepListing,
    CreateListing,
    RepriceListing,
    ManualReview
}

public sealed record MarketboardPricingRule(
    uint ItemId,
    bool ExplicitlyApprovedForSale,
    int MinimumUnitPrice,
    int PreferredUnitPrice = 0,
    int UndercutAmount = 1,
    double MaximumAutomaticPriceDropPercent = 10,
    int ReserveQuantity = 0,
    int MaximumListingQuantity = 99,
    int MinimumNetUnitPrice = 0)
{
    public void Validate()
    {
        if (ItemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ItemId));
        }

        if (MinimumUnitPrice < 0 || PreferredUnitPrice < 0 || UndercutAmount < 0 ||
            ReserveQuantity < 0 || MaximumListingQuantity <= 0 || MinimumNetUnitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumUnitPrice), "Marketboard pricing values cannot be negative, and maximum listing quantity must be positive.");
        }

        if (MaximumAutomaticPriceDropPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAutomaticPriceDropPercent));
        }
    }
}

public sealed record MarketboardItemSnapshot(
    uint ItemId,
    string Name,
    int OwnedQuantity,
    int LowestCompetingUnitPrice = 0,
    int CurrentListedQuantity = 0,
    int CurrentOwnUnitPrice = 0)
{
    public bool HasOwnListing => CurrentListedQuantity > 0 && CurrentOwnUnitPrice > 0;
}

public sealed record MarketboardPlannerOptions(double EstimatedFeeRate = 0)
{
    public void Validate()
    {
        if (EstimatedFeeRate is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(EstimatedFeeRate));
        }
    }
}

public sealed record MarketboardRecommendation(
    MarketboardItemSnapshot Item,
    MarketboardRecommendationKind Kind,
    int TargetUnitPrice,
    int Quantity,
    int EstimatedNetUnitPrice,
    string Reason);

public static class MarketboardPricingPlanner
{
    public static MarketboardRecommendation Evaluate(
        MarketboardItemSnapshot item,
        MarketboardPricingRule rule,
        MarketboardPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(rule);
        options ??= new MarketboardPlannerOptions();
        rule.Validate();
        options.Validate();

        if (item.ItemId != rule.ItemId)
        {
            throw new ArgumentException("Marketboard item and pricing rule item IDs do not match.", nameof(rule));
        }

        if (!rule.ExplicitlyApprovedForSale)
        {
            return Recommendation(item, MarketboardRecommendationKind.NoAction, 0, 0, 0,
                "Item is not explicitly approved for Marketboard sale.");
        }

        if (item.OwnedQuantity < 0 || item.CurrentListedQuantity < 0 || item.CurrentOwnUnitPrice < 0 || item.LowestCompetingUnitPrice < 0)
        {
            return Recommendation(item, MarketboardRecommendationKind.ManualReview, 0, 0, 0,
                "Marketboard snapshot contains an invalid negative value.");
        }

        var competitorBelowFloor = item.LowestCompetingUnitPrice > 0 &&
                                   item.LowestCompetingUnitPrice < rule.MinimumUnitPrice;
        if (competitorBelowFloor)
        {
            return Recommendation(
                item,
                item.HasOwnListing ? MarketboardRecommendationKind.KeepListing : MarketboardRecommendationKind.NoAction,
                item.HasOwnListing ? item.CurrentOwnUnitPrice : 0,
                item.HasOwnListing ? item.CurrentListedQuantity : 0,
                item.HasOwnListing ? EstimateNet(item.CurrentOwnUnitPrice, options.EstimatedFeeRate) : 0,
                $"Lowest competitor price {item.LowestCompetingUnitPrice:N0} is below the configured floor of {rule.MinimumUnitPrice:N0}; EZBuddy will not race below the floor.");
        }

        var targetPrice = ResolveTargetPrice(item, rule);
        if (targetPrice <= 0)
        {
            return Recommendation(
                item,
                item.HasOwnListing ? MarketboardRecommendationKind.KeepListing : MarketboardRecommendationKind.ManualReview,
                item.HasOwnListing ? item.CurrentOwnUnitPrice : 0,
                item.HasOwnListing ? item.CurrentListedQuantity : 0,
                item.HasOwnListing ? EstimateNet(item.CurrentOwnUnitPrice, options.EstimatedFeeRate) : 0,
                "No verified competitor price or preferred price is available; automatic pricing is suppressed.");
        }

        var estimatedNet = EstimateNet(targetPrice, options.EstimatedFeeRate);
        if (rule.MinimumNetUnitPrice > 0 && estimatedNet < rule.MinimumNetUnitPrice)
        {
            return Recommendation(item, MarketboardRecommendationKind.ManualReview, targetPrice, 0, estimatedNet,
                $"Target price would net about {estimatedNet:N0} after the configured fee estimate, below the minimum net floor of {rule.MinimumNetUnitPrice:N0}.");
        }

        if (item.HasOwnListing)
        {
            if (item.CurrentOwnUnitPrice <= targetPrice)
            {
                return Recommendation(item, MarketboardRecommendationKind.KeepListing, item.CurrentOwnUnitPrice, item.CurrentListedQuantity,
                    EstimateNet(item.CurrentOwnUnitPrice, options.EstimatedFeeRate),
                    "Existing listing is already at or below the guarded target; no additional undercut is recommended.");
            }

            var dropPercent = 100d * (item.CurrentOwnUnitPrice - targetPrice) / item.CurrentOwnUnitPrice;
            if (dropPercent > rule.MaximumAutomaticPriceDropPercent)
            {
                return Recommendation(item, MarketboardRecommendationKind.ManualReview, targetPrice, item.CurrentListedQuantity, estimatedNet,
                    $"Repricing would reduce unit price by {dropPercent:F1}%, above the configured {rule.MaximumAutomaticPriceDropPercent:F1}% automatic-drop limit.");
            }

            return Recommendation(item, MarketboardRecommendationKind.RepriceListing, targetPrice, item.CurrentListedQuantity, estimatedNet,
                $"Guarded reprice to {targetPrice:N0}; floor, undercut and maximum-drop rules are satisfied.");
        }

        var available = Math.Max(0, item.OwnedQuantity - rule.ReserveQuantity);
        if (available <= 0)
        {
            return Recommendation(item, MarketboardRecommendationKind.NoAction, 0, 0, 0,
                $"Owned quantity does not exceed the configured reserve of {rule.ReserveQuantity}.");
        }

        var quantity = Math.Min(available, rule.MaximumListingQuantity);
        return Recommendation(item, MarketboardRecommendationKind.CreateListing, targetPrice, quantity, estimatedNet,
            $"Create an approved listing for {quantity} item(s) at {targetPrice:N0} each while preserving the configured reserve.");
    }

    private static int ResolveTargetPrice(MarketboardItemSnapshot item, MarketboardPricingRule rule)
    {
        if (item.LowestCompetingUnitPrice > 0)
        {
            return Math.Max(rule.MinimumUnitPrice, item.LowestCompetingUnitPrice - rule.UndercutAmount);
        }

        if (rule.PreferredUnitPrice > 0)
        {
            return Math.Max(rule.MinimumUnitPrice, rule.PreferredUnitPrice);
        }

        return 0;
    }

    private static int EstimateNet(int unitPrice, double feeRate)
        => checked((int)Math.Floor(unitPrice * (1d - feeRate)));

    private static MarketboardRecommendation Recommendation(
        MarketboardItemSnapshot item,
        MarketboardRecommendationKind kind,
        int targetPrice,
        int quantity,
        int estimatedNet,
        string reason)
        => new(item, kind, targetPrice, quantity, estimatedNet, reason);
}

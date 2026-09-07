using EZBuddy.Core.Marketboard;

namespace EZBuddy.Core.Tests;

public sealed class MarketboardPricingPlannerTests
{
    [Fact]
    public void UnapprovedItemNeverCreatesListing()
    {
        var item = new MarketboardItemSnapshot(100, "Widget", 20, LowestCompetingUnitPrice: 1000);
        var rule = new MarketboardPricingRule(100, ExplicitlyApprovedForSale: false, MinimumUnitPrice: 800);

        var recommendation = MarketboardPricingPlanner.Evaluate(item, rule);

        Assert.Equal(MarketboardRecommendationKind.NoAction, recommendation.Kind);
        Assert.Equal(0, recommendation.Quantity);
    }

    [Fact]
    public void ApprovedListingPreservesReserveAndUsesGuardedUndercut()
    {
        var item = new MarketboardItemSnapshot(100, "Widget", 20, LowestCompetingUnitPrice: 1000);
        var rule = new MarketboardPricingRule(
            100,
            ExplicitlyApprovedForSale: true,
            MinimumUnitPrice: 800,
            UndercutAmount: 1,
            ReserveQuantity: 5,
            MaximumListingQuantity: 10);

        var recommendation = MarketboardPricingPlanner.Evaluate(item, rule);

        Assert.Equal(MarketboardRecommendationKind.CreateListing, recommendation.Kind);
        Assert.Equal(999, recommendation.TargetUnitPrice);
        Assert.Equal(10, recommendation.Quantity);
    }

    [Fact]
    public void CompetitorBelowFloorNeverTriggersRaceToBottom()
    {
        var item = new MarketboardItemSnapshot(100, "Widget", 20, LowestCompetingUnitPrice: 700);
        var rule = new MarketboardPricingRule(100, true, MinimumUnitPrice: 800);

        var recommendation = MarketboardPricingPlanner.Evaluate(item, rule);

        Assert.Equal(MarketboardRecommendationKind.NoAction, recommendation.Kind);
        Assert.Contains("floor", recommendation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LargeAutomaticPriceDropRequiresManualReview()
    {
        var item = new MarketboardItemSnapshot(
            100,
            "Widget",
            OwnedQuantity: 20,
            LowestCompetingUnitPrice: 700,
            CurrentListedQuantity: 5,
            CurrentOwnUnitPrice: 1000);
        var rule = new MarketboardPricingRule(
            100,
            true,
            MinimumUnitPrice: 600,
            MaximumAutomaticPriceDropPercent: 10);

        var recommendation = MarketboardPricingPlanner.Evaluate(item, rule);

        Assert.Equal(MarketboardRecommendationKind.ManualReview, recommendation.Kind);
        Assert.Equal(699, recommendation.TargetUnitPrice);
    }

    [Fact]
    public void FeeAwareNetFloorCanBlockOtherwiseValidListing()
    {
        var item = new MarketboardItemSnapshot(100, "Widget", 20, LowestCompetingUnitPrice: 1000);
        var rule = new MarketboardPricingRule(
            100,
            true,
            MinimumUnitPrice: 800,
            MinimumNetUnitPrice: 975);

        var recommendation = MarketboardPricingPlanner.Evaluate(
            item,
            rule,
            new MarketboardPlannerOptions(EstimatedFeeRate: 0.05));

        Assert.Equal(MarketboardRecommendationKind.ManualReview, recommendation.Kind);
        Assert.Equal(949, recommendation.EstimatedNetUnitPrice);
    }
}

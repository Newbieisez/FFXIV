using EZBuddy.Core.Collections;
using EZBuddy.Core.Economy;
using EZBuddy.Core.Gear;
using EZBuddy.Core.Goals;
using EZBuddy.Core.Loot;
using EZBuddy.Core.Product;

namespace EZBuddy.Core.Tests;

public sealed class ProductIntelligenceTests
{
    [Fact]
    public void Preflight_BlocksMissingRouteAndPatchSensitiveMismatch()
    {
        var snapshot = new PreflightSnapshot(
            CharacterAvailable: true,
            LicenseAllowsExecution: true,
            MagitekRequired: true,
            MagitekReady: true,
            FreeInventorySlots: 10,
            MinimumFreeInventorySlots: 6,
            RouteRequired: true,
            RouteVerified: false,
            Adapters: [new AdapterRuntimeSnapshot("lisbeth", true, "2.0")],
            CompatibilityStamps: [new AdapterCompatibilityStamp("lisbeth", "1.0", PatchSensitive: true)]);

        var report = PreflightEvaluator.Evaluate(snapshot);

        Assert.True(report.IsBlocked);
        Assert.Equal("BLOCKED", report.OverallState);
        Assert.Contains(report.Checks, check => check.Key == "route" && check.Severity == PreflightSeverity.Blocked);
        Assert.Contains(report.Checks, check => check.Key == "compat:lisbeth" && check.Severity == PreflightSeverity.Blocked);
    }

    [Fact]
    public void DryRun_RequiresExplicitOptInForDestructiveAction()
    {
        var plan = DryRunPlanner.Build([
            new DryRunCandidate("desynth", "Desynthesis", "Inventory", 90, true, true, IsDestructive: true, DestructiveOptIn: false),
            new DryRunCandidate("repair", "Repair", "Maintenance", 100, true, true)
        ]);

        Assert.Equal(1, plan.RunnableCount);
        var destructive = Assert.Single(plan.Actions, action => action.Key == "desynth");
        Assert.Equal(DryRunDisposition.RequiresOptIn, destructive.Disposition);
    }

    [Fact]
    public void SmartGear_SelectsBestOwnedPieceAndClassifiesOldGearSafely()
    {
        var jobs = new HashSet<string>(["PLD"], StringComparer.OrdinalIgnoreCase);
        var items = new[]
        {
            new GearItemSnapshot(1, "Old Body", GearSlot.Body, 700, jobs, GearStorageLocation.Inventory, CanExpertDelivery: true),
            new GearItemSnapshot(2, "New Body", GearSlot.Body, 730, jobs, GearStorageLocation.ArmoryChest)
        };

        var plan = SmartGearManager.Build(items, new GearScoreProfile("PLD"), new GearManagerOptions(720));

        Assert.Equal((uint)2, plan.BestBySlot[GearSlot.Body].ItemId);
        Assert.Contains(plan.Recommendations, recommendation => recommendation.Item.ItemId == 1 && recommendation.Disposition == GearDisposition.ExpertDeliveryCandidate);
        Assert.Contains(plan.Recommendations, recommendation => recommendation.Item.ItemId == 2 && recommendation.Disposition == GearDisposition.EquipBest);
    }

    [Fact]
    public void CurrencyCapManager_SpendsOnlyApprovedRules()
    {
        var plan = CurrencyCapManager.Build(
            [new CurrencySnapshot("seals", "Company Seals", 1900, 2000, ProjectedIncoming: 300, SafetyBuffer: 100)],
            [
                new CurrencySpendOption("seals", "ventures", "Venture Tokens", 200, 100, ExplicitlyApproved: true, MaximumPurchases: 2),
                new CurrencySpendOption("seals", "darkmatter", "Dark Matter", 100, 200, ExplicitlyApproved: false, MaximumPurchases: 5)
            ]);

        var spend = Assert.Single(plan.SpendItems);
        Assert.Equal("ventures", spend.OptionKey);
        Assert.Equal(2, spend.Quantity);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void SmartLoot_EscalatesUpgradeWhenInventoryIsFullish()
    {
        var item = new LootItemSnapshot(42, "Potential Upgrade", IsUpgrade: true);
        var decision = SmartLootPolicy.Decide(item, new SmartLootSettings(3, 3));

        Assert.Equal(SmartLootAction.ManualReview, decision.Action);
    }

    [Fact]
    public void GoalPlanner_UsesOnlyCapabilitiesThatActuallyExist()
    {
        var available = new HashSet<string>(["smart-gear", "procurement"], StringComparer.OrdinalIgnoreCase);
        var plan = GoalPlanner.Build(new GoalRequest(GoalType.BuildGearSet, "Paladin 100"), available);

        Assert.True(plan.CanStart);
        Assert.Contains(plan.Steps, step => step.CapabilityKey == "smart-gear" && step.Available);
        Assert.Contains(plan.Steps, step => step.CapabilityKey == "materia" && !step.Available);
    }

    [Fact]
    public void CollectionPlanner_PrioritizesVerifiedAutomation()
    {
        var ranked = CollectionCompletionPlanner.Rank([
            new CollectionItemSnapshot("a", "Manual Mount", "Mount", false, CollectionSourceKind.Manual, false, 10, Priority: 10),
            new CollectionItemSnapshot("b", "Verified Minion", "Minion", false, CollectionSourceKind.Duty, true, 30, Priority: 10),
            new CollectionItemSnapshot("c", "Owned Card", "Card", true, CollectionSourceKind.GoldSaucer, true, 1, Priority: 100)
        ]);

        Assert.Equal(2, ranked.Count);
        Assert.Equal("b", ranked[0].Item.Key);
    }
}

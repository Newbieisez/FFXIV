using EZBuddy.Core.DeepDungeons;
using EZBuddy.Core.DomanEnclave;
using EZBuddy.Core.FieldOperations;
using EZBuddy.Core.Housing;
using EZBuddy.Core.IslandSanctuary;
using EZBuddy.Core.Levequests;
using EZBuddy.Core.Planning;
using EZBuddy.Core.SharedFates;
using EZBuddy.Core.TreasureHunts;
using EZBuddy.Core.Voyages;

namespace EZBuddy.Core.Tests;

public sealed class RemainingDomainPlannerTests
{
    [Fact]
    public void DeepDungeon_InventoryFloorSuppressesOptionalLootAndPrioritizesExit()
    {
        var plan = DeepDungeonPlanner.Build(new DeepDungeonSnapshot(
            "potd", 21, 30, 30, 30, 3, 3,
            PassageOpen: true,
            CairnOfReturnNeeded: false,
            PartyMemberKO: false,
            BossFloor: false,
            Pomanders: new Dictionary<string, int>()));

        Assert.Equal("exit", plan[0].Key);
        Assert.DoesNotContain(plan, item => item.Key == "silver");
    }

    [Fact]
    public void TreasurePortal_RequiresReviewWhenPartyPolicyIsNotMet()
    {
        var plan = TreasureHuntPlanner.Build(new TreasureHuntSnapshot(
            TreasureMapState.PortalAvailable, 1, true, true, true, false,
            PartyRecommended: true, PartySize: 1, PortalSupported: true));

        var step = Assert.Single(plan);
        Assert.Equal(PlanRisk.ManualReview, step.Risk);
    }

    [Fact]
    public void FieldOperations_ProtectsProgressByPenalizingDanger()
    {
        var plan = FieldOperationPlanner.Build(new FieldOperationSnapshot(
            "bozja", 100, 100, true, 1,
            [
                new FieldEventSnapshot("safe", "Safe Event", 50, 10, 1, true, true),
                new FieldEventSnapshot("danger", "Danger Event", 80, 10, 20, true, true)
            ]));

        Assert.Equal("safe", plan[0].Key);
    }

    [Fact]
    public void SharedFates_CapPressureRequiresApprovedSpendRule()
    {
        var plan = SharedFatePlanner.Build(new SharedFateSnapshot(
            1490, 1500, 100, 50,
            [new SharedFateRegionSnapshot("r1", "Region", 0, 10, true, 5)],
            HasApprovedSpendRule: false));

        var spend = Assert.Single(plan, item => item.Key == "spend");
        Assert.Equal(PlanRisk.ManualReview, spend.Risk);
    }

    [Fact]
    public void LevequestPlanner_PreservesConfiguredAllowanceReserve()
    {
        var plan = LevequestPlanner.Build(new LeveSnapshot(
            95, 100, PreserveAllowances: 90, BurnWhenAtOrAbove: 90, MaximumLevesThisRun: 5,
            [
                new LeveOption("cheap", "One Allowance", LeveType.Craft, 1, 100, 1, true),
                new LeveOption("too-expensive", "Ten Allowances", LeveType.Craft, 10, 1000, 1, true)
            ]));

        Assert.Contains(plan, item => item.Key == "cheap");
        Assert.DoesNotContain(plan, item => item.Key == "too-expensive");
    }

    [Fact]
    public void DomanEnclave_DonatesOnlyExplicitlyApprovedInventoryAboveReserve()
    {
        var plan = DomanEnclavePlanner.Build(new DomanEnclaveSnapshot(
            1000,
            [
                new DomanDonationCandidate(1, "Approved", 5, 2, 100, true),
                new DomanDonationCandidate(2, "Unapproved", 99, 0, 500, false)
            ]));

        var donation = Assert.Single(plan.Donations);
        Assert.Equal((uint)1, donation.ItemId);
        Assert.Equal(3, donation.Quantity);
        Assert.Equal(300, plan.TotalValue);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void VoyagePlanner_BlocksDeploymentWhenConditionOrFuelIsUnsafe()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = VoyagePlanner.Build([
            new VoyageVesselSnapshot("sub1", "Sub One", false, null, 20, 30, 10, 50, 1, 5, "route-a", true),
            new VoyageVesselSnapshot("sub2", "Sub Two", false, null, 100, 30, 60, 50, 1, 5, "route-b", true)
        ], now);

        Assert.Contains(plan, item => item.Key == "sub1:repair" && item.Risk == PlanRisk.ManualReview);
        Assert.Contains(plan, item => item.Key == "sub2:fuel" && item.Risk == PlanRisk.ManualReview);
        Assert.DoesNotContain(plan, item => item.Key.EndsWith(":deploy", StringComparison.Ordinal));
    }

    [Fact]
    public void IslandPlanner_BlocksHarvestWhenInventoryAtFloor()
    {
        var plan = IslandSanctuaryPlanner.Build(new IslandSnapshot(
            3, 3, ReadyCrops: 4, EmptyCropPlots: 0, ReadyPastureAnimals: 2, EmptyPastureSlots: 0,
            GranariesReady: 1, WorkshopDaysUnscheduled: 1, WorkshopPlanVerified: true,
            HasRequiredSeeds: true, HasRequiredFeed: true));

        Assert.Contains(plan, item => item.Key == "crops-harvest" && item.Risk == PlanRisk.ManualReview);
        Assert.Contains(plan, item => item.Key == "pasture-collect" && item.Risk == PlanRisk.ManualReview);
    }

    [Fact]
    public void HousingPlanner_HarvestRequiresExplicitApprovalButWaterDoesNot()
    {
        var plan = HousingGardeningPlanner.Build(new HousingSnapshot(
            HousingLotteryState.None,
            null,
            [
                new GardenPlotSnapshot("p1", "Thavnairian Onion", false, true, false, false, true, HarvestApproved: false),
                new GardenPlotSnapshot("p2", "Flower", false, false, true, false, true, HarvestApproved: false)
            ]), DateTimeOffset.UtcNow);

        Assert.Contains(plan, item => item.Key == "p1:harvest" && item.Risk == PlanRisk.ManualReview);
        Assert.Contains(plan, item => item.Key == "p2:water" && item.Risk == PlanRisk.Low);
    }
}

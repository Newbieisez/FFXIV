using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Tests;

public sealed class DutyRuntimeProfileTests
{
    [Fact]
    public void LootPolicy_PassesWhenInventoryIsAtSafetyFloor()
    {
        var policy = new DutyLootPolicy(DutyLootAction.Greed, PassAtOrBelowFreeSlots: 3);

        Assert.Equal(DutyLootAction.Pass, policy.SelectAction(3));
        Assert.Equal(DutyLootAction.Pass, policy.SelectAction(2));
        Assert.Equal(DutyLootAction.Greed, policy.SelectAction(4));
    }

    [Fact]
    public void NavigationProfile_RejectsDuplicateObjectiveIds()
    {
        var profile = new DutyNavigationProfile(
            DutyId: 1,
            Name: "Test Duty",
            MinimumLevel: 15,
            MaximumLevel: 20,
            Objectives:
            [
                new DutyObjectiveNode("door-1", DutyObjectiveKind.Waypoint, new DutyPoint(1, 2, 3)),
                new DutyObjectiveNode("door-1", DutyObjectiveKind.Waypoint, new DutyPoint(4, 5, 6))
            ],
            Bosses: Array.Empty<BossMechanicProfile>());

        Assert.Throws<InvalidDataException>(profile.Validate);
    }

    [Fact]
    public void MechanicProfile_RequiresSafePointForSafeSpotRule()
    {
        var boss = new BossMechanicProfile(
            BossNpcId: 123,
            Name: "Test Boss",
            Rules:
            [
                new BossMechanicRule("safe", BossMechanicKind.SafeSpot, ActionId: 456)
            ]);

        Assert.Throws<InvalidDataException>(boss.Validate);
    }

    [Fact]
    public void Catalog_ReturnsValidatedProfileByDutyId()
    {
        var profile = new DutyNavigationProfile(
            DutyId: 777,
            Name: "Catalog Duty",
            MinimumLevel: 71,
            MaximumLevel: 72,
            Objectives:
            [
                new DutyObjectiveNode("start", DutyObjectiveKind.Waypoint, new DutyPoint(0, 0, 0))
            ],
            Bosses: Array.Empty<BossMechanicProfile>());

        var catalog = new DutyNavigationProfileCatalog([profile]);

        Assert.True(catalog.TryGet(777, out var loaded));
        Assert.Equal("Catalog Duty", loaded?.Name);
    }
}

using EZBuddy.Core.Gear;

namespace EZBuddy.Core.Tests;

public sealed class SmartGearDuplicateSelectionTests
{
    [Fact]
    public void Build_DuplicateSingleSlotItemId_OnlyExactSelectedCopyIsEquipBest()
    {
        var equipped = new GearItemSnapshot(
            100,
            "Twin Body",
            GearSlot.Body,
            740,
            ["Paladin"],
            GearStorageLocation.Equipped,
            IsEquipped: true,
            IsProtected: true);

        var duplicate = new GearItemSnapshot(
            100,
            "Twin Body",
            GearSlot.Body,
            740,
            ["Paladin"],
            GearStorageLocation.ArmoryChest);

        var plan = SmartGearManager.Build(
            [equipped, duplicate],
            new GearScoreProfile("paladin"),
            new GearManagerOptions(730));

        Assert.Same(equipped, plan.BestBySlot[GearSlot.Body]);
        Assert.Equal(GearDisposition.Protect, plan.Recommendations.Single(x => ReferenceEquals(x.Item, equipped)).Disposition);
        Assert.NotEqual(GearDisposition.EquipBest, plan.Recommendations.Single(x => ReferenceEquals(x.Item, duplicate)).Disposition);
    }

    [Fact]
    public void Build_DuplicateItemIdWithDifferentItemLevel_SelectsHigherCopyOnly()
    {
        var lower = new GearItemSnapshot(
            200,
            "Variant Body",
            GearSlot.Body,
            720,
            ["RedMage"],
            GearStorageLocation.Inventory);

        var higher = new GearItemSnapshot(
            200,
            "Variant Body",
            GearSlot.Body,
            750,
            ["RedMage"],
            GearStorageLocation.ArmoryChest);

        var plan = SmartGearManager.Build(
            [lower, higher],
            new GearScoreProfile("RedMage"),
            new GearManagerOptions(730));

        Assert.Same(higher, plan.BestBySlot[GearSlot.Body]);
        Assert.Equal(GearDisposition.EquipBest, plan.Recommendations.Single(x => ReferenceEquals(x.Item, higher)).Disposition);
        Assert.NotEqual(GearDisposition.EquipBest, plan.Recommendations.Single(x => ReferenceEquals(x.Item, lower)).Disposition);
    }

    [Fact]
    public void Build_Rings_SelectsTwoHighestScoringPhysicalCopies()
    {
        var best = new GearItemSnapshot(
            301,
            "Ring A",
            GearSlot.Ring,
            760,
            ["Scholar"],
            GearStorageLocation.ArmoryChest);
        var second = new GearItemSnapshot(
            302,
            "Ring B",
            GearSlot.Ring,
            750,
            ["Scholar"],
            GearStorageLocation.Inventory);
        var third = new GearItemSnapshot(
            303,
            "Ring C",
            GearSlot.Ring,
            740,
            ["Scholar"],
            GearStorageLocation.Inventory);

        var plan = SmartGearManager.Build(
            [third, second, best],
            new GearScoreProfile("Scholar"),
            new GearManagerOptions(730));

        Assert.Same(best, plan.BestBySlot[GearSlot.Ring]);
        Assert.Same(second, plan.EffectiveSecondaryBestBySlot[GearSlot.Ring]);
        Assert.Equal(GearDisposition.EquipBest, plan.Recommendations.Single(x => ReferenceEquals(x.Item, best)).Disposition);
        Assert.Equal(GearDisposition.EquipBest, plan.Recommendations.Single(x => ReferenceEquals(x.Item, second)).Disposition);
        Assert.NotEqual(GearDisposition.EquipBest, plan.Recommendations.Single(x => ReferenceEquals(x.Item, third)).Disposition);
    }
}

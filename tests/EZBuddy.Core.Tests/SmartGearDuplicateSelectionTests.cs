using EZBuddy.Core.Gear;

namespace EZBuddy.Core.Tests;

public sealed class SmartGearDuplicateSelectionTests
{
    [Fact]
    public void Build_DuplicateItemId_OnlyExactSelectedCopyIsEquipBest()
    {
        var equipped = new GearItemSnapshot(
            100,
            "Twin Ring",
            GearSlot.Ring,
            740,
            ["Paladin"],
            GearStorageLocation.Equipped,
            IsEquipped: true,
            IsProtected: true);

        var duplicate = new GearItemSnapshot(
            100,
            "Twin Ring",
            GearSlot.Ring,
            740,
            ["Paladin"],
            GearStorageLocation.ArmoryChest);

        var plan = SmartGearManager.Build(
            [equipped, duplicate],
            new GearScoreProfile("paladin"),
            new GearManagerOptions(730));

        Assert.Same(equipped, plan.BestBySlot[GearSlot.Ring]);
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
}

using EZBuddy.Core.Collections;
using EZBuddy.Core.Economy;
using EZBuddy.Core.Gear;
using EZBuddy.Core.Materia;
using EZBuddy.Core.Procurement;
using EZBuddy.Core.Product;

namespace EZBuddy.Core.Tests;

public sealed class ProductSnapshotMergerTests
{
    [Fact]
    public void MergeLive_PreservesRichDomainsAndRefreshesLiveDomains()
    {
        var previous = new ProductSnapshotBundle(
            "Character",
            DateTimeOffset.UtcNow.AddHours(-1),
            [new GearItemSnapshot(1, "Old Gear", GearSlot.Body, 700, ["PLD"], GearStorageLocation.Inventory)],
            [new CurrencySnapshot("seals", "Company Seals", 100, 1000)],
            [new CollectionItemSnapshot("mount", "Mount", "Mount", false, CollectionSourceKind.Duty, true, 10)],
            [new MateriaStock(50, "Crit", "Crit", 10, 2)],
            new Dictionary<uint, int> { [900] = 9 },
            [new ProcurementRecipe(100, 1, [new ProcurementIngredient(200, 1)])],
            [new ProcurementSource(200, "gather", ProcurementAction.Gather, 10)]);

        var live = new ProductSnapshotBundle(
            "character",
            DateTimeOffset.UtcNow,
            [new GearItemSnapshot(2, "Live Gear", GearSlot.Body, 750, ["PLD"], GearStorageLocation.ArmoryChest)],
            [],
            [],
            [],
            new Dictionary<uint, int> { [901] = 2 },
            [],
            []);

        var merged = ProductSnapshotMerger.MergeLive(live, previous);

        Assert.Equal((uint)2, Assert.Single(merged.Gear).ItemId);
        Assert.Equal(2, merged.OwnedItems[901]);
        Assert.False(merged.OwnedItems.ContainsKey(900));
        Assert.Single(merged.Currencies);
        Assert.Single(merged.Collections);
        Assert.Single(merged.Materia);
        Assert.Single(merged.Recipes);
        Assert.Single(merged.Sources);
        Assert.Equal(live.CapturedAtUtc, merged.CapturedAtUtc);
    }

    [Fact]
    public void MergeLive_WithNoPrevious_ReturnsLiveSnapshot()
    {
        var live = new ProductSnapshotBundle(
            "Character",
            DateTimeOffset.UtcNow,
            [], [], [], [], new Dictionary<uint, int>(), [], []);

        Assert.Same(live, ProductSnapshotMerger.MergeLive(live, null));
    }

    [Fact]
    public void MergeLive_DifferentCharacter_Throws()
    {
        var live = new ProductSnapshotBundle(
            "One",
            DateTimeOffset.UtcNow,
            [], [], [], [], new Dictionary<uint, int>(), [], []);
        var previous = live with { CharacterKey = "Two" };

        Assert.Throws<InvalidDataException>(() => ProductSnapshotMerger.MergeLive(live, previous));
    }
}

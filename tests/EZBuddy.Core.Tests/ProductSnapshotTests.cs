using EZBuddy.Core.Collections;
using EZBuddy.Core.Economy;
using EZBuddy.Core.Gear;
using EZBuddy.Core.Materia;
using EZBuddy.Core.Procurement;
using EZBuddy.Core.Product;

namespace EZBuddy.Core.Tests;

public sealed class ProductSnapshotTests
{
    [Fact]
    public async Task SnapshotStore_RoundTripsAndAnalyzerUsesAllAvailableDomains()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddyTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "snapshot.json");
        var store = new JsonProductSnapshotStore(path);
        var jobs = new HashSet<string>(["PLD"], StringComparer.OrdinalIgnoreCase);

        var snapshot = new ProductSnapshotBundle(
            "TestCharacter",
            DateTimeOffset.UtcNow,
            Gear:
            [
                new GearItemSnapshot(1, "Body 730", GearSlot.Body, 730, jobs, GearStorageLocation.ArmoryChest),
                new GearItemSnapshot(2, "Old Body", GearSlot.Body, 700, jobs, GearStorageLocation.Inventory, CanExpertDelivery: true)
            ],
            Currencies: [new CurrencySnapshot("seals", "Company Seals", 1900, 2000, 200, 100)],
            Collections: [new CollectionItemSnapshot("mount", "Missing Mount", "Mount", false, CollectionSourceKind.Duty, true, 20, Priority: 5)],
            Materia: [new MateriaStock(50, "Crit Materia", "Crit", 10, 4, UnitCost: 20)],
            OwnedItems: new Dictionary<uint, int> { [200] = 1 },
            Recipes: [new ProcurementRecipe(100, 1, [new ProcurementIngredient(200, 2)])],
            Sources: [new ProcurementSource(200, "gather", ProcurementAction.Gather, 100)]);

        await store.SaveAsync(snapshot, cancellationToken);
        var loaded = await store.LoadAsync(cancellationToken);
        Assert.NotNull(loaded);

        var meldRequest = new GearMeldRequest(
            1,
            "Body 730",
            new Dictionary<string, int> { ["Crit"] = 0 },
            new Dictionary<string, int> { ["Crit"] = 20 },
            new Dictionary<string, int> { ["Crit"] = 10 },
            new Dictionary<string, double> { ["Crit"] = 1 },
            [new MeldSlot(1, false)]);

        var analysis = ProductSnapshotAnalyzer.Analyze(loaded, new ProductAnalysisRequest(
            "PLD",
            720,
            CurrencySpendOptions: [new CurrencySpendOption("seals", "ventures", "Venture Tokens", 200, 100, ExplicitlyApproved: true, MaximumPurchases: 2)],
            ProcurementTargetItemId: 100,
            ProcurementTargetQuantity: 1,
            MeldRequests: [meldRequest]));

        Assert.NotNull(analysis.Gear);
        Assert.Equal((uint)1, analysis.Gear.BestBySlot[GearSlot.Body].ItemId);
        Assert.NotEmpty(analysis.Currency.SpendItems);
        Assert.Single(analysis.Collections);
        Assert.NotNull(analysis.Procurement);
        Assert.True(analysis.Procurement.IsComplete);
        Assert.Single(analysis.Materia);
        Assert.True(analysis.Materia[0].MeetsTargets);
    }

    [Fact]
    public async Task SnapshotStore_FailsClosedOnCorruptJson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "snapshot.json");
        await File.WriteAllTextAsync(path, "{not-valid-json", cancellationToken);
        var store = new JsonProductSnapshotStore(path);

        Assert.Null(await store.LoadAsync(cancellationToken));
    }
}

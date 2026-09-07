using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class GrandCompanyRoutineActivityTests
{
    [Fact]
    public async Task ExpertDeliveryRunsExplicitAllowlistOnce()
    {
        var adapter = new FakeGrandCompanyAdapter { DeliveryResult = true };
        var activity = new GrandCompanyExpertDeliveryActivity(
            adapter,
            new GrandCompanyExpertDeliveryOptions([101u, 202u]));

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.Equal(1, adapter.DeliveryCalls);
        Assert.Equal([101u, 202u], adapter.LastDeliveryItems);
    }

    [Fact]
    public async Task ExpertDeliveryAmbiguousFailureDoesNotRepeat()
    {
        var adapter = new FakeGrandCompanyAdapter { DeliveryResult = false };
        var activity = new GrandCompanyExpertDeliveryActivity(
            adapter,
            new GrandCompanyExpertDeliveryOptions([101u]));

        var first = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var second = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Block, first.Disposition);
        Assert.Equal(ExecutionDisposition.Block, second.Disposition);
        Assert.Equal(1, adapter.DeliveryCalls);
    }

    [Fact]
    public async Task VentureRefillCompletesFromExistingSealsWithoutDelivery()
    {
        var quantities = new MutableQuantityProvider(21072, 4);
        var adapter = new FakeGrandCompanyAdapter
        {
            PurchaseResult = true,
            OnPurchase = (_, _, target) => quantities.Set(21072, target)
        };
        var activity = new VentureTokenRefillActivity(
            adapter,
            quantities,
            new VentureTokenRefillOptions(21072, 10, 50));

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.Equal(1, adapter.PurchaseCalls);
        Assert.Equal(0, adapter.DeliveryCalls);
        Assert.Equal(50, quantities.GetQuantity(21072));
    }

    [Fact]
    public async Task VentureRefillUsesOneApprovedDeliveryThenOneFinalPurchase()
    {
        var quantities = new MutableQuantityProvider(21072, 2);
        var adapter = new FakeGrandCompanyAdapter
        {
            DeliveryResult = true,
            OnPurchase = (_, _, target) =>
            {
                if (quantities.GetQuantity(21072) == 2 && adapterPurchaseCountPlaceholder == 0)
                {
                    return;
                }
                quantities.Set(21072, target);
            }
        };

        var purchaseCount = 0;
        adapter.OnPurchase = (_, _, target) =>
        {
            purchaseCount++;
            adapter.PurchaseResult = purchaseCount > 1;
            if (adapter.PurchaseResult)
            {
                quantities.Set(21072, target);
            }
        };

        var activity = new VentureTokenRefillActivity(
            adapter,
            quantities,
            new VentureTokenRefillOptions(21072, 10, 50, [301u]));

        var first = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var second = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.Equal(ExecutionDisposition.Complete, second.Disposition);
        Assert.Equal(2, adapter.PurchaseCalls);
        Assert.Equal(1, adapter.DeliveryCalls);
        Assert.Equal(50, quantities.GetQuantity(21072));
    }

    [Fact]
    public async Task VentureRefillWithoutAllowlistBlocksBeforeDestructiveAction()
    {
        var quantities = new MutableQuantityProvider(21072, 1);
        var adapter = new FakeGrandCompanyAdapter { PurchaseResult = false };
        var activity = new VentureTokenRefillActivity(
            adapter,
            quantities,
            new VentureTokenRefillOptions(21072, 10, 50));

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Block, result.Disposition);
        Assert.Equal(0, adapter.DeliveryCalls);
    }

    private static int adapterPurchaseCountPlaceholder => 0;

    private sealed class MutableQuantityProvider(uint itemId, int quantity) : IItemQuantityProvider
    {
        private readonly Dictionary<uint, int> _quantities = new() { [itemId] = quantity };
        public int GetQuantity(uint requestedItemId) => _quantities.GetValueOrDefault(requestedItemId);
        public void Set(uint requestedItemId, int value) => _quantities[requestedItemId] = value;
    }

    private sealed class FakeGrandCompanyAdapter : IGrandCompanyAdapter
    {
        public string Key => "fake-gc";
        public string DisplayName => "Fake GC";
        public bool PurchaseResult { get; set; }
        public bool DeliveryResult { get; set; }
        public int PurchaseCalls { get; private set; }
        public int DeliveryCalls { get; private set; }
        public IReadOnlyList<uint> LastDeliveryItems { get; private set; } = [];
        public Action<uint, int, int>? OnPurchase { get; set; }

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Ready,
                "ready",
                DateTimeOffset.UtcNow));
        }

        public Task<bool> EnsureVentureTokensAsync(
            uint ventureItemId,
            int currentQuantity,
            int targetQuantity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PurchaseCalls++;
            OnPurchase?.Invoke(ventureItemId, currentQuantity, targetQuantity);
            return Task.FromResult(PurchaseResult);
        }

        public Task<bool> RunExpertDeliveryAsync(
            IReadOnlyCollection<uint> approvedItemIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeliveryCalls++;
            LastDeliveryItems = approvedItemIds.ToArray();
            return Task.FromResult(DeliveryResult);
        }
    }
}
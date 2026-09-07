using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class VentureSealPressureActivityTests
{
    [Fact]
    public async Task HealthyReserveAndSealHeadroomCompletesWithoutPurchase()
    {
        var quantities = new MutableQuantityProvider(21072, 25);
        var adapter = new FakeGrandCompanyAdapter();
        var activity = Create(
            adapter,
            quantities,
            new FakeSealStateProvider(new GrandCompanySealState(80_000, 90_000)),
            minimum: 10,
            target: 50,
            buffer: 1_000);

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.Equal(0, adapter.PurchaseCalls);
        Assert.Equal(0, adapter.DeliveryCalls);
    }

    [Fact]
    public async Task NearCapTopsVenturesOnlyToConfiguredTarget()
    {
        var quantities = new MutableQuantityProvider(21072, 40);
        var adapter = new FakeGrandCompanyAdapter
        {
            PurchaseResult = true,
            OnPurchase = (_, _, target) => quantities.Set(21072, target)
        };
        var activity = Create(
            adapter,
            quantities,
            new FakeSealStateProvider(new GrandCompanySealState(89_500, 90_000)),
            minimum: 10,
            target: 50,
            buffer: 1_000);

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.Equal(1, adapter.PurchaseCalls);
        Assert.Equal(50, adapter.LastPurchaseTarget);
        Assert.Equal(0, adapter.DeliveryCalls);
    }

    [Fact]
    public async Task NearCapAtConfiguredTargetBlocksBeforeGcTurnIn()
    {
        var quantities = new MutableQuantityProvider(21072, 50);
        var adapter = new FakeGrandCompanyAdapter();
        var activity = Create(
            adapter,
            quantities,
            new FakeSealStateProvider(new GrandCompanySealState(89_750, 90_000)),
            minimum: 10,
            target: 50,
            buffer: 1_000);

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Block, result.Disposition);
        Assert.False(activity.IsComplete);
        Assert.Equal(0, adapter.PurchaseCalls);
        Assert.Contains("target", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PressurePurchaseFailureNeverFallsBackToExpertDeliveryOrRepeats()
    {
        var quantities = new MutableQuantityProvider(21072, 40);
        var adapter = new FakeGrandCompanyAdapter { PurchaseResult = false };
        var activity = Create(
            adapter,
            quantities,
            new FakeSealStateProvider(new GrandCompanySealState(89_500, 90_000)),
            minimum: 10,
            target: 50,
            buffer: 1_000);

        var first = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var second = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Block, first.Disposition);
        Assert.Equal(ExecutionDisposition.Block, second.Disposition);
        Assert.Equal(1, adapter.PurchaseCalls);
        Assert.Equal(0, adapter.DeliveryCalls);
    }

    [Fact]
    public async Task LowReserveStillUsesExistingGuardedRefillFlow()
    {
        var quantities = new MutableQuantityProvider(21072, 3);
        var adapter = new FakeGrandCompanyAdapter
        {
            PurchaseResult = true,
            OnPurchase = (_, _, target) => quantities.Set(21072, target)
        };
        var state = new FakeSealStateProvider(null);
        var activity = Create(adapter, quantities, state, minimum: 10, target: 50, buffer: 1_000);

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.Equal(1, adapter.PurchaseCalls);
        Assert.Equal(0, state.Calls);
        Assert.Equal(50, quantities.GetQuantity(21072));
    }

    private static VentureSealPressureActivity Create(
        FakeGrandCompanyAdapter adapter,
        MutableQuantityProvider quantities,
        FakeSealStateProvider state,
        int minimum,
        int target,
        int buffer)
        => new(
            adapter,
            quantities,
            state,
            new VentureTokenRefillOptions(
                VentureItemId: 21072,
                MinimumQuantity: minimum,
                TargetQuantity: target,
                ApprovedExpertDeliveryItemIds: []),
            new VentureSealPressureOptions(buffer));

    private sealed class MutableQuantityProvider(uint itemId, int quantity) : IItemQuantityProvider
    {
        private readonly Dictionary<uint, int> _quantities = new() { [itemId] = quantity };
        public int GetQuantity(uint requestedItemId) => _quantities.GetValueOrDefault(requestedItemId);
        public void Set(uint requestedItemId, int value) => _quantities[requestedItemId] = value;
    }

    private sealed class FakeSealStateProvider(GrandCompanySealState? state) : IGrandCompanySealStateProvider
    {
        public int Calls { get; private set; }

        public Task<GrandCompanySealState?> GetStateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(state);
        }
    }

    private sealed class FakeGrandCompanyAdapter : IGrandCompanyAdapter
    {
        public string Key => "fake-gc";
        public string DisplayName => "Fake GC";
        public bool PurchaseResult { get; set; }
        public int PurchaseCalls { get; private set; }
        public int DeliveryCalls { get; private set; }
        public int LastPurchaseTarget { get; private set; }
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
            LastPurchaseTarget = targetQuantity;
            OnPurchase?.Invoke(ventureItemId, currentQuantity, targetQuantity);
            return Task.FromResult(PurchaseResult);
        }

        public Task<bool> RunExpertDeliveryAsync(
            IReadOnlyCollection<uint> approvedItemIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeliveryCalls++;
            return Task.FromResult(false);
        }
    }
}

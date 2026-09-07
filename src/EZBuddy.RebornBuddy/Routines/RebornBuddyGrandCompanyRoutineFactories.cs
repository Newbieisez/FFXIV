using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Routines;

public sealed class RebornBuddyInventoryQuantityProvider : IItemQuantityProvider
{
    public int GetQuantity(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        return InventoryManager.FilledSlots
            .Where(slot => NormalizeItemId(slot.RawItemId) == itemId)
            .Sum(slot => checked((int)slot.Count));
    }

    private static uint NormalizeItemId(uint rawItemId)
    {
        if (rawItemId > 1_000_000U)
        {
            return rawItemId - 1_000_000U;
        }

        if (rawItemId > 500_000U)
        {
            return rawItemId - 500_000U;
        }

        return rawItemId;
    }
}

public sealed class RebornBuddyGrandCompanyExpertDeliveryRoutineFactory : IRoutineActivityFactory
{
    private readonly IReadOnlyCollection<uint> _approvedItemIds;

    public RebornBuddyGrandCompanyExpertDeliveryRoutineFactory(IReadOnlyCollection<uint> approvedItemIds)
    {
        _approvedItemIds = approvedItemIds ?? throw new ArgumentNullException(nameof(approvedItemIds));
    }

    public string RoutineKey => "gc-expert-delivery";

    public Task<IEZActivity?> CreateAsync(
        RoutineDefinition routine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_approvedItemIds.Count == 0)
        {
            return Task.FromResult<IEZActivity?>(null);
        }

        var adapter = EZBuddyRuntime.Adapters.All.OfType<IGrandCompanyAdapter>().FirstOrDefault()
            ?? new LlamaGrandCompanyAdapter();
        IEZActivity activity = new GrandCompanyExpertDeliveryActivity(
            adapter,
            new GrandCompanyExpertDeliveryOptions(_approvedItemIds));
        return Task.FromResult<IEZActivity?>(activity);
    }
}

public sealed class RebornBuddyVentureTokenRefillRoutineFactory : IRoutineActivityFactory
{
    private readonly IReadOnlyCollection<uint> _approvedExpertDeliveryItemIds;
    private readonly int _minimumQuantity;
    private readonly int _targetQuantity;
    private readonly int _sealSafetyBuffer;

    public RebornBuddyVentureTokenRefillRoutineFactory(
        IReadOnlyCollection<uint> approvedExpertDeliveryItemIds,
        int minimumQuantity,
        int targetQuantity,
        int sealSafetyBuffer = 1000)
    {
        _approvedExpertDeliveryItemIds = approvedExpertDeliveryItemIds
            ?? throw new ArgumentNullException(nameof(approvedExpertDeliveryItemIds));
        _minimumQuantity = minimumQuantity;
        _targetQuantity = targetQuantity;
        _sealSafetyBuffer = sealSafetyBuffer;
    }

    public string RoutineKey => "retainer-venture-refill";

    public Task<IEZActivity?> CreateAsync(
        RoutineDefinition routine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = EZBuddyRuntime.Adapters.All.OfType<IGrandCompanyAdapter>().FirstOrDefault()
            ?? new LlamaGrandCompanyAdapter();
        var quantities = new RebornBuddyInventoryQuantityProvider();
        IEZActivity activity = new VentureSealPressureActivity(
            adapter,
            quantities,
            new LlamaGrandCompanySealStateProvider(),
            new VentureTokenRefillOptions(
                VentureItemId: 21072,
                MinimumQuantity: _minimumQuantity,
                TargetQuantity: _targetQuantity,
                ApprovedExpertDeliveryItemIds: _approvedExpertDeliveryItemIds),
            new VentureSealPressureOptions(_sealSafetyBuffer));
        return Task.FromResult<IEZActivity?>(activity);
    }
}

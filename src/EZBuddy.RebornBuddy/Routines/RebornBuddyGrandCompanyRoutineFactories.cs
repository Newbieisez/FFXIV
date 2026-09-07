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
    private const string EnableEnvironmentKey = "EZBUDDY_RUN_GC_EXPERT_DELIVERY";
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
        if (!ReadBooleanEnvironmentFlag(EnableEnvironmentKey) || _approvedItemIds.Count == 0)
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

    internal static bool IsEnabled() => ReadBooleanEnvironmentFlag(EnableEnvironmentKey);

    private static bool ReadBooleanEnvironmentFlag(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RebornBuddyVentureTokenRefillRoutineFactory : IRoutineActivityFactory
{
    private const string EnableEnvironmentKey = "EZBUDDY_RUN_VENTURE_REFILL";
    private readonly IReadOnlyCollection<uint> _approvedExpertDeliveryItemIds;

    public RebornBuddyVentureTokenRefillRoutineFactory(IReadOnlyCollection<uint> approvedExpertDeliveryItemIds)
    {
        _approvedExpertDeliveryItemIds = approvedExpertDeliveryItemIds
            ?? throw new ArgumentNullException(nameof(approvedExpertDeliveryItemIds));
    }

    public string RoutineKey => "retainer-venture-refill";

    public Task<IEZActivity?> CreateAsync(
        RoutineDefinition routine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReadBooleanEnvironmentFlag(EnableEnvironmentKey))
        {
            return Task.FromResult<IEZActivity?>(null);
        }

        var adapter = EZBuddyRuntime.Adapters.All.OfType<IGrandCompanyAdapter>().FirstOrDefault()
            ?? new LlamaGrandCompanyAdapter();
        IEZActivity activity = new VentureTokenRefillActivity(
            adapter,
            new RebornBuddyInventoryQuantityProvider(),
            new VentureTokenRefillOptions(
                VentureItemId: 21072,
                MinimumQuantity: ReadPositiveInt("EZBUDDY_VENTURE_MINIMUM") ?? 10,
                TargetQuantity: ReadPositiveInt("EZBUDDY_VENTURE_TARGET") ?? 50,
                ApprovedExpertDeliveryItemIds: _approvedExpertDeliveryItemIds));
        return Task.FromResult<IEZActivity?>(activity);
    }

    internal static bool IsEnabled() => ReadBooleanEnvironmentFlag(EnableEnvironmentKey);

    private static int? ReadPositiveInt(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new InvalidOperationException($"{key} must be a non-negative integer when configured.");
        }

        return parsed;
    }

    private static bool ReadBooleanEnvironmentFlag(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
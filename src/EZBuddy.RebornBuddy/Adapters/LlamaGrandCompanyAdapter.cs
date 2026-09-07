using System.Reflection;
using EZBuddy.Core.Adapters;
using EZBuddy.RebornBuddy.Interop;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class LlamaGrandCompanyAdapter : IGrandCompanyAdapter
{
    private const string ShopTypeName = "LlamaLibrary.Helpers.GrandCompanyShop";
    private const string ExpertDeliveryTypeName = "LlamaLibrary.Helpers.ExpertDelivery";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);

    public string Key => "llama-grand-company";
    public string DisplayName => "LlamaLibrary Grand Company Bridge";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var shopType = OptionalRuntimeInterop.ResolveType(ShopTypeName);
        if (shopType is null)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Missing,
                "LlamaLibrary Grand Company shop helper is not loaded.",
                DateTimeOffset.UtcNow));
        }

        var buyMethod = OptionalRuntimeInterop.ResolveStaticMethod(
            ShopTypeName,
            "BuyKnownItem",
            typeof(uint),
            typeof(int));

        if (buyMethod is null || !typeof(Task).IsAssignableFrom(buyMethod.ReturnType))
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Degraded,
                "LlamaLibrary is loaded but the allowlisted BuyKnownItem API is unavailable.",
                DateTimeOffset.UtcNow,
                shopType.Assembly.GetName().Version));
        }

        var expertAvailable = ResolveExpertDeliveryMethod() is not null;
        var message = expertAvailable
            ? "GC seal shop and explicit Expert Delivery APIs are ready."
            : "GC seal shop is ready; Expert Delivery API is unavailable.";

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            expertAvailable ? AdapterHealth.Ready : AdapterHealth.Degraded,
            message,
            DateTimeOffset.UtcNow,
            shopType.Assembly.GetName().Version));
    }

    public async Task<bool> EnsureVentureTokensAsync(
        uint ventureItemId,
        int currentQuantity,
        int targetQuantity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ventureItemId == 0 || currentQuantity < 0 || targetQuantity < 0)
        {
            return false;
        }

        if (currentQuantity >= targetQuantity)
        {
            return true;
        }

        var method = OptionalRuntimeInterop.ResolveStaticMethod(
            ShopTypeName,
            "BuyKnownItem",
            typeof(uint),
            typeof(int));
        if (method is null)
        {
            return false;
        }

        var quantityNeeded = checked(targetQuantity - currentQuantity);
        var result = method.Invoke(null, [ventureItemId, quantityNeeded]);
        if (result is not Task task)
        {
            return false;
        }

        await task.WaitAsync(OperationTimeout, cancellationToken);
        var purchased = OptionalRuntimeInterop.ReadTaskResult<int>(task);
        return purchased > 0 && currentQuantity + purchased >= targetQuantity;
    }

    public async Task<bool> RunExpertDeliveryAsync(
        IReadOnlyCollection<uint> approvedItemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedItemIds);
        cancellationToken.ThrowIfCancellationRequested();

        var itemIds = approvedItemIds.Where(itemId => itemId != 0).Distinct().ToArray();
        if (itemIds.Length == 0)
        {
            return false;
        }

        var method = ResolveExpertDeliveryMethod();
        if (method is null)
        {
            return false;
        }

        var result = method.Invoke(null, [itemIds]);
        if (result is not Task task)
        {
            return false;
        }

        await task.WaitAsync(OperationTimeout, cancellationToken);
        var status = OptionalRuntimeInterop.ReadTaskResultObject(task)?.ToString();
        return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
    }

    private static MethodInfo? ResolveExpertDeliveryMethod()
        => OptionalRuntimeInterop.ResolveStaticMethod(
            ExpertDeliveryTypeName,
            "DeliverItems",
            method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    return false;
                }

                var parameterType = parameters[0].ParameterType;
                return (parameterType.IsAssignableFrom(typeof(uint[])) ||
                        parameterType == typeof(IEnumerable<uint>)) &&
                       typeof(Task).IsAssignableFrom(method.ReturnType);
            });
}

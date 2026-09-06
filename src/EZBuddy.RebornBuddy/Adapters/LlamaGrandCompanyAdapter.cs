using System.Reflection;
using EZBuddy.Core.Adapters;

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

        var shopType = ResolveType(ShopTypeName);
        if (shopType is null)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Missing,
                "LlamaLibrary Grand Company shop helper is not loaded.",
                DateTimeOffset.UtcNow));
        }

        var buyMethod = shopType.GetMethod(
            "BuyKnownItem",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(uint), typeof(int)],
            modifiers: null);

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

        var shopType = ResolveType(ShopTypeName);
        var method = shopType?.GetMethod(
            "BuyKnownItem",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(uint), typeof(int)],
            modifiers: null);

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

        await task.WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        var purchased = ReadTaskResult<int>(task);
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

        await task.WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        var status = ReadTaskResultObject(task)?.ToString();
        return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
    }

    private static MethodInfo? ResolveExpertDeliveryMethod()
    {
        var type = ResolveType(ExpertDeliveryTypeName);
        if (type is null)
        {
            return null;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, "DeliverItems", StringComparison.Ordinal))
            .Where(method => method.GetParameters().Length == 1)
            .FirstOrDefault(method =>
            {
                var parameterType = method.GetParameters()[0].ParameterType;
                return parameterType.IsAssignableFrom(typeof(uint[])) ||
                       parameterType == typeof(IEnumerable<uint>);
            });
    }

    private static Type? ResolveType(string fullName)
        => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly =>
            {
                try
                {
                    return assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(type => type is not null);

    private static T ReadTaskResult<T>(Task task)
    {
        var value = ReadTaskResultObject(task);
        if (value is T typed)
        {
            return typed;
        }

        try
        {
            return value is null ? default! : (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default!;
        }
    }

    private static object? ReadTaskResultObject(Task task)
        => task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(task);
}

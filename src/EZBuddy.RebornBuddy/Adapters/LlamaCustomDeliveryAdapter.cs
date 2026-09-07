using System.Reflection;
using EZBuddy.Core.Adapters;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Interop;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class LlamaCustomDeliveryAdapter : ICustomDeliveryAdapter
{
    private const string UtilityTypeName = "LlamaLibrary.Utilities.CustomDeliveries";
    private const string SelectionMethodName = "RunCustomDeliveriesBySelection";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(45);

    private static readonly string[] ClientOrder =
    [
        "zhloe-aliapoh",
        "mnaago",
        "kurenai",
        "adkiragh",
        "kaishirr",
        "ehll-tou",
        "charlemend",
        "ameliance",
        "anden",
        "margrat",
        "nitowikwe",
        "tiisol-ja"
    ];

    public string Key => "llama-custom-deliveries";
    public string DisplayName => "LlamaLibrary Custom Deliveries Bridge";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = OptionalRuntimeInterop.ResolveType(UtilityTypeName);
        if (type is null)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Missing,
                "LlamaLibrary Custom Deliveries utility is not loaded.",
                DateTimeOffset.UtcNow));
        }

        var method = ResolveSelectionMethod();
        if (method is null)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Degraded,
                "LlamaLibrary is loaded but the public selected-client Custom Deliveries workflow is unavailable.",
                DateTimeOffset.UtcNow,
                type.Assembly.GetName().Version));
        }

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Ready,
            "Selected-client Custom Deliveries workflow is ready.",
            DateTimeOffset.UtcNow,
            type.Assembly.GetName().Version));
    }

    public async Task<bool> RunSelectedAsync(
        IReadOnlyCollection<string> clientKeys,
        string craftingClassKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(craftingClassKey);
        cancellationToken.ThrowIfCancellationRequested();

        var selected = clientKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(NormalizeClientKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            return false;
        }

        var unknown = selected
            .Where(key => !ClientOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (unknown.Length > 0)
        {
            return false;
        }

        var method = ResolveSelectionMethod();
        if (method is null)
        {
            return false;
        }

        var parameters = method.GetParameters();
        var craftingClassType = parameters[^1].ParameterType;
        if (!craftingClassType.IsEnum)
        {
            return false;
        }

        object craftingClass;
        try
        {
            craftingClass = Enum.Parse(craftingClassType, craftingClassKey.Trim(), ignoreCase: true);
        }
        catch
        {
            return false;
        }

        var arguments = new object[ClientOrder.Length + 1];
        for (var index = 0; index < ClientOrder.Length; index++)
        {
            arguments[index] = selected.Contains(ClientOrder[index]);
        }
        arguments[^1] = craftingClass;

        using var transition = HostLifecycleTransition.BeginInternalTransition();
        var invoked = method.Invoke(null, arguments);
        if (invoked is not Task task)
        {
            return false;
        }

        await task.WaitAsync(OperationTimeout, cancellationToken);
        return OptionalRuntimeInterop.ReadTaskResult<bool>(task);
    }

    private static MethodInfo? ResolveSelectionMethod()
        => OptionalRuntimeInterop.ResolveStaticMethod(
            UtilityTypeName,
            SelectionMethodName,
            method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == ClientOrder.Length + 1 &&
                       parameters.Take(ClientOrder.Length).All(parameter => parameter.ParameterType == typeof(bool)) &&
                       parameters[^1].ParameterType.IsEnum &&
                       typeof(Task).IsAssignableFrom(method.ReturnType);
            });

    private static string NormalizeClientKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal)
            .Replace("_", "-", StringComparison.Ordinal);

        return normalized switch
        {
            "zhloe" => "zhloe-aliapoh",
            "zhloe-aliapoh" => "zhloe-aliapoh",
            "m-naago" or "mnaago" => "mnaago",
            "kai-shirr" or "kaishirr" => "kaishirr",
            "ehll-tou" or "ehlltou" => "ehll-tou",
            "tiisol-ja" or "tiisolja" => "tiisol-ja",
            _ => normalized
        };
    }
}

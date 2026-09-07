using System.Reflection;
using Clio.Utilities;
using EZBuddy.Core.Adapters;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class LisbethAdapter : ILisbethAdapter
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(30);

    public string Key => "lisbeth";
    public string DisplayName => "Lisbeth";

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryResolve(out var context, out var failure))
            {
                return new AdapterStatus(Key, DisplayName, AdapterHealth.Missing, failure, DateTimeOffset.UtcNow);
            }

            var productKeyMethod = context.Api.GetType().GetMethod("IsProductKeyValid", BindingFlags.Public | BindingFlags.Instance);
            if (productKeyMethod is null)
            {
                return new AdapterStatus(Key, DisplayName, AdapterHealth.Degraded, "Lisbeth is loaded but the expected public product-key API is unavailable.", DateTimeOffset.UtcNow, context.Version);
            }

            var result = productKeyMethod.Invoke(context.Api, null);
            if (result is not Task<bool> productKeyTask)
            {
                return new AdapterStatus(Key, DisplayName, AdapterHealth.Degraded, "Lisbeth product-key API returned an unexpected type.", DateTimeOffset.UtcNow, context.Version);
            }

            var valid = await productKeyTask.WaitAsync(HealthTimeout, cancellationToken);
            return new AdapterStatus(
                Key,
                DisplayName,
                valid ? AdapterHealth.Ready : AdapterHealth.Degraded,
                valid ? "Lisbeth is installed and its public API is ready." : "Lisbeth is installed but its product key is not currently valid.",
                DateTimeOffset.UtcNow,
                context.Version);
        }
        catch (TimeoutException)
        {
            return new AdapterStatus(Key, DisplayName, AdapterHealth.Busy, "Lisbeth health check timed out.", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AdapterStatus(Key, DisplayName, AdapterHealth.Faulted, exception.Message, DateTimeOffset.UtcNow);
        }
    }

    public async Task<bool> ExecuteOrdersAsync(string ordersJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ordersJson);
        if (!TryResolve(out var context, out _))
        {
            return false;
        }

        var method = context.Lisbeth.GetType().GetMethod("ExecuteOrders", BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
        {
            return false;
        }

        var result = method.Invoke(context.Lisbeth, new object[] { ordersJson, false });
        return result is Task<bool> task && await task.WaitAsync(OperationTimeout, cancellationToken);
    }

    public async Task<bool> ExitCraftingAsync(CancellationToken cancellationToken = default)
    {
        if (!TryResolve(out var context, out _))
        {
            return false;
        }

        var method = context.Api.GetType().GetMethod("ExitCrafting", BindingFlags.Public | BindingFlags.Instance);
        if (method?.Invoke(context.Api, null) is not Task<bool> task)
        {
            return false;
        }

        return await task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
    }

    public async Task<bool> SelfRepairAsync(bool allowMenderFallback, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(out var context, out _))
        {
            return false;
        }

        var methodName = allowMenderFallback ? "SelfRepairWithMenderFallback" : "SelfRepair";
        var method = context.Api.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        if (method?.Invoke(context.Api, null) is not Task task)
        {
            return false;
        }

        await task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        return true;
    }

    public async Task<bool> ExtractMateriaAsync(CancellationToken cancellationToken = default)
    {
        if (!TryResolve(out var context, out _))
        {
            return false;
        }

        var method = context.Api.GetType().GetMethod("ExtractMateria", BindingFlags.Public | BindingFlags.Instance);
        if (method?.Invoke(context.Api, null) is not Task task)
        {
            return false;
        }

        await task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        return true;
    }

    public async Task<bool> TravelAsync(uint zoneId, float x, float y, float z, bool land = true, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(out var context, out _))
        {
            return false;
        }

        var method = context.Api.GetType().GetMethod("TravelToWithoutSubzone", BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
        {
            return false;
        }

        Func<bool> continueCondition = static () => true;
        var position = new Vector3(x, y, z);
        var result = method.Invoke(context.Api, new object[] { zoneId, position, continueCondition, land });
        return result is Task<bool> task && await task.WaitAsync(OperationTimeout, cancellationToken);
    }

    private static bool TryResolve(out LisbethContext context, out string failure)
    {
        context = default;
        failure = string.Empty;

        var loader = BotManager.Bots.FirstOrDefault(bot =>
            string.Equals(bot.Name, "Lisbeth", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bot.EnglishName, "Lisbeth", StringComparison.OrdinalIgnoreCase));

        if (loader is null)
        {
            failure = "Lisbeth botbase is not installed or loaded.";
            return false;
        }

        var lisbethProperty = loader.GetType().GetProperty("Lisbeth", BindingFlags.Public | BindingFlags.Instance);
        var lisbeth = lisbethProperty?.GetValue(loader);
        if (lisbeth is null)
        {
            failure = "Lisbeth loader is present, but its public Lisbeth object is unavailable.";
            return false;
        }

        var apiProperty = lisbeth.GetType().GetProperty("Api", BindingFlags.Public | BindingFlags.Instance);
        var api = apiProperty?.GetValue(lisbeth);
        if (api is null)
        {
            failure = "Lisbeth is loaded, but its public API object is unavailable.";
            return false;
        }

        context = new LisbethContext(lisbeth, api, loader.GetType().Assembly.GetName().Version);
        return true;
    }

    private readonly record struct LisbethContext(object Lisbeth, object Api, Version? Version);
}

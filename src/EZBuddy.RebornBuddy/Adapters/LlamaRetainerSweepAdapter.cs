using System.Reflection;
using EZBuddy.Core.Adapters;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class LlamaRetainerSweepAdapter : IRetainerSweepAdapter
{
    private const string HelperTypeName = "LlamaLibrary.Retainers.HelperFunctions";
    private const string SweepMethodName = "CheckVentureTask";
    private static readonly TimeSpan SweepTimeout = TimeSpan.FromMinutes(15);

    public string Key => "llama-retainers";
    public string DisplayName => "LlamaLibrary Retainer Bridge";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolve(out var method, out var version, out var failure))
        {
            return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Missing, failure, DateTimeOffset.UtcNow));
        }

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Ready,
            $"Allowlisted retainer sweep bridge ready ({method.DeclaringType?.FullName}.{method.Name}).",
            DateTimeOffset.UtcNow,
            version));
    }

    public async Task<bool> SweepCompletedVenturesAsync(CancellationToken cancellationToken = default)
    {
        if (!TryResolve(out var method, out _, out _))
        {
            return false;
        }

        var result = method.Invoke(null, null);
        if (result is not Task task)
        {
            return false;
        }

        await task.WaitAsync(SweepTimeout, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool TryResolve(out MethodInfo method, out Version? version, out string failure)
    {
        method = null!;
        version = null;
        failure = string.Empty;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => candidate.GetType(HelperTypeName, throwOnError: false, ignoreCase: false) is not null);
        if (assembly is null)
        {
            failure = "LlamaLibrary retainer helpers are not loaded.";
            return false;
        }

        var type = assembly.GetType(HelperTypeName, throwOnError: false, ignoreCase: false);
        method = type?.GetMethod(SweepMethodName, BindingFlags.Public | BindingFlags.Static, binder: null, Type.EmptyTypes, modifiers: null)!;
        if (method is null || !typeof(Task).IsAssignableFrom(method.ReturnType))
        {
            failure = "The allowlisted LlamaLibrary CheckVentureTask API is unavailable or incompatible.";
            return false;
        }

        version = assembly.GetName().Version;
        return true;
    }
}

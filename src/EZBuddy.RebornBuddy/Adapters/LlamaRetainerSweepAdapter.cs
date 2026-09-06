using System.Reflection;
using EZBuddy.Core.Adapters;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class LlamaRetainerSweepAdapter : IRetainerSweepAdapter
{
    private const string HelperTypeName = "LlamaLibrary.Retainers.HelperFunctions";
    private const string SweepMethodName = "CheckVentureTask";
    private static readonly TimeSpan SweepTimeout = TimeSpan.FromMinutes(15);
    private readonly IRetainerSweepAdapter _fallback;

    public LlamaRetainerSweepAdapter(IRetainerSweepAdapter? fallback = null)
    {
        _fallback = fallback ?? new NativeRetainerSweepAdapter();
    }

    public string Key => "llama-retainers";
    public string DisplayName => "Retainer Sweep Bridge";

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryResolve(out var method, out var version, out _))
        {
            return new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Ready,
                $"LlamaLibrary retainer sweep ready ({method.DeclaringType?.FullName}.{method.Name}); native fallback remains available if supported by this RebornBuddy build.",
                DateTimeOffset.UtcNow,
                version);
        }

        var fallbackStatus = await _fallback.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return fallbackStatus.Health is AdapterHealth.Ready or AdapterHealth.Busy
            ? fallbackStatus with
            {
                Key = Key,
                DisplayName = DisplayName,
                Message = $"LlamaLibrary retainer helper unavailable; using {fallbackStatus.DisplayName}. {fallbackStatus.Message}"
            }
            : new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Missing,
                $"Neither the allowlisted LlamaLibrary retainer helper nor a compatible native RebornBuddy retainer sweep API is available. {fallbackStatus.Message}",
                DateTimeOffset.UtcNow);
    }

    public async Task<bool> SweepCompletedVenturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryResolve(out var method, out _, out _))
        {
            try
            {
                var result = method.Invoke(null, null);
                if (result is Task task)
                {
                    await task.WaitAsync(SweepTimeout, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TargetInvocationException exception)
            {
                ff14bot.Helpers.Logging.Write($"[EZBuddy Retainers] Llama sweep failed; trying fallback: {exception.InnerException?.Message ?? exception.Message}");
            }
            catch (Exception exception)
            {
                ff14bot.Helpers.Logging.Write($"[EZBuddy Retainers] Llama sweep failed; trying fallback: {exception.Message}");
            }
        }

        return await _fallback.SweepCompletedVenturesAsync(cancellationToken).ConfigureAwait(false);
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

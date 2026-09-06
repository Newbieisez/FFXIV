using System.Reflection;
using EZBuddy.Core.Adapters;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class NativeRetainerSweepAdapter : IRetainerSweepAdapter
{
    private static readonly string[] CandidateTypeNames =
    [
        "ff14bot.RemoteWindows.RetainerList",
        "ff14bot.RemoteWindows.AddonRetainerList",
        "ff14bot.RemoteAgents.AgentRetainerList"
    ];

    private static readonly string[] ApprovedSweepMethods =
    [
        "SweepCompletedVenturesAsync",
        "ProcessCompletedVenturesAsync",
        "CheckVentureTask"
    ];

    private static readonly TimeSpan SweepTimeout = TimeSpan.FromMinutes(15);

    public string Key => "rebornbuddy-retainers";
    public string DisplayName => "RebornBuddy Retainer Fallback";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveSweepMethod(out var type, out var method, out _))
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Missing,
                "This RebornBuddy build does not expose an allowlisted public retainer sweep operation. EZBuddy will not use raw offsets as a fallback.",
                DateTimeOffset.UtcNow));
        }

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Ready,
            $"Native retainer fallback ready via {type.FullName}.{method.Name}.",
            DateTimeOffset.UtcNow,
            type.Assembly.GetName().Version));
    }

    public async Task<bool> SweepCompletedVenturesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveSweepMethod(out var type, out var method, out var target))
        {
            return false;
        }

        try
        {
            var result = method.Invoke(target, null);
            if (result is not Task task)
            {
                return false;
            }

            await task.WaitAsync(SweepTimeout, cancellationToken).ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            if (resultProperty is null)
            {
                return true;
            }

            var value = resultProperty.GetValue(task);
            return value is not bool booleanResult || booleanResult;
        }
        catch (TargetInvocationException exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Retainer Fallback] {exception.InnerException?.Message ?? exception.Message}");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Retainer Fallback] {exception.Message}");
            return false;
        }
    }

    private static bool TryResolveSweepMethod(out Type type, out MethodInfo method, out object? target)
    {
        type = null!;
        method = null!;
        target = null;

        foreach (var typeName in CandidateTypeNames)
        {
            var candidate = ResolveType(typeName);
            if (candidate is null)
            {
                continue;
            }

            foreach (var methodName in ApprovedSweepMethods)
            {
                var candidateMethod = candidate.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                if (candidateMethod is null || !typeof(Task).IsAssignableFrom(candidateMethod.ReturnType))
                {
                    continue;
                }

                object? candidateTarget = null;
                if (!candidateMethod.IsStatic)
                {
                    candidateTarget = candidate.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (candidateTarget is null)
                    {
                        continue;
                    }
                }

                type = candidate;
                method = candidateMethod;
                target = candidateTarget;
                return true;
            }
        }

        return false;
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
            .FirstOrDefault(candidate => candidate is not null);
}

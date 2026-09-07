using System.Reflection;
using Buddy.Coroutines;
using EZBuddy.Core.Adapters;
using ff14bot.Enums;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class RebornBuddyDutySupportAdapter : IDutySupportAdapter
{
    private const string DawnStoryTypeName = "LlamaLibrary.RemoteWindows.DawnStory";
    private const string AgentDawnStoryTypeName = "LlamaLibrary.RemoteAgents.AgentDawnStory";
    private const string DawnTypeName = "LlamaLibrary.RemoteWindows.Dawn";
    private const string AgentDawnTypeName = "LlamaLibrary.RemoteAgents.AgentDawn";

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EntryTimeout = TimeSpan.FromMinutes(3);

    public string Key => "rebornbuddy-duty-support";
    public string DisplayName => "Duty Support / Trust Bridge";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ff14bot.Core.Player is null)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Busy,
                "Character is not currently available.",
                DateTimeOffset.UtcNow));
        }

        if (!TryResolveWindowBridge(out var version, out var failure))
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Missing,
                failure,
                DateTimeOffset.UtcNow));
        }

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Ready,
            "RebornBuddy DutyManager is ready and the allowlisted Duty Support/Trust window bridge is loaded.",
            DateTimeOffset.UtcNow,
            version));
    }

    public Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = DutyManager.QueueState;
        return Task.FromResult(new DutyAutomationStatus(
            state.ToString(),
            state == QueueState.InQueue,
            state == QueueState.CommenceAvailable,
            state == QueueState.JoiningInstance,
            state == QueueState.InDungeon));
    }

    public async Task<bool> EnterAsync(DutyAutomationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.DutyId == 0 || !TryResolveWindowBridge(out _, out _))
        {
            return false;
        }

        if (DutyManager.QueueState == QueueState.InDungeon)
        {
            return true;
        }

        if (DutyManager.QueueState == QueueState.None)
        {
            var queued = request.Mode switch
            {
                DutyAutomationMode.DutySupport => await QueueDutySupportAsync(request.DutyId, cancellationToken).ConfigureAwait(true),
                DutyAutomationMode.Trust => await QueueTrustAsync(request.TrustId, cancellationToken).ConfigureAwait(true),
                _ => false
            };

            if (!queued)
            {
                return false;
            }
        }

        return await WaitForEntryAsync(cancellationToken).ConfigureAwait(true);
    }

    private static async Task<bool> QueueDutySupportAsync(uint dutyId, CancellationToken cancellationToken)
    {
        var dawnStory = GetStaticInstance(DawnStoryTypeName);
        var agentDawnStory = GetStaticInstance(AgentDawnStoryTypeName);
        if (dawnStory is null || agentDawnStory is null)
        {
            return false;
        }

        if (!GetBooleanProperty(dawnStory, "IsOpen"))
        {
            if (!InvokeVoid(agentDawnStory, "Toggle"))
            {
                return false;
            }

            if (!await WaitUntilAsync(
                    () => GetBooleanProperty(dawnStory, "IsOpen"),
                    WindowTimeout,
                    cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var selected = await InvokeTaskBoolAsync(
            dawnStory,
            "SelectDuty",
            [(int)dutyId],
            WindowTimeout,
            cancellationToken).ConfigureAwait(true);
        if (!selected)
        {
            return false;
        }

        if (!InvokeVoid(dawnStory, "Commence"))
        {
            return false;
        }

        return await WaitUntilAsync(
            () => DutyManager.QueueState != QueueState.None,
            WindowTimeout,
            cancellationToken).ConfigureAwait(true);
    }

    private static async Task<bool> QueueTrustAsync(int? trustId, CancellationToken cancellationToken)
    {
        if (trustId is null or <= 0)
        {
            return false;
        }

        var dawn = GetStaticInstance(DawnTypeName);
        var agentDawn = GetStaticInstance(AgentDawnTypeName);
        if (dawn is null || agentDawn is null)
        {
            return false;
        }

        var currentTrustId = GetInt32Property(agentDawn, "TrustId");
        if (GetBooleanProperty(dawn, "IsOpen") && currentTrustId != trustId.Value)
        {
            if (!InvokeVoid(agentDawn, "Toggle"))
            {
                return false;
            }

            if (!await WaitUntilAsync(
                    () => !GetBooleanProperty(dawn, "IsOpen"),
                    WindowTimeout,
                    cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        if (!SetProperty(agentDawn, "TrustId", trustId.Value))
        {
            return false;
        }

        if (!await WaitUntilAsync(
                () => GetInt32Property(agentDawn, "TrustId") == trustId.Value,
                WindowTimeout,
                cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        if (!GetBooleanProperty(dawn, "IsOpen"))
        {
            if (!InvokeVoid(agentDawn, "Toggle"))
            {
                return false;
            }

            if (!await WaitUntilAsync(
                    () => GetBooleanProperty(dawn, "IsOpen"),
                    WindowTimeout,
                    cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!InvokeVoid(dawn, "Register"))
        {
            return false;
        }

        return await WaitUntilAsync(
            () => DutyManager.QueueState != QueueState.None,
            WindowTimeout,
            cancellationToken).ConfigureAwait(true);
    }

    private static async Task<bool> WaitForEntryAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var commenceSent = false;

        while (DateTimeOffset.UtcNow - startedAt < EntryTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = DutyManager.QueueState;

            if (state == QueueState.InDungeon)
            {
                return true;
            }

            if (state == QueueState.None)
            {
                return false;
            }

            if (state == QueueState.JoiningInstance && !commenceSent)
            {
                DutyManager.Commence();
                commenceSent = true;
            }

            await Coroutine.Yield();
        }

        return DutyManager.QueueState == QueueState.InDungeon;
    }

    private static bool TryResolveWindowBridge(out Version? version, out string failure)
    {
        version = null;
        failure = string.Empty;

        var requiredTypes = new[]
        {
            DawnStoryTypeName,
            AgentDawnStoryTypeName,
            DawnTypeName,
            AgentDawnTypeName
        };

        Assembly? bridgeAssembly = null;
        foreach (var typeName in requiredTypes)
        {
            var type = ResolveType(typeName);
            if (type is null)
            {
                failure = $"Optional LlamaLibrary Duty Support/Trust wrapper '{typeName}' is not loaded. Native DutyManager remains available, but EZBuddy will not use raw offsets for these windows.";
                return false;
            }

            bridgeAssembly ??= type.Assembly;
            if (GetStaticInstance(typeName) is null)
            {
                failure = $"Optional window wrapper '{typeName}' is loaded but its singleton instance is unavailable.";
                return false;
            }
        }

        version = bridgeAssembly?.GetName().Version;
        return true;
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

    private static object? GetStaticInstance(string typeName)
    {
        var type = ResolveType(typeName);
        return type?.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null);
    }

    private static bool GetBooleanProperty(object target, string propertyName)
        => target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) is true;

    private static int? GetInt32Property(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        return value switch
        {
            int number => number,
            byte number => number,
            _ => null
        };
    }

    private static bool SetProperty(object target, string propertyName, object value)
    {
        try
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite)
            {
                return false;
            }

            property.SetValue(target, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool InvokeVoid(object target, string methodName)
    {
        try
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method is null || method.ReturnType != typeof(void))
            {
                return false;
            }

            method.Invoke(target, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> InvokeTaskBoolAsync(
        object target,
        string methodName,
        object[] arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var argumentTypes = arguments.Select(argument => argument.GetType()).ToArray();
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: argumentTypes,
                modifiers: null);
            if (method?.Invoke(target, arguments) is not Task<bool> task)
            {
                return false;
            }

            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate())
            {
                return true;
            }

            await Coroutine.Yield();
        }

        return predicate();
    }
}

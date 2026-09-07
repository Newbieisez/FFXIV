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

    private readonly object _bridgeSync = new();
    private bool _bridgeResolutionAttempted;
    private BridgeBinding? _bridge;
    private string _bridgeFailure = "Duty Support/Trust wrapper has not been resolved yet.";

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

        lock (_bridgeSync)
        {
            if (!_bridgeResolutionAttempted)
            {
                return Task.FromResult(new AdapterStatus(
                    Key,
                    DisplayName,
                    AdapterHealth.Degraded,
                    "Native DutyManager is available. Optional LlamaLibrary Duty Support/Trust reflection binding is deferred until the first duty request so plugin load order cannot permanently poison discovery.",
                    DateTimeOffset.UtcNow));
            }

            if (_bridge is null)
            {
                return Task.FromResult(new AdapterStatus(
                    Key,
                    DisplayName,
                    AdapterHealth.Missing,
                    _bridgeFailure,
                    DateTimeOffset.UtcNow));
            }

            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Ready,
                "RebornBuddy DutyManager is ready and the cached allowlisted Duty Support/Trust window bridge is loaded.",
                DateTimeOffset.UtcNow,
                _bridge.Version));
        }
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

        if (request.DutyId == 0 || !TryGetBridge(out var bridge))
        {
            return false;
        }

        if (DutyManager.QueueState == QueueState.InDungeon)
        {
            return true;
        }

        if (DutyManager.QueueState != QueueState.None)
        {
            return true;
        }

        return request.Mode switch
        {
            DutyAutomationMode.DutySupport => await QueueDutySupportAsync(bridge, request.DutyId, cancellationToken).ConfigureAwait(true),
            DutyAutomationMode.Trust => await QueueTrustAsync(bridge, request.TrustId, cancellationToken).ConfigureAwait(true),
            _ => false
        };
    }

    public Task<bool> AdvanceEntryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = DutyManager.QueueState;

        if (state == QueueState.InDungeon)
        {
            return Task.FromResult(true);
        }

        if (state is QueueState.CommenceAvailable or QueueState.JoiningInstance)
        {
            DutyManager.Commence();
        }

        return Task.FromResult(state != QueueState.None);
    }

    private bool TryGetBridge(out BridgeBinding bridge)
    {
        lock (_bridgeSync)
        {
            if (!_bridgeResolutionAttempted)
            {
                _bridgeResolutionAttempted = true;
                _bridge = BridgeBinding.TryCreate(out _bridgeFailure);
            }

            bridge = _bridge!;
            return _bridge is not null;
        }
    }

    private static async Task<bool> QueueDutySupportAsync(
        BridgeBinding bridge,
        uint dutyId,
        CancellationToken cancellationToken)
    {
        if (!bridge.GetDawnStoryIsOpen())
        {
            if (!bridge.ToggleDawnStory())
            {
                return false;
            }

            if (!await WaitUntilAsync(
                    bridge.GetDawnStoryIsOpen,
                    WindowTimeout,
                    cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await bridge.SelectDutyAsync((int)dutyId, WindowTimeout, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        if (!bridge.CommenceDawnStory())
        {
            return false;
        }

        return await WaitUntilAsync(
            () => DutyManager.QueueState != QueueState.None,
            WindowTimeout,
            cancellationToken).ConfigureAwait(true);
    }

    private static async Task<bool> QueueTrustAsync(
        BridgeBinding bridge,
        int? trustId,
        CancellationToken cancellationToken)
    {
        if (trustId is null or <= 0)
        {
            return false;
        }

        var currentTrustId = bridge.GetTrustId();
        if (bridge.GetDawnIsOpen() && currentTrustId != trustId.Value)
        {
            if (!bridge.ToggleDawn())
            {
                return false;
            }

            if (!await WaitUntilAsync(
                    () => !bridge.GetDawnIsOpen(),
                    WindowTimeout,
                    cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        if (!bridge.SetTrustId(trustId.Value))
        {
            return false;
        }

        if (!await WaitUntilAsync(
                () => bridge.GetTrustId() == trustId.Value,
                WindowTimeout,
                cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        if (!bridge.GetDawnIsOpen())
        {
            if (!bridge.ToggleDawn())
            {
                return false;
            }

            if (!await WaitUntilAsync(
                    bridge.GetDawnIsOpen,
                    WindowTimeout,
                    cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        if (!bridge.RegisterDawn())
        {
            return false;
        }

        return await WaitUntilAsync(
            () => DutyManager.QueueState != QueueState.None,
            WindowTimeout,
            cancellationToken).ConfigureAwait(true);
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

    private sealed class BridgeBinding
    {
        private readonly object _dawnStory;
        private readonly object _agentDawnStory;
        private readonly object _dawn;
        private readonly object _agentDawn;
        private readonly PropertyInfo _dawnStoryIsOpen;
        private readonly MethodInfo _agentDawnStoryToggle;
        private readonly MethodInfo _dawnStorySelectDuty;
        private readonly MethodInfo _dawnStoryCommence;
        private readonly PropertyInfo _dawnIsOpen;
        private readonly MethodInfo _agentDawnToggle;
        private readonly PropertyInfo _trustId;
        private readonly MethodInfo _dawnRegister;

        private BridgeBinding(
            object dawnStory,
            object agentDawnStory,
            object dawn,
            object agentDawn,
            PropertyInfo dawnStoryIsOpen,
            MethodInfo agentDawnStoryToggle,
            MethodInfo dawnStorySelectDuty,
            MethodInfo dawnStoryCommence,
            PropertyInfo dawnIsOpen,
            MethodInfo agentDawnToggle,
            PropertyInfo trustId,
            MethodInfo dawnRegister,
            Version? version)
        {
            _dawnStory = dawnStory;
            _agentDawnStory = agentDawnStory;
            _dawn = dawn;
            _agentDawn = agentDawn;
            _dawnStoryIsOpen = dawnStoryIsOpen;
            _agentDawnStoryToggle = agentDawnStoryToggle;
            _dawnStorySelectDuty = dawnStorySelectDuty;
            _dawnStoryCommence = dawnStoryCommence;
            _dawnIsOpen = dawnIsOpen;
            _agentDawnToggle = agentDawnToggle;
            _trustId = trustId;
            _dawnRegister = dawnRegister;
            Version = version;
        }

        public Version? Version { get; }

        public static BridgeBinding? TryCreate(out string failure)
        {
            failure = string.Empty;

            var dawnStoryType = ResolveType(DawnStoryTypeName);
            var agentDawnStoryType = ResolveType(AgentDawnStoryTypeName);
            var dawnType = ResolveType(DawnTypeName);
            var agentDawnType = ResolveType(AgentDawnTypeName);

            if (dawnStoryType is null || agentDawnStoryType is null || dawnType is null || agentDawnType is null)
            {
                failure = "Optional LlamaLibrary Duty Support/Trust wrappers are not loaded. Native DutyManager remains available, but EZBuddy will not use raw offsets for Duty Support/Trust windows.";
                return null;
            }

            var dawnStory = GetSingleton(dawnStoryType);
            var agentDawnStory = GetSingleton(agentDawnStoryType);
            var dawn = GetSingleton(dawnType);
            var agentDawn = GetSingleton(agentDawnType);
            if (dawnStory is null || agentDawnStory is null || dawn is null || agentDawn is null)
            {
                failure = "LlamaLibrary Duty Support/Trust wrapper types are loaded, but one or more singleton instances are unavailable.";
                return null;
            }

            var dawnStoryIsOpen = GetReadableProperty(dawnStoryType, "IsOpen");
            var agentDawnStoryToggle = GetVoidMethod(agentDawnStoryType, "Toggle");
            var dawnStorySelectDuty = dawnStoryType.GetMethod(
                "SelectDuty",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(int)],
                modifiers: null);
            var dawnStoryCommence = GetVoidMethod(dawnStoryType, "Commence");
            var dawnIsOpen = GetReadableProperty(dawnType, "IsOpen");
            var agentDawnToggle = GetVoidMethod(agentDawnType, "Toggle");
            var trustId = agentDawnType.GetProperty("TrustId", BindingFlags.Public | BindingFlags.Instance);
            var dawnRegister = GetVoidMethod(dawnType, "Register");

            if (dawnStoryIsOpen is null ||
                agentDawnStoryToggle is null ||
                dawnStorySelectDuty is null ||
                dawnStorySelectDuty.ReturnType != typeof(Task<bool>) ||
                dawnStoryCommence is null ||
                dawnIsOpen is null ||
                agentDawnToggle is null ||
                trustId is null || !trustId.CanRead || !trustId.CanWrite ||
                dawnRegister is null)
            {
                failure = "LlamaLibrary is loaded, but the expected allowlisted Duty Support/Trust public API surface does not match this EZBuddy build.";
                return null;
            }

            return new BridgeBinding(
                dawnStory,
                agentDawnStory,
                dawn,
                agentDawn,
                dawnStoryIsOpen,
                agentDawnStoryToggle,
                dawnStorySelectDuty,
                dawnStoryCommence,
                dawnIsOpen,
                agentDawnToggle,
                trustId,
                dawnRegister,
                dawnStoryType.Assembly.GetName().Version);
        }

        public bool GetDawnStoryIsOpen() => ReadBool(_dawnStoryIsOpen, _dawnStory);
        public bool ToggleDawnStory() => InvokeVoid(_agentDawnStoryToggle, _agentDawnStory);
        public bool CommenceDawnStory() => InvokeVoid(_dawnStoryCommence, _dawnStory);
        public bool GetDawnIsOpen() => ReadBool(_dawnIsOpen, _dawn);
        public bool ToggleDawn() => InvokeVoid(_agentDawnToggle, _agentDawn);
        public bool RegisterDawn() => InvokeVoid(_dawnRegister, _dawn);

        public int? GetTrustId()
        {
            try
            {
                var value = _trustId.GetValue(_agentDawn);
                return value switch
                {
                    int number => number,
                    byte number => number,
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        public bool SetTrustId(int value)
        {
            try
            {
                var targetType = Nullable.GetUnderlyingType(_trustId.PropertyType) ?? _trustId.PropertyType;
                _trustId.SetValue(_agentDawn, Convert.ChangeType(value, targetType));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SelectDutyAsync(
            int dutyId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_dawnStorySelectDuty.Invoke(_dawnStory, [dutyId]) is not Task<bool> task)
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

        private static object? GetSingleton(Type type)
        {
            try
            {
                return type.GetProperty(
                        "Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static PropertyInfo? GetReadableProperty(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property is { CanRead: true } ? property : null;
        }

        private static MethodInfo? GetVoidMethod(Type type, string name)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            return method?.ReturnType == typeof(void) ? method : null;
        }

        private static bool ReadBool(PropertyInfo property, object target)
        {
            try
            {
                return property.GetValue(target) is true;
            }
            catch
            {
                return false;
            }
        }

        private static bool InvokeVoid(MethodInfo method, object target)
        {
            try
            {
                method.Invoke(target, null);
                return true;
            }
            catch
            {
                return false;
            }
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
    }
}

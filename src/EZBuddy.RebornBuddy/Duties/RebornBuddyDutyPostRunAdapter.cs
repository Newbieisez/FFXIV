using System.Reflection;
using EZBuddy.Core.Duties;
using ff14bot.Directors;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Duties;

public sealed class RebornBuddyDutyPostRunAdapter : IDutyPostRunAdapter
{
    private static readonly string[] LootTypeNames =
    [
        "ff14bot.RemoteWindows.NeedGreed",
        "LlamaLibrary.RemoteWindows.NeedGreed"
    ];

    private int _nextLootIndex;
    private bool _leaveRequested;
    private LootBridge? _lootBridge;
    private bool _lootBridgeResolved;

    public Task<DutyPostRunStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var complete = DirectorManager.ActiveDirector is InstanceContentDirector director && director.InstanceEnded;
        var hasLootBridge = TryGetLootBridge(out var bridge);
        var lootOpen = hasLootBridge && bridge.IsOpen;

        // If the duty is complete but this build exposes no compatible loot wrapper,
        // fail closed by treating loot as unresolved. The activity will request manual
        // intervention rather than leave the instance and potentially forfeit drops.
        if (complete && !hasLootBridge)
        {
            lootOpen = true;
        }

        var message = !complete
            ? "Instance director has not reported completion yet."
            : hasLootBridge
                ? "Instance director reports completion; Need/Greed capability is available."
                : "Instance director reports completion, but no compatible Need/Greed wrapper is loaded. Loot handling requires manual review.";

        return Task.FromResult(new DutyPostRunStatus(
            complete,
            lootOpen,
            _leaveRequested,
            message));
    }

    public Task<bool> ProcessLootAsync(
        DutyLootPolicy policy,
        int freeInventorySlots,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        if (!TryGetLootBridge(out var bridge))
        {
            ff14bot.Helpers.Logging.Write(
                "[EZBuddy Duty] Loot processing is unavailable because no compatible Need/Greed wrapper is loaded. EZBuddy will not guess raw window actions.");
            return Task.FromResult(false);
        }

        if (!bridge.IsOpen)
        {
            _nextLootIndex = 0;
            return Task.FromResult(true);
        }

        var itemCount = bridge.NumberOfItems;
        if (itemCount <= 0)
        {
            bridge.Close();
            _nextLootIndex = 0;
            return Task.FromResult(true);
        }

        if (_nextLootIndex >= itemCount)
        {
            bridge.Close();
            _nextLootIndex = 0;
            return Task.FromResult(true);
        }

        var action = policy.SelectAction(freeInventorySlots);
        switch (action)
        {
            case DutyLootAction.Pass:
                if (!bridge.PassItem(_nextLootIndex))
                {
                    return Task.FromResult(false);
                }

                _nextLootIndex++;
                return Task.FromResult(true);

            case DutyLootAction.Greed:
                if (!bridge.GreedItem(_nextLootIndex))
                {
                    ff14bot.Helpers.Logging.Write(
                        "[EZBuddy Duty] Greed was requested, but the loaded Need/Greed wrapper exposes no compatible public Greed operation. EZBuddy will not guess a SendAction payload or silently pass the item.");
                    return Task.FromResult(false);
                }

                _nextLootIndex++;
                return Task.FromResult(true);

            case DutyLootAction.LeaveUnchanged:
                ff14bot.Helpers.Logging.Write(
                    "[EZBuddy Duty] Automatic loot rolling is disabled; waiting for manual loot resolution before leaving the instance.");
                return Task.FromResult(false);

            default:
                return Task.FromResult(false);
        }
    }

    public Task<bool> RequestLeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!DutyManager.InInstance)
        {
            _leaveRequested = true;
            return Task.FromResult(true);
        }

        if (DirectorManager.ActiveDirector is not InstanceContentDirector director || !director.InstanceEnded)
        {
            return Task.FromResult(false);
        }

        if (!TryGetLootBridge(out var bridge))
        {
            return Task.FromResult(false);
        }

        if (bridge.IsOpen)
        {
            return Task.FromResult(false);
        }

        DutyManager.LeaveActiveDuty();
        _leaveRequested = true;
        return Task.FromResult(true);
    }

    private bool TryGetLootBridge(out LootBridge bridge)
    {
        if (!_lootBridgeResolved)
        {
            _lootBridgeResolved = true;
            _lootBridge = ResolveLootBridge();
        }

        bridge = _lootBridge!;
        return bridge is not null;
    }

    private static LootBridge? ResolveLootBridge()
    {
        foreach (var typeName in LootTypeNames)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly =>
                {
                    try
                    {
                        return assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .FirstOrDefault(candidate => candidate is not null);

            if (type is null)
            {
                continue;
            }

            var instance = type
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null);
            if (instance is null)
            {
                continue;
            }

            var isOpen = type.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Instance);
            var numberOfItems = type.GetProperty("NumberOfItems", BindingFlags.Public | BindingFlags.Instance);
            var close = type.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);
            var pass = type.GetMethod("PassItem", BindingFlags.Public | BindingFlags.Instance, binder: null, [typeof(int)], modifiers: null);
            var greed = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name.Contains("Greed", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
                });

            if (isOpen?.PropertyType != typeof(bool) ||
                numberOfItems?.PropertyType != typeof(int) ||
                close is null ||
                pass is null)
            {
                continue;
            }

            return new LootBridge(instance, isOpen, numberOfItems, close, pass, greed);
        }

        return null;
    }

    private sealed class LootBridge(
        object instance,
        PropertyInfo isOpenProperty,
        PropertyInfo numberOfItemsProperty,
        MethodInfo closeMethod,
        MethodInfo passMethod,
        MethodInfo? greedMethod)
    {
        public bool IsOpen => isOpenProperty.GetValue(instance) is true;

        public int NumberOfItems => numberOfItemsProperty.GetValue(instance) is int count ? count : 0;

        public void Close()
        {
            try
            {
                closeMethod.Invoke(instance, null);
            }
            catch
            {
                // The next host tick will re-check the window state.
            }
        }

        public bool PassItem(int index)
            => InvokeIndex(passMethod, index);

        public bool GreedItem(int index)
            => greedMethod is not null && InvokeIndex(greedMethod, index);

        private bool InvokeIndex(MethodInfo method, int index)
        {
            try
            {
                var result = method.Invoke(instance, [index]);
                return result is not bool boolean || boolean;
            }
            catch (TargetInvocationException exception)
            {
                ff14bot.Helpers.Logging.Write(
                    $"[EZBuddy Duty] Loot operation failed: {exception.InnerException?.Message ?? exception.Message}");
                return false;
            }
            catch (Exception exception)
            {
                ff14bot.Helpers.Logging.Write($"[EZBuddy Duty] Loot operation failed: {exception.Message}");
                return false;
            }
        }
    }
}

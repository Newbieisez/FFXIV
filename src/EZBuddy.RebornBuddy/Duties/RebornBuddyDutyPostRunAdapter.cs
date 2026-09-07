using System.Reflection;
using EZBuddy.Core.Duties;
using ff14bot.Directors;
using ff14bot.Managers;
using ff14bot.RemoteWindows;

namespace EZBuddy.RebornBuddy.Duties;

public sealed class RebornBuddyDutyPostRunAdapter : IDutyPostRunAdapter
{
    private int _nextLootIndex;
    private bool _leaveRequested;
    private MethodInfo? _greedMethod;
    private bool _greedMethodResolved;

    public Task<DutyPostRunStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var complete = DirectorManager.ActiveDirector is InstanceContentDirector director && director.InstanceEnded;
        var lootOpen = NeedGreed.Instance.IsOpen;
        var message = complete
            ? "Instance director reports completion."
            : "Instance director has not reported completion yet.";

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

        if (!NeedGreed.Instance.IsOpen)
        {
            _nextLootIndex = 0;
            return Task.FromResult(true);
        }

        var itemCount = NeedGreed.Instance.NumberOfItems;
        if (itemCount <= 0)
        {
            NeedGreed.Instance.Close();
            _nextLootIndex = 0;
            return Task.FromResult(true);
        }

        if (_nextLootIndex >= itemCount)
        {
            NeedGreed.Instance.Close();
            _nextLootIndex = 0;
            return Task.FromResult(true);
        }

        var action = policy.SelectAction(freeInventorySlots);
        switch (action)
        {
            case DutyLootAction.Pass:
                NeedGreed.Instance.PassItem(_nextLootIndex);
                _nextLootIndex++;
                return Task.FromResult(true);

            case DutyLootAction.Greed:
                if (!TryGreedItem(_nextLootIndex))
                {
                    ff14bot.Helpers.Logging.Write(
                        "[EZBuddy Duty] Greed was requested, but this RebornBuddy Need/Greed wrapper exposes no compatible public Greed method. EZBuddy will not guess a SendAction payload or silently pass the item.");
                    return Task.FromResult(false);
                }

                _nextLootIndex++;
                return Task.FromResult(true);

            case DutyLootAction.LeaveUnchanged:
                return Task.FromResult(true);

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

        if (NeedGreed.Instance.IsOpen)
        {
            return Task.FromResult(false);
        }

        DutyManager.LeaveActiveDuty();
        _leaveRequested = true;
        return Task.FromResult(true);
    }

    private bool TryGreedItem(int index)
    {
        if (!_greedMethodResolved)
        {
            _greedMethodResolved = true;
            _greedMethod = NeedGreed.Instance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name.Contains("Greed", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
                });
        }

        if (_greedMethod is null)
        {
            return false;
        }

        try
        {
            var result = _greedMethod.Invoke(NeedGreed.Instance, [index]);
            return result is not bool boolean || boolean;
        }
        catch (TargetInvocationException exception)
        {
            ff14bot.Helpers.Logging.Write(
                $"[EZBuddy Duty] Greed invocation failed: {exception.InnerException?.Message ?? exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Duty] Greed invocation failed: {exception.Message}");
            return false;
        }
    }
}

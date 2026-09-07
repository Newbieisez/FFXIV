using Buddy.Coroutines;
using EZBuddy.Core.Adapters;
using ff14bot.Enums;
using ff14bot.Managers;
using ff14bot.RemoteAgents;
using ff14bot.RemoteWindows;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class RebornBuddyDutySupportAdapter : IDutySupportAdapter
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EntryTimeout = TimeSpan.FromMinutes(3);

    public string Key => "rebornbuddy-duty-support";
    public string DisplayName => "RebornBuddy Duty Support / Trust";

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

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Ready,
            "Native Duty Support and Trust windows are available through RebornBuddy.",
            DateTimeOffset.UtcNow));
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

        if (request.DutyId == 0)
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
        if (!DawnStory.Instance.IsOpen)
        {
            AgentDawnStory.Instance.Toggle();
            if (!await WaitUntilAsync(() => DawnStory.Instance.IsOpen, WindowTimeout, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var selected = await DawnStory.Instance.SelectDuty((int)dutyId)
            .WaitAsync(WindowTimeout, cancellationToken)
            .ConfigureAwait(true);
        if (!selected)
        {
            return false;
        }

        DawnStory.Instance.Commence();
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

        if (Dawn.Instance.IsOpen && AgentDawn.Instance.TrustId != trustId.Value)
        {
            AgentDawn.Instance.Toggle();
            if (!await WaitUntilAsync(() => !Dawn.Instance.IsOpen, WindowTimeout, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        AgentDawn.Instance.TrustId = trustId.Value;
        if (!await WaitUntilAsync(() => AgentDawn.Instance.TrustId == trustId.Value, WindowTimeout, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        if (!Dawn.Instance.IsOpen)
        {
            AgentDawn.Instance.Toggle();
            if (!await WaitUntilAsync(() => Dawn.Instance.IsOpen, WindowTimeout, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Dawn.Instance.Register();
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

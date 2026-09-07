using EZBuddy.Core.Engine;
using EZBuddy.Core.Notifications;
using EZBuddy.Core.Runtime;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Runtime;

/// <summary>
/// Converts the last persisted queue checkpoint into a fail-closed recovery assessment for the
/// currently loaded RebornBuddy session. This reporter never requeues work automatically.
/// </summary>
public sealed class RebornBuddyRuntimeRecoveryReporter
{
    private static readonly IReadOnlySet<string> SafeIdempotentActivities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Smart Gear Auto-Equip"
        };

    private readonly IResumeCheckpointStore _checkpointStore;
    private readonly INotificationSink _notifications;

    public RebornBuddyRuntimeRecoveryReporter(
        IResumeCheckpointStore checkpointStore,
        INotificationSink notifications)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public async Task<ResumePlan> AssessAndReportAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checkpoint = await _checkpointStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var queueHasWork = EZBuddyRuntime.Queue.GetSnapshots().Any(snapshot => snapshot.State is
            ActivityState.Pending or
            ActivityState.Waiting or
            ActivityState.Running or
            ActivityState.GentleStopping or
            ActivityState.Blocked);

        var environment = new ResumeEnvironmentSnapshot(
            CharacterAvailable: ff14bot.Core.Player is not null && !ff14bot.Behavior.CommonBehaviors.IsLoading,
            InDuty: DutyManager.InInstance,
            QueueHasWork: queueHasWork,
            CurrentActivityName: checkpoint?.CurrentActivityKey,
            SafeIdempotentActivities: SafeIdempotentActivities,
            DestructiveActivities: null);
        var plan = ResumePlanner.Build(checkpoint, environment);

        if (plan.Disposition == ResumeDisposition.NothingToResume)
        {
            return plan;
        }

        ff14bot.Helpers.Logging.Write($"[EZBuddy Recovery] {plan.Message}");
        foreach (var warning in plan.Warnings)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Recovery] {warning}");
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["disposition"] = plan.Disposition.ToString(),
            ["completedActivities"] = plan.CompletedActivities.Count.ToString()
        };
        if (!string.IsNullOrWhiteSpace(plan.ActivityToRequeue))
        {
            fields["interruptedActivity"] = plan.ActivityToRequeue;
        }

        await _notifications.SendAsync(new EZNotification(
            plan.RequiresUserReview ? NotificationEventType.EngineError : NotificationEventType.Information,
            "EZBuddy Recovery Assessment",
            plan.Message,
            DateTimeOffset.UtcNow,
            fields,
            RequiresUserReview: true), cancellationToken).ConfigureAwait(false);

        return plan;
    }
}

namespace EZBuddy.Core.Safety;

public enum SessionSafetyAction
{
    None,
    RecommendBreak,
    RequestGentleStop,
    PauseForStuckReview,
    QueueComplete
}

public sealed record SessionSafetyPolicy(
    TimeSpan MaximumContinuousRuntime,
    TimeSpan BreakReminderAfter,
    TimeSpan StuckAfterNoProgress,
    bool GentleStopAtMaximumRuntime = true,
    bool PauseWhenStuck = true,
    bool NotifyWhenQueueComplete = true)
{
    public void Validate()
    {
        if (MaximumContinuousRuntime <= TimeSpan.Zero ||
            BreakReminderAfter <= TimeSpan.Zero ||
            StuckAfterNoProgress <= TimeSpan.Zero ||
            BreakReminderAfter > MaximumContinuousRuntime)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumContinuousRuntime));
        }
    }
}

public sealed record SessionSafetySnapshot(
    TimeSpan SessionRuntime,
    DateTimeOffset NowUtc,
    DateTimeOffset LastMeaningfulProgressUtc,
    bool QueueStarted,
    bool QueueHasPendingOrActiveWork,
    bool ActivityExpectedToProgress,
    bool CharacterMoving,
    string? CurrentActivityName = null);

public sealed record SessionSafetyDecision(
    SessionSafetyAction Action,
    string Message,
    bool RequiresUserReview = false);

public static class SessionSafetyEvaluator
{
    public static SessionSafetyDecision Evaluate(SessionSafetyPolicy policy, SessionSafetySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(snapshot);
        policy.Validate();

        if (snapshot.SessionRuntime < TimeSpan.Zero || snapshot.LastMeaningfulProgressUtc > snapshot.NowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        }

        if (policy.NotifyWhenQueueComplete && snapshot.QueueStarted && !snapshot.QueueHasPendingOrActiveWork)
        {
            return new SessionSafetyDecision(
                SessionSafetyAction.QueueComplete,
                "The EZBuddy queue is complete. No further automation work is scheduled.");
        }

        var noProgressFor = snapshot.NowUtc - snapshot.LastMeaningfulProgressUtc;
        if (policy.PauseWhenStuck &&
            snapshot.QueueHasPendingOrActiveWork &&
            snapshot.ActivityExpectedToProgress &&
            noProgressFor >= policy.StuckAfterNoProgress)
        {
            return new SessionSafetyDecision(
                SessionSafetyAction.PauseForStuckReview,
                $"No meaningful progress has been observed for {noProgressFor:g} while '{snapshot.CurrentActivityName ?? "the current activity"}' is expected to progress.",
                RequiresUserReview: true);
        }

        if (policy.GentleStopAtMaximumRuntime && snapshot.SessionRuntime >= policy.MaximumContinuousRuntime)
        {
            return new SessionSafetyDecision(
                SessionSafetyAction.RequestGentleStop,
                $"Configured maximum continuous runtime of {policy.MaximumContinuousRuntime:g} has been reached; request a safe-boundary stop.");
        }

        if (snapshot.SessionRuntime >= policy.BreakReminderAfter)
        {
            return new SessionSafetyDecision(
                SessionSafetyAction.RecommendBreak,
                $"Configured break reminder threshold of {policy.BreakReminderAfter:g} has been reached.");
        }

        return new SessionSafetyDecision(SessionSafetyAction.None, "Session safety thresholds are satisfied.");
    }
}

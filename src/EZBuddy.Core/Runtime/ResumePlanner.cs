namespace EZBuddy.Core.Runtime;

public enum ResumeDisposition
{
    NothingToResume,
    ResumeNextSafeActivity,
    RequeueFromStart,
    ManualReviewRequired,
    DiscardCheckpoint
}

public sealed record ResumeEnvironmentSnapshot(
    bool CharacterAvailable,
    bool InDuty,
    bool QueueHasWork,
    string? CurrentActivityName = null,
    IReadOnlySet<string>? SafeIdempotentActivities = null,
    IReadOnlySet<string>? DestructiveActivities = null)
{
    public IReadOnlySet<string> EffectiveSafeIdempotentActivities =>
        SafeIdempotentActivities ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> EffectiveDestructiveActivities =>
        DestructiveActivities ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record ResumePlan(
    ResumeDisposition Disposition,
    string Message,
    string? ActivityToRequeue,
    IReadOnlyList<string> CompletedActivities,
    IReadOnlyList<string> Warnings)
{
    public bool RequiresUserReview => Disposition == ResumeDisposition.ManualReviewRequired;
}

public static class ResumePlanner
{
    public static ResumePlan Build(ResumeCheckpoint? checkpoint, ResumeEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (checkpoint is null)
        {
            return new ResumePlan(
                ResumeDisposition.NothingToResume,
                "No runtime checkpoint exists.",
                null,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        var completed = checkpoint.CompletedRoutineKeys?.ToArray() ?? Array.Empty<string>();
        if (checkpoint.CleanShutdown)
        {
            return new ResumePlan(
                ResumeDisposition.NothingToResume,
                "The previous EZBuddy session ended cleanly; no crash recovery is needed.",
                null,
                completed,
                Array.Empty<string>());
        }

        if (!environment.CharacterAvailable)
        {
            return new ResumePlan(
                ResumeDisposition.ManualReviewRequired,
                "The previous session ended unexpectedly, but the character is not currently available for recovery validation.",
                checkpoint.CurrentActivityKey,
                completed,
                ["Wait until the character is fully loaded, then run recovery analysis again."]);
        }

        if (environment.InDuty)
        {
            return new ResumePlan(
                ResumeDisposition.ManualReviewRequired,
                "The character is currently inside an instance after an unclean EZBuddy session. Automatic resume is intentionally blocked.",
                checkpoint.CurrentActivityKey,
                completed,
                ["Verify duty state, route position, loot, and combat ownership before restarting automation."]);
        }

        var interrupted = checkpoint.CurrentActivityKey;
        if (string.IsNullOrWhiteSpace(interrupted))
        {
            return new ResumePlan(
                environment.QueueHasWork ? ResumeDisposition.ResumeNextSafeActivity : ResumeDisposition.NothingToResume,
                environment.QueueHasWork
                    ? "The previous session ended unexpectedly between activities; the remaining queue may resume from its next safe boundary."
                    : "The previous session ended unexpectedly, but no activity was in flight and no queued work remains.",
                null,
                completed,
                Array.Empty<string>());
        }

        if (environment.EffectiveDestructiveActivities.Contains(interrupted))
        {
            return new ResumePlan(
                ResumeDisposition.ManualReviewRequired,
                $"'{interrupted}' was interrupted and is classified as destructive. EZBuddy will not replay it automatically.",
                interrupted,
                completed,
                ["Re-scan inventory/currency state before explicitly approving the activity again."]);
        }

        if (environment.EffectiveSafeIdempotentActivities.Contains(interrupted))
        {
            return new ResumePlan(
                ResumeDisposition.RequeueFromStart,
                $"'{interrupted}' is explicitly classified as safe/idempotent and may be requeued from its first checkpoint.",
                interrupted,
                completed,
                ["The activity is restarted from the beginning; EZBuddy does not attempt to reconstruct an unknown internal coroutine frame."]);
        }

        return new ResumePlan(
            ResumeDisposition.ManualReviewRequired,
            $"'{interrupted}' was active during the unclean shutdown and has no explicit safe-resume classification.",
            interrupted,
            completed,
            ["Review current game state and either discard the checkpoint or manually requeue the activity from a known safe start."]);
    }
}

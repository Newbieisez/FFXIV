using EZBuddy.Core.Safety;

namespace EZBuddy.Core.Tests;

public sealed class SessionSafetyTests
{
    [Fact]
    public void StuckCondition_PausesBeforeRuntimeStop()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new SessionSafetyPolicy(
            MaximumContinuousRuntime: TimeSpan.FromHours(4),
            BreakReminderAfter: TimeSpan.FromHours(2),
            StuckAfterNoProgress: TimeSpan.FromMinutes(3));
        var snapshot = new SessionSafetySnapshot(
            SessionRuntime: TimeSpan.FromMinutes(30),
            NowUtc: now,
            LastMeaningfulProgressUtc: now.AddMinutes(-4),
            QueueStarted: true,
            QueueHasPendingOrActiveWork: true,
            ActivityExpectedToProgress: true,
            CharacterMoving: false,
            CurrentActivityName: "Retainer Venture Sweep");

        var decision = SessionSafetyEvaluator.Evaluate(policy, snapshot);

        Assert.Equal(SessionSafetyAction.PauseForStuckReview, decision.Action);
        Assert.True(decision.RequiresUserReview);
    }

    [Fact]
    public void MaximumRuntime_RequestsGentleStop()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new SessionSafetyPolicy(TimeSpan.FromHours(3), TimeSpan.FromHours(2), TimeSpan.FromMinutes(5));
        var snapshot = new SessionSafetySnapshot(
            TimeSpan.FromHours(3), now, now, true, true, false, false);

        var decision = SessionSafetyEvaluator.Evaluate(policy, snapshot);

        Assert.Equal(SessionSafetyAction.RequestGentleStop, decision.Action);
    }

    [Fact]
    public void EmptyStartedQueue_ReportsCompletion()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new SessionSafetyPolicy(TimeSpan.FromHours(3), TimeSpan.FromHours(2), TimeSpan.FromMinutes(5));
        var snapshot = new SessionSafetySnapshot(
            TimeSpan.FromMinutes(10), now, now, true, false, false, false);

        var decision = SessionSafetyEvaluator.Evaluate(policy, snapshot);

        Assert.Equal(SessionSafetyAction.QueueComplete, decision.Action);
    }
}

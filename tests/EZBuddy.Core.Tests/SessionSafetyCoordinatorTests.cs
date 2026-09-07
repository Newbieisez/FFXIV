using EZBuddy.Core.Engine;
using EZBuddy.Core.Notifications;
using EZBuddy.Core.Safety;

namespace EZBuddy.Core.Tests;

public sealed class SessionSafetyCoordinatorTests
{
    [Fact]
    public async Task ApplyAsync_RepeatedBreakReminder_IsDeduplicated()
    {
        var queue = new ActivityQueueEngine();
        var runLoop = new RunLoopController(queue);
        var notifications = new CapturingNotificationSink();
        var coordinator = new SessionSafetyCoordinator(runLoop, notifications);
        var decision = new SessionSafetyDecision(SessionSafetyAction.RecommendBreak, "Take a configured break.");

        var first = await coordinator.ApplyAsync(decision);
        var second = await coordinator.ApplyAsync(decision);

        Assert.True(first.ActionApplied);
        Assert.False(second.ActionApplied);
        Assert.Single(notifications.Items);
    }

    [Fact]
    public async Task ApplyAsync_StuckReview_PausesRunningLoop()
    {
        var queue = new ActivityQueueEngine();
        var runLoop = new RunLoopController(queue);
        var notifications = new CapturingNotificationSink();
        var coordinator = new SessionSafetyCoordinator(runLoop, notifications);

        await runLoop.StartAsync();
        await runLoop.ApplyPendingSignalsAsync();
        Assert.Equal(RunLoopState.Running, runLoop.State);

        var result = await coordinator.ApplyAsync(new SessionSafetyDecision(
            SessionSafetyAction.PauseForStuckReview,
            "No meaningful progress.",
            RequiresUserReview: true));
        await runLoop.ApplyPendingSignalsAsync();

        Assert.True(result.ActionApplied);
        Assert.Equal(RunLoopState.Paused, runLoop.State);
        Assert.True(Assert.Single(notifications.Items).RequiresUserReview);
    }

    [Fact]
    public async Task ApplyAsync_MaxRuntime_RequestsSafeStop()
    {
        var queue = new ActivityQueueEngine();
        var runLoop = new RunLoopController(queue);
        var notifications = new CapturingNotificationSink();
        var coordinator = new SessionSafetyCoordinator(runLoop, notifications);

        await runLoop.StartAsync();
        await runLoop.ApplyPendingSignalsAsync();

        var result = await coordinator.ApplyAsync(new SessionSafetyDecision(
            SessionSafetyAction.RequestGentleStop,
            "Configured runtime reached.",
            RequiresUserReview: true));
        await runLoop.ApplyPendingSignalsAsync();

        Assert.True(result.ActionApplied);
        Assert.Equal(RunLoopState.Idle, runLoop.State);
        Assert.True(Assert.Single(notifications.Items).RequiresUserReview);
    }

    [Fact]
    public async Task ApplyAsync_QueueComplete_UsesQueueCompleteNotification()
    {
        var queue = new ActivityQueueEngine();
        var runLoop = new RunLoopController(queue);
        var notifications = new CapturingNotificationSink();
        var coordinator = new SessionSafetyCoordinator(runLoop, notifications);

        var result = await coordinator.ApplyAsync(new SessionSafetyDecision(
            SessionSafetyAction.QueueComplete,
            "Queue is complete."));

        Assert.True(result.ActionApplied);
        Assert.Equal(NotificationEventType.QueueComplete, Assert.Single(notifications.Items).EventType);
    }

    [Fact]
    public async Task ApplyAsync_None_ResetsDeduplication()
    {
        var queue = new ActivityQueueEngine();
        var runLoop = new RunLoopController(queue);
        var notifications = new CapturingNotificationSink();
        var coordinator = new SessionSafetyCoordinator(runLoop, notifications);
        var reminder = new SessionSafetyDecision(SessionSafetyAction.RecommendBreak, "Take a break.");

        await coordinator.ApplyAsync(reminder);
        await coordinator.ApplyAsync(new SessionSafetyDecision(SessionSafetyAction.None, "Safe."));
        var repeatedAfterClear = await coordinator.ApplyAsync(reminder);

        Assert.True(repeatedAfterClear.ActionApplied);
        Assert.Equal(2, notifications.Items.Count);
    }

    private sealed class CapturingNotificationSink : INotificationSink
    {
        public string Key => "test";
        public List<EZNotification> Items { get; } = [];

        public Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(notification);
            return Task.CompletedTask;
        }
    }
}

using EZBuddy.Core.Engine;
using EZBuddy.Core.Notifications;
using EZBuddy.Core.Safety;

namespace EZBuddy.Core.Tests;

public sealed class SocialSafetyMonitorTests
{
    [Fact]
    public async Task DirectTell_QueuesPauseAndReviewNotification()
    {
        var token = TestContext.Current.CancellationToken;
        var queue = new ActivityQueueEngine();
        var runLoop = new RunLoopController(queue);
        await runLoop.StartAsync(token);
        await runLoop.ApplyPendingSignalsAsync(token);

        var sink = new CapturingSink();
        var source = new ScriptedSource([
            new SocialContactSignal("tell-1", SocialContactKind.DirectTell, "Player One", DateTimeOffset.UtcNow, "hello")
        ]);
        var monitor = new SocialSafetyMonitor(source, runLoop, sink);

        var result = await monitor.PollAsync(token);

        Assert.True(result.PauseRequested);
        Assert.Single(result.ReviewSignals);
        Assert.Equal(RunLoopState.Running, runLoop.State);
        await runLoop.ApplyPendingSignalsAsync(token);
        Assert.Equal(RunLoopState.Paused, runLoop.State);
        var notification = Assert.Single(sink.Notifications);
        Assert.Equal(NotificationEventType.SocialContact, notification.EventType);
        Assert.True(notification.RequiresUserReview);
    }

    [Fact]
    public async Task DuplicateSignal_IsProcessedOnlyOnce()
    {
        var token = TestContext.Current.CancellationToken;
        var runLoop = new RunLoopController(new ActivityQueueEngine());
        var signal = new SocialContactSignal("same-id", SocialContactKind.DirectTell, "Player", DateTimeOffset.UtcNow);
        var source = new ScriptedSource([signal], [signal]);
        var sink = new CapturingSink();
        var monitor = new SocialSafetyMonitor(source, runLoop, sink);

        var first = await monitor.PollAsync(token);
        var second = await monitor.PollAsync(token);

        Assert.Equal(1, first.NewSignals);
        Assert.Equal(0, second.NewSignals);
        Assert.Single(sink.Notifications);
    }

    [Fact]
    public async Task RepeatedTradeRequests_PauseOnlyAfterThresholdWithinWindow()
    {
        var token = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var runLoop = new RunLoopController(new ActivityQueueEngine());
        var source = new ScriptedSource([
            new SocialContactSignal("trade-1", SocialContactKind.TradeRequest, "Trader", now),
            new SocialContactSignal("trade-2", SocialContactKind.TradeRequest, "Trader", now.AddSeconds(10))
        ]);
        var sink = new CapturingSink();
        var monitor = new SocialSafetyMonitor(source, runLoop, sink);

        var result = await monitor.PollAsync(token);

        Assert.True(result.PauseRequested);
        Assert.Single(result.ReviewSignals);
        Assert.Equal("trade-2", result.ReviewSignals[0].Id);
        Assert.Equal(2, sink.Notifications.Count);
        Assert.False(sink.Notifications[0].RequiresUserReview);
        Assert.True(sink.Notifications[1].RequiresUserReview);
    }

    [Fact]
    public async Task GmCommunication_PausesEvenWithoutMessageBody()
    {
        var token = TestContext.Current.CancellationToken;
        var runLoop = new RunLoopController(new ActivityQueueEngine());
        var source = new ScriptedSource([
            new SocialContactSignal("gm-1", SocialContactKind.GmCommunication, "GM", DateTimeOffset.UtcNow)
        ]);
        var monitor = new SocialSafetyMonitor(source, runLoop);

        var result = await monitor.PollAsync(token);

        Assert.True(result.PauseRequested);
        Assert.Single(result.ReviewSignals);
    }

    private sealed class ScriptedSource : ISocialContactSource
    {
        private readonly Queue<IReadOnlyList<SocialContactSignal>> _batches;

        public ScriptedSource(params IReadOnlyList<SocialContactSignal>[] batches)
        {
            _batches = new Queue<IReadOnlyList<SocialContactSignal>>(batches);
        }

        public Task<IReadOnlyList<SocialContactSignal>> PollAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_batches.Count == 0 ? (IReadOnlyList<SocialContactSignal>)[] : _batches.Dequeue());
        }
    }

    private sealed class CapturingSink : INotificationSink
    {
        public string Key => "test";
        public List<EZNotification> Notifications { get; } = [];

        public Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}

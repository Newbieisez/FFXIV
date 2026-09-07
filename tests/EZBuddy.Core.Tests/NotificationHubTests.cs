using EZBuddy.Core.Notifications;

namespace EZBuddy.Core.Tests;

public sealed class NotificationHubTests
{
    [Fact]
    public async Task SendAsync_FansOutAndIsolatesProviderFailure()
    {
        var token = TestContext.Current.CancellationToken;
        var hub = new NotificationHub();
        var good = new CapturingSink("good");
        var failing = new ThrowingSink();
        hub.Register(failing);
        hub.Register(good);

        await hub.SendAsync(new EZNotification(
            NotificationEventType.Information,
            "Test",
            "Message",
            DateTimeOffset.UtcNow), token);

        Assert.Single(good.Items);
    }

    [Fact]
    public async Task Register_DuplicateSink_DoesNotDuplicateDelivery()
    {
        var token = TestContext.Current.CancellationToken;
        var hub = new NotificationHub();
        var sink = new CapturingSink("same");
        hub.Register(sink);
        hub.Register(sink);

        await hub.SendAsync(new EZNotification(
            NotificationEventType.QueueComplete,
            "Done",
            "Queue complete",
            DateTimeOffset.UtcNow), token);

        Assert.Single(sink.Items);
        Assert.Single(hub.Sinks);
    }

    [Fact]
    public async Task Unregister_StopsFutureDelivery()
    {
        var token = TestContext.Current.CancellationToken;
        var hub = new NotificationHub();
        var sink = new CapturingSink("removable");
        hub.Register(sink);
        Assert.True(hub.Unregister(sink));

        await hub.SendAsync(new EZNotification(
            NotificationEventType.Information,
            "Test",
            "Message",
            DateTimeOffset.UtcNow), token);

        Assert.Empty(sink.Items);
    }

    private sealed class CapturingSink(string key) : INotificationSink
    {
        public string Key { get; } = key;
        public List<EZNotification> Items { get; } = [];

        public Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : INotificationSink
    {
        public string Key => "throw";

        public Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Synthetic provider failure.");
    }
}

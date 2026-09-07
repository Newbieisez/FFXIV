using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Notifications;

public sealed class NotificationActivityTelemetrySink : IActivityTelemetrySink
{
    private readonly INotificationSink _notificationSink;

    public NotificationActivityTelemetrySink(INotificationSink notificationSink)
    {
        _notificationSink = notificationSink ?? throw new ArgumentNullException(nameof(notificationSink));
    }

    public async Task PublishAsync(ActivityRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var notification = snapshot.State switch
        {
            ActivityState.Failed => new EZNotification(
                NotificationEventType.EngineError,
                $"Activity failed: {snapshot.Name}",
                snapshot.LastMessage,
                DateTimeOffset.UtcNow,
                BuildFields(snapshot),
                RequiresUserReview: true),

            ActivityState.Blocked => new EZNotification(
                NotificationEventType.InventoryBlocked,
                $"Activity blocked: {snapshot.Name}",
                snapshot.LastMessage,
                DateTimeOffset.UtcNow,
                BuildFields(snapshot),
                RequiresUserReview: true),

            ActivityState.Completed => new EZNotification(
                NotificationEventType.Information,
                $"Activity complete: {snapshot.Name}",
                snapshot.LastMessage,
                DateTimeOffset.UtcNow,
                BuildFields(snapshot)),

            _ => null
        };

        if (notification is not null)
        {
            await _notificationSink.SendAsync(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildFields(ActivityRuntimeSnapshot snapshot)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Category"] = snapshot.Category.ToString(),
            ["State"] = snapshot.State.ToString(),
            ["Attempts"] = snapshot.Attempts.ToString(),
            ["Activity ID"] = snapshot.ActivityId.ToString("N")
        };
}

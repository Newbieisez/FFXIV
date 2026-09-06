namespace EZBuddy.Core.Notifications;

public enum NotificationEventType
{
    Information,
    RareDrop,
    RetainerComplete,
    LevelUp,
    DutyComplete,
    InventoryBlocked,
    SocialContact,
    UnexpectedRelocation,
    EngineError,
    QueueComplete
}

public sealed record EZNotification(
    NotificationEventType EventType,
    string Title,
    string Message,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, string>? Fields = null,
    bool RequiresUserReview = false);

public interface INotificationSink
{
    string Key { get; }
    Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default);
}

public sealed class CompositeNotificationSink : INotificationSink
{
    private readonly IReadOnlyList<INotificationSink> _sinks;

    public CompositeNotificationSink(IEnumerable<INotificationSink> sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks.Where(sink => sink is not null).ToArray();
    }

    public string Key => "composite";

    public async Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        foreach (var sink in _sinks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await sink.SendAsync(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // One optional notification provider must not block the automation engine or leak provider secrets.
            }
        }
    }
}

public sealed class NullNotificationSink : INotificationSink
{
    public static NullNotificationSink Instance { get; } = new();
    private NullNotificationSink() { }

    public string Key => "none";
    public Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

namespace EZBuddy.Core.Notifications;

/// <summary>
/// Thread-safe fan-out for optional notification providers. Provider failures are isolated so a
/// webhook/push integration can never crash or block the automation engine.
/// </summary>
public sealed class NotificationHub : INotificationSink
{
    private readonly object _sync = new();
    private readonly List<INotificationSink> _sinks = [];

    public string Key => "hub";

    public IReadOnlyList<INotificationSink> Sinks
    {
        get
        {
            lock (_sync)
            {
                return _sinks.ToArray();
            }
        }
    }

    public void Register(INotificationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_sync)
        {
            if (!_sinks.Contains(sink))
            {
                _sinks.Add(sink);
            }
        }
    }

    public bool Unregister(INotificationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_sync)
        {
            return _sinks.Remove(sink);
        }
    }

    public async Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        foreach (var sink in Sinks)
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
                // Optional providers are isolated from the automation engine and each other.
            }
        }
    }
}

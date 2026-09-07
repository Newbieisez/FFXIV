namespace EZBuddy.Core.Engine;

public sealed class ActivityTelemetryHub : IActivityTelemetrySink
{
    private readonly object _sync = new();
    private readonly List<IActivityTelemetrySink> _sinks = new();

    public IReadOnlyList<IActivityTelemetrySink> Sinks
    {
        get
        {
            lock (_sync)
            {
                return _sinks.ToArray();
            }
        }
    }

    public void Register(IActivityTelemetrySink sink)
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

    public bool Unregister(IActivityTelemetrySink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_sync)
        {
            return _sinks.Remove(sink);
        }
    }

    public async Task PublishAsync(ActivityRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var sink in Sinks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await sink.PublishAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Optional telemetry must never crash the activity engine.
            }
        }
    }
}

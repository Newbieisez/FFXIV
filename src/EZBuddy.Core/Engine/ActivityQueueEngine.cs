using System.Collections.Concurrent;

namespace EZBuddy.Core.Engine;

public sealed class ActivityQueueEngine
{
    private readonly object _sync = new();
    private readonly LinkedList<QueuedActivity> _queue = new();
    private readonly ConcurrentDictionary<Guid, ActivityRuntimeSnapshot> _snapshots = new();
    private readonly IActivityTelemetrySink _telemetry;
    private CancellationTokenSource _engineCts = new();
    private QueuedActivity? _current;
    private bool _gentleStopRequested;

    public ActivityQueueEngine(IActivityTelemetrySink? telemetry = null)
    {
        _telemetry = telemetry ?? NullActivityTelemetrySink.Instance;
    }

    public bool IsEmergencyStopped { get; private set; }
    public bool IsGentleStopRequested => _gentleStopRequested;
    public Guid? CurrentActivityId => _current?.Item.Activity.Id;

    public IReadOnlyList<ActivityRuntimeSnapshot> GetSnapshots()
        => _snapshots.Values.OrderBy(x => x.EnqueuedAt).ToArray();

    public void Enqueue(ActivityQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Activity);

        var queued = new QueuedActivity(item, DateTimeOffset.UtcNow);
        lock (_sync)
        {
            var node = _queue.First;
            while (node is not null && node.Value.Item.Priority >= item.Priority)
            {
                node = node.Next;
            }

            if (node is null)
            {
                _queue.AddLast(queued);
            }
            else
            {
                _queue.AddBefore(node, queued);
            }
        }

        _snapshots[item.Activity.Id] = queued.ToSnapshot(ActivityState.Pending, "Queued.");
    }

    public bool Remove(Guid activityId)
    {
        lock (_sync)
        {
            var node = _queue.First;
            while (node is not null)
            {
                if (node.Value.Item.Activity.Id == activityId)
                {
                    _queue.Remove(node);
                    _snapshots[activityId] = node.Value.ToSnapshot(ActivityState.Cancelled, "Removed from queue.");
                    return true;
                }

                node = node.Next;
            }
        }

        return false;
    }

    public void RequestGentleStop() => _gentleStopRequested = true;

    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        IsEmergencyStopped = true;
        _gentleStopRequested = false;
        _engineCts.Cancel();

        var current = _current;
        if (current is not null)
        {
            try
            {
                await current.Item.Activity.OnHaltAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Emergency shutdown must continue even if a module's halt hook fails.
            }

            await SetStateAsync(current, ActivityState.Cancelled, "Emergency stop.", cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            foreach (var queued in _queue)
            {
                _snapshots[queued.Item.Activity.Id] = queued.ToSnapshot(ActivityState.Cancelled, "Cancelled by emergency stop.");
            }
            _queue.Clear();
        }
    }

    public void ResetEmergencyStop()
    {
        if (!IsEmergencyStopped)
        {
            return;
        }

        _engineCts.Dispose();
        _engineCts = new CancellationTokenSource();
        IsEmergencyStopped = false;
    }

    public async Task<ExecutionResult> TickAsync(CancellationToken cancellationToken = default)
    {
        if (IsEmergencyStopped)
        {
            return ExecutionResult.Block("Activity engine is emergency-stopped.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _engineCts.Token);
        var token = linked.Token;

        var current = GetOrDequeueCurrent();
        if (current is null)
        {
            return ExecutionResult.Yield("Activity queue is empty.");
        }

        if (_gentleStopRequested)
        {
            await SetStateAsync(current, ActivityState.GentleStopping, "Gentle stop requested; halting at safe boundary.", token).ConfigureAwait(false);
            await SafeHaltAsync(current, token).ConfigureAwait(false);
            await SetStateAsync(current, ActivityState.Cancelled, "Stopped gently.", token).ConfigureAwait(false);
            _current = null;
            _gentleStopRequested = false;
            return ExecutionResult.Complete("Gentle stop complete.");
        }

        if (current.Item.Activity.IsComplete)
        {
            await CompleteCurrentAsync(current, "Activity reported complete.", token).ConfigureAwait(false);
            return ExecutionResult.Complete("Activity complete.");
        }

        try
        {
            if (!await current.Item.Activity.CanExecuteAsync(token).ConfigureAwait(false))
            {
                await SetStateAsync(current, ActivityState.Waiting, "Preconditions are not currently satisfied.", token).ConfigureAwait(false);
                return ExecutionResult.Yield("Waiting for activity preconditions.");
            }

            if (current.StartedAt is null)
            {
                current.StartedAt = DateTimeOffset.UtcNow;
            }

            await SetStateAsync(current, ActivityState.Running, "Executing.", token).ConfigureAwait(false);
            var result = await current.Item.Activity.ExecuteStepAsync(token).ConfigureAwait(false);

            switch (result.Disposition)
            {
                case ExecutionDisposition.Complete:
                    await CompleteCurrentAsync(current, result.Message, token).ConfigureAwait(false);
                    break;

                case ExecutionDisposition.Fail:
                    await HandleFailureAsync(current, result, token).ConfigureAwait(false);
                    break;

                case ExecutionDisposition.Block:
                    await SetStateAsync(current, ActivityState.Blocked, result.Message, token).ConfigureAwait(false);
                    break;

                case ExecutionDisposition.Retry:
                    current.Attempts++;
                    if (current.Attempts > current.Item.MaxRetries)
                    {
                        await HandleFailureAsync(current, ExecutionResult.Fail($"Retry limit exceeded. Last message: {result.Message}"), token).ConfigureAwait(false);
                    }
                    else
                    {
                        await SetStateAsync(current, ActivityState.Waiting, result.Message, token).ConfigureAwait(false);
                    }
                    break;

                default:
                    await SetStateAsync(current, ActivityState.Running, result.Message, token).ConfigureAwait(false);
                    break;
            }

            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await SafeHaltAsync(current, CancellationToken.None).ConfigureAwait(false);
            await SetStateAsync(current, ActivityState.Cancelled, "Execution cancelled.", CancellationToken.None).ConfigureAwait(false);
            _current = null;
            return ExecutionResult.Fail("Execution cancelled.");
        }
        catch (Exception exception)
        {
            var result = ExecutionResult.Fail("Unhandled activity exception.", exception);
            await HandleFailureAsync(current, result, token).ConfigureAwait(false);
            return result;
        }
    }

    private QueuedActivity? GetOrDequeueCurrent()
    {
        if (_current is not null)
        {
            return _current;
        }

        lock (_sync)
        {
            if (_queue.First is null)
            {
                return null;
            }

            _current = _queue.First.Value;
            _queue.RemoveFirst();
            return _current;
        }
    }

    private async Task CompleteCurrentAsync(QueuedActivity current, string message, CancellationToken cancellationToken)
    {
        current.CompletedAt = DateTimeOffset.UtcNow;
        await SetStateAsync(current, ActivityState.Completed, message, cancellationToken).ConfigureAwait(false);
        _current = null;
    }

    private async Task HandleFailureAsync(QueuedActivity current, ExecutionResult result, CancellationToken cancellationToken)
    {
        current.CompletedAt = DateTimeOffset.UtcNow;
        await SetStateAsync(current, ActivityState.Failed, result.Message, cancellationToken).ConfigureAwait(false);
        await SafeHaltAsync(current, cancellationToken).ConfigureAwait(false);

        if (current.Item.ContinueOnFailure)
        {
            _current = null;
            return;
        }

        _gentleStopRequested = true;
    }

    private async Task SafeHaltAsync(QueuedActivity current, CancellationToken cancellationToken)
    {
        try
        {
            await current.Item.Activity.OnHaltAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Halt failures are reported by the module's own diagnostics; never crash the engine.
        }
    }

    private async Task SetStateAsync(QueuedActivity current, ActivityState state, string message, CancellationToken cancellationToken)
    {
        var snapshot = current.ToSnapshot(state, message);
        _snapshots[current.Item.Activity.Id] = snapshot;
        await _telemetry.PublishAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private sealed class QueuedActivity
    {
        public QueuedActivity(ActivityQueueItem item, DateTimeOffset enqueuedAt)
        {
            Item = item;
            EnqueuedAt = enqueuedAt;
        }

        public ActivityQueueItem Item { get; }
        public DateTimeOffset EnqueuedAt { get; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public int Attempts { get; set; }

        public ActivityRuntimeSnapshot ToSnapshot(ActivityState state, string message)
            => new(
                Item.Activity.Id,
                Item.Activity.Name,
                Item.Activity.Category,
                state,
                Attempts,
                EnqueuedAt,
                StartedAt,
                CompletedAt,
                message);
    }
}

using System.Collections.Concurrent;

namespace EZBuddy.Core.Engine;

public enum RunLoopState
{
    Idle,
    Starting,
    Running,
    Pausing,
    Paused,
    Stopping
}

public sealed record RunLoopStatus(
    RunLoopState State,
    string Message,
    DateTimeOffset Timestamp);

public interface IRunLoopController
{
    RunLoopState State { get; }
    RunLoopStatus Status { get; }
    event EventHandler<RunLoopStatus>? StatusChanged;
    event EventHandler<string>? StatusReported;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<ExecutionResult> TickAsync(CancellationToken cancellationToken = default);

    Task ApplyPendingSignalsAsync(CancellationToken cancellationToken = default);
    void ObserveEngineState();
}

public sealed class RunLoopController : IRunLoopController
{
    private enum RunLoopSignal
    {
        Start,
        Pause,
        Resume,
        Stop
    }

    private readonly ActivityQueueEngine _queue;
    private readonly ConcurrentQueue<RunLoopSignal> _signals = new();
    private readonly object _stateSync = new();
    private RunLoopStatus _status = new(
        RunLoopState.Idle,
        "Run loop is idle.",
        DateTimeOffset.UtcNow);

    public RunLoopController(ActivityQueueEngine queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public RunLoopState State
    {
        get
        {
            lock (_stateSync)
            {
                return _status.State;
            }
        }
    }

    public RunLoopStatus Status
    {
        get
        {
            lock (_stateSync)
            {
                return _status;
            }
        }
    }

    public event EventHandler<RunLoopStatus>? StatusChanged;
    public event EventHandler<string>? StatusReported;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => QueueSignalAsync(RunLoopSignal.Start, cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => QueueSignalAsync(RunLoopSignal.Pause, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default)
        => QueueSignalAsync(RunLoopSignal.Resume, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => QueueSignalAsync(RunLoopSignal.Stop, cancellationToken);

    public async Task<ExecutionResult> TickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyPendingSignalsAsync(cancellationToken).ConfigureAwait(true);

        if (!_queue.IsRunning)
        {
            ObserveEngineState();
            return ExecutionResult.Yield(Status.Message);
        }

        var result = await _queue.TickAsync(cancellationToken).ConfigureAwait(true);
        ObserveEngineState();
        return result;
    }

    public async Task ApplyPendingSignalsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (_signals.TryDequeue(out var signal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (signal)
            {
                case RunLoopSignal.Start:
                    if (State is RunLoopState.Running or RunLoopState.Starting)
                    {
                        break;
                    }

                    SetStatus(RunLoopState.Starting, "Starting EZBuddy run loop...");
                    _queue.Start();
                    SetStatus(RunLoopState.Running, "EZBuddy run loop started.");
                    break;

                case RunLoopSignal.Pause:
                    if (State != RunLoopState.Running)
                    {
                        break;
                    }

                    SetStatus(RunLoopState.Pausing, "Pausing at the next activity-step boundary...");
                    _queue.Pause();
                    SetStatus(RunLoopState.Paused, "EZBuddy run loop paused.");
                    break;

                case RunLoopSignal.Resume:
                    if (State != RunLoopState.Paused)
                    {
                        break;
                    }

                    _queue.Resume();
                    SetStatus(RunLoopState.Running, "EZBuddy run loop resumed.");
                    break;

                case RunLoopSignal.Stop:
                    if (State is RunLoopState.Idle or RunLoopState.Stopping)
                    {
                        break;
                    }

                    SetStatus(RunLoopState.Stopping, "Gentle stop requested; current activity will halt at the next safe boundary.");

                    if (_queue.CurrentActivity is null && !HasPendingWork())
                    {
                        await _queue.StopAsync(
                            preservePendingQueue: true,
                            cancellationToken: cancellationToken).ConfigureAwait(true);
                        SetStatus(RunLoopState.Idle, "EZBuddy run loop stopped.");
                    }
                    else
                    {
                        _queue.RequestGentleStop();
                    }

                    break;
            }
        }

        ObserveEngineState();
    }

    public void ObserveEngineState()
    {
        var state = State;

        if (state == RunLoopState.Stopping &&
            _queue.IsPaused &&
            !_queue.IsGentleStopRequested)
        {
            SetStatus(RunLoopState.Idle, "EZBuddy run loop stopped at a safe activity boundary.");
            return;
        }

        if (state == RunLoopState.Running && _queue.IsPaused)
        {
            SetStatus(RunLoopState.Paused, "EZBuddy queue paused by a runtime guard or activity condition.");
        }
    }

    public void NotifyHostStopRequested()
        => SetStatus(RunLoopState.Stopping, "RebornBuddy stop requested; active activity cancellation is propagating.");

    public void NotifyHostStopped()
        => SetStatus(RunLoopState.Idle, "RebornBuddy stopped EZBuddy; pending queue state was preserved.");

    private Task QueueSignalAsync(RunLoopSignal signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _signals.Enqueue(signal);
        return Task.CompletedTask;
    }

    private bool HasPendingWork()
        => _queue.GetSnapshots().Any(snapshot => snapshot.State is
            ActivityState.Pending or
            ActivityState.Waiting or
            ActivityState.Running or
            ActivityState.GentleStopping);

    private void SetStatus(RunLoopState state, string message)
    {
        RunLoopStatus next;
        lock (_stateSync)
        {
            if (_status.State == state && string.Equals(_status.Message, message, StringComparison.Ordinal))
            {
                return;
            }

            next = new RunLoopStatus(state, message, DateTimeOffset.UtcNow);
            _status = next;
        }

        StatusChanged?.Invoke(this, next);
        StatusReported?.Invoke(this, message);
    }
}

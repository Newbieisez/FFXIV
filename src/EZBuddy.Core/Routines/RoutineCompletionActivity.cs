using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

/// <summary>
/// Decorates a routine activity and persists completion only after the wrapped activity reports
/// successful completion. Failed, blocked, retrying, cancelled, or halted activities are never
/// recorded as completed.
/// </summary>
public sealed class RoutineCompletionActivity : IEZActivity
{
    private readonly IEZActivity _inner;
    private readonly IRoutineCompletionStore _store;
    private readonly string _routineKey;
    private readonly Func<DateTimeOffset> _utcNow;
    private bool _completionRecorded;

    public RoutineCompletionActivity(
        string routineKey,
        IEZActivity inner,
        IRoutineCompletionStore store,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routineKey);
        _routineKey = routineKey.Trim();
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Guid Id => _inner.Id;
    public string Name => _inner.Name;
    public ActivityCategory Category => _inner.Category;
    public bool IsComplete => _completionRecorded;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
        => _completionRecorded
            ? Task.FromResult(false)
            : _inner.CanExecuteAsync(cancellationToken);

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (_completionRecorded)
        {
            return ExecutionResult.Complete($"{Name} completion was already recorded.");
        }

        if (_inner.IsComplete)
        {
            await RecordCompletionAsync("Completed", cancellationToken).ConfigureAwait(false);
            return ExecutionResult.Complete($"{Name} completed and routine history was recorded.");
        }

        var result = await _inner.ExecuteStepAsync(cancellationToken).ConfigureAwait(false);
        if (result.Disposition == ExecutionDisposition.Complete || _inner.IsComplete)
        {
            await RecordCompletionAsync("Completed", cancellationToken).ConfigureAwait(false);
            return result.Disposition == ExecutionDisposition.Complete
                ? result
                : ExecutionResult.Complete(result.Message);
        }

        return result;
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
        => _inner.OnHaltAsync(cancellationToken);

    private async Task RecordCompletionAsync(string outcome, CancellationToken cancellationToken)
    {
        if (_completionRecorded)
        {
            return;
        }

        await _store.RecordAsync(
            new RoutineCompletion(_routineKey, _utcNow().ToUniversalTime(), outcome),
            cancellationToken).ConfigureAwait(false);
        _completionRecorded = true;
    }
}

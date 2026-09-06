namespace EZBuddy.Core.Engine;

public enum ActivityCategory
{
    Core,
    Duty,
    Questing,
    Gathering,
    Crafting,
    Retainers,
    Marketboard,
    Relics,
    Events,
    Sanctuary,
    GoldSaucer,
    TripleTriad,
    DailyWeekly,
    Utility
}

public enum ActivityState
{
    Pending,
    Waiting,
    Running,
    GentleStopping,
    Completed,
    Failed,
    Cancelled,
    Blocked
}

public enum ExecutionDisposition
{
    Continue,
    Yield,
    Complete,
    Retry,
    Block,
    Fail
}

public sealed record ExecutionResult(
    ExecutionDisposition Disposition,
    string Message,
    TimeSpan? RetryAfter = null,
    Exception? Exception = null)
{
    public static ExecutionResult Continue(string message = "Continue") => new(ExecutionDisposition.Continue, message);
    public static ExecutionResult Yield(string message = "Yield") => new(ExecutionDisposition.Yield, message);
    public static ExecutionResult Complete(string message = "Complete") => new(ExecutionDisposition.Complete, message);
    public static ExecutionResult Retry(string message, TimeSpan? retryAfter = null) => new(ExecutionDisposition.Retry, message, retryAfter);
    public static ExecutionResult Block(string message) => new(ExecutionDisposition.Block, message);
    public static ExecutionResult Fail(string message, Exception? exception = null) => new(ExecutionDisposition.Fail, message, null, exception);
}

public interface IEZActivity
{
    Guid Id { get; }
    string Name { get; }
    ActivityCategory Category { get; }
    bool IsComplete { get; }
    Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default);
    Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default);
    Task OnHaltAsync(CancellationToken cancellationToken = default);
}

public sealed record ActivityQueueItem(
    IEZActivity Activity,
    int Priority = 0,
    int MaxRetries = 3,
    bool ContinueOnFailure = false,
    string? StopConditionLabel = null);

public sealed record ActivityRuntimeSnapshot(
    Guid ActivityId,
    string Name,
    ActivityCategory Category,
    ActivityState State,
    int Attempts,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string LastMessage);

public interface IActivityTelemetrySink
{
    Task PublishAsync(ActivityRuntimeSnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed class NullActivityTelemetrySink : IActivityTelemetrySink
{
    public static NullActivityTelemetrySink Instance { get; } = new();
    private NullActivityTelemetrySink() { }
    public Task PublishAsync(ActivityRuntimeSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

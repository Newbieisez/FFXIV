using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

public sealed record RoutineQueueResult(
    IReadOnlyList<string> QueuedRoutineKeys,
    IReadOnlyList<string> SkippedRoutineKeys,
    IReadOnlyList<string> ManualRoutineKeys,
    IReadOnlyList<string> Messages);

public interface IRoutineActivityFactory
{
    string RoutineKey { get; }
    Task<IEZActivity?> CreateAsync(RoutineDefinition routine, CancellationToken cancellationToken = default);
}

public sealed class RoutineQueueOrchestrator
{
    private readonly IRoutinePlanner _planner;
    private readonly ActivityQueueEngine _queue;
    private readonly IReadOnlyDictionary<string, IRoutineActivityFactory> _factories;
    private readonly IRoutineCompletionStore? _completionStore;

    public RoutineQueueOrchestrator(
        IRoutinePlanner planner,
        ActivityQueueEngine queue,
        IEnumerable<IRoutineActivityFactory>? factories = null,
        IRoutineCompletionStore? completionStore = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _completionStore = completionStore;
        _factories = (factories ?? Array.Empty<IRoutineActivityFactory>())
            .GroupBy(factory => factory.RoutineKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RoutineQueueResult> QueueDueAsync(
        IEnumerable<RoutineDefinition> routines,
        DateTimeOffset nowUtc,
        IReadOnlySet<string>? enabledRoutineKeys = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routines);
        var routineList = routines.ToArray();
        var dueStates = await _planner.GetDueAsync(routineList, nowUtc, cancellationToken);

        var queued = new List<string>();
        var skipped = new List<string>();
        var manual = new List<string>();
        var messages = new List<string>();

        foreach (var dueState in dueStates.Where(state => state.IsDue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var routine = dueState.Routine;

            var enabled = enabledRoutineKeys?.Contains(routine.Key) ?? routine.EnabledByDefault;
            if (!enabled)
            {
                skipped.Add(routine.Key);
                messages.Add($"{routine.DisplayName}: due but disabled.");
                continue;
            }

            if (routine.ExecutionKind == RoutineExecutionKind.ManualReview)
            {
                manual.Add(routine.Key);
                messages.Add($"{routine.DisplayName}: due and requires manual review.");
                continue;
            }

            if (!_factories.TryGetValue(routine.Key, out var factory))
            {
                skipped.Add(routine.Key);
                messages.Add($"{routine.DisplayName}: due, but no compatible runtime executor is registered.");
                continue;
            }

            var activity = await factory.CreateAsync(routine, cancellationToken);
            if (activity is null)
            {
                skipped.Add(routine.Key);
                messages.Add($"{routine.DisplayName}: executor declined creation for the current state.");
                continue;
            }

            IEZActivity queuedActivity = _completionStore is null
                ? activity
                : new RoutineCompletionActivity(routine.Key, activity, _completionStore);

            _queue.Enqueue(new ActivityQueueItem(
                queuedActivity,
                Priority: routine.Priority,
                MaxRetries: 3,
                ContinueOnFailure: true,
                StopConditionLabel: routine.ResetRule.Cadence.ToString()));

            queued.Add(routine.Key);
            messages.Add($"{routine.DisplayName}: queued.");
        }

        return new RoutineQueueResult(queued, skipped, manual, messages);
    }
}

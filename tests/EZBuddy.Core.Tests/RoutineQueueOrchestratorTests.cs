using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class RoutineQueueOrchestratorTests
{
    [Fact]
    public async Task DueEnabledRoutineWithFactoryIsQueued()
    {
        var store = new InMemoryRoutineCompletionStore();
        var planner = new ResetAwareRoutinePlanner(store);
        var queue = new ActivityQueueEngine();
        var routine = CreateRoutine("daily-test");
        var factory = new TestFactory(routine.Key);
        var orchestrator = new RoutineQueueOrchestrator(planner, queue, [factory]);

        var result = await orchestrator.QueueDueAsync(
            [routine],
            new DateTimeOffset(2026, 9, 6, 18, 0, 0, TimeSpan.Zero),
            new HashSet<string>([routine.Key], StringComparer.OrdinalIgnoreCase),
            TestContext.Current.CancellationToken);

        Assert.Contains(routine.Key, result.QueuedRoutineKeys);
        Assert.Single(queue.GetSnapshots());
        Assert.Equal(ActivityState.Pending, queue.GetSnapshots()[0].State);
    }

    [Fact]
    public async Task MissingExecutorStaysVisibleAndIsNotQueued()
    {
        var store = new InMemoryRoutineCompletionStore();
        var planner = new ResetAwareRoutinePlanner(store);
        var queue = new ActivityQueueEngine();
        var routine = CreateRoutine("unsupported-test");
        var orchestrator = new RoutineQueueOrchestrator(planner, queue);

        var result = await orchestrator.QueueDueAsync(
            [routine],
            new DateTimeOffset(2026, 9, 6, 18, 0, 0, TimeSpan.Zero),
            new HashSet<string>([routine.Key], StringComparer.OrdinalIgnoreCase),
            TestContext.Current.CancellationToken);

        Assert.Contains(routine.Key, result.SkippedRoutineKeys);
        Assert.Empty(queue.GetSnapshots());
        Assert.Contains(result.Messages, message => message.Contains("no compatible runtime executor", StringComparison.OrdinalIgnoreCase));
    }

    private static RoutineDefinition CreateRoutine(string key)
        => new(
            key,
            key,
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Daily, new TimeOnly(15, 0)),
            RoutineExecutionKind.Activity,
            Priority: 50);

    private sealed class TestFactory(string routineKey) : IRoutineActivityFactory
    {
        public string RoutineKey { get; } = routineKey;

        public Task<IEZActivity?> CreateAsync(RoutineDefinition routine, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IEZActivity?>(new TestActivity(routine.DisplayName));
        }
    }

    private sealed class TestActivity(string name) : IEZActivity
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; } = name;
        public ActivityCategory Category => ActivityCategory.DailyWeekly;
        public bool IsComplete { get; private set; }

        public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
        {
            IsComplete = true;
            return Task.FromResult(ExecutionResult.Complete());
        }

        public Task OnHaltAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

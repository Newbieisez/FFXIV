using EZBuddy.Core.Bundles;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class RoutinePriorityBundleTests
{
    [Fact]
    public void BeforeProgressionRoutinesUseCatalogPriorityInsteadOfInsertionOrder()
    {
        var queue = new ActivityQueueEngine();
        var planner = new FirstPlayableBundlePlanner(queue, new FakeFactory());
        var store = new NoOpRoutineCompletionStore();

        var expert = new RoutineCompletionActivity(
            "gc-expert-delivery",
            new FakeActivity("Expert Delivery", ActivityCategory.DailyWeekly),
            store);
        var venture = new RoutineCompletionActivity(
            "retainer-venture-refill",
            new FakeActivity("Venture Pressure", ActivityCategory.Retainers),
            store);

        var plan = planner.Enqueue(new FirstPlayableBundleOptions(
            RunMaintenance: false,
            RunRetainerSweep: false,
            RunInventoryPressureRelief: false,
            RunDailyProgression: false,
            RunDutyLoop: false,
            ReturnToIdle: false,
            BeforeProgressionActivities: [expert, venture]));

        Assert.Equal(["Venture Pressure", "Expert Delivery"], plan.StageNames);
    }

    private sealed class NoOpRoutineCompletionStore : IRoutineCompletionStore
    {
        public Task<RoutineCompletion?> GetLatestAsync(string routineKey, CancellationToken cancellationToken = default)
            => Task.FromResult<RoutineCompletion?>(null);

        public Task RecordAsync(RoutineCompletion completion, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeFactory : IFirstPlayableActivityFactory
    {
        public IEZActivity CreateMaintenanceActivity() => new FakeActivity("Maintenance", ActivityCategory.Utility);
        public IEZActivity CreateRetainerSweepActivity() => new FakeActivity("Retainers", ActivityCategory.Retainers);
        public IEZActivity CreateInventoryPressureReliefActivity() => new FakeActivity("Inventory", ActivityCategory.Utility);
        public IEZActivity CreateDailyProgressionActivity() => new FakeActivity("Daily", ActivityCategory.DailyWeekly);
        public IEZActivity CreateDutyLoopActivity() => new FakeActivity("Duty", ActivityCategory.Duty);
    }

    private sealed class FakeActivity(string name, ActivityCategory category) : IEZActivity
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; } = name;
        public ActivityCategory Category { get; } = category;
        public bool IsComplete { get; private set; }

        public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
        {
            IsComplete = true;
            return Task.FromResult(ExecutionResult.Complete());
        }

        public Task OnHaltAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

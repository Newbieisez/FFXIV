using EZBuddy.Core.Bundles;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class FirstPlayableBundleTests
{
    [Fact]
    public void Enqueue_DefaultBundle_UsesSafeStageOrderAndReturnsToIdle()
    {
        var queue = new ActivityQueueEngine();
        var planner = new FirstPlayableBundlePlanner(queue, new FakeFactory());

        var plan = planner.Enqueue();

        Assert.Equal(
            [
                "Maintenance",
                "Retainers",
                "Inventory Relief",
                "Daily Progression",
                "Duty Loop",
                "Return to Idle"
            ],
            plan.StageNames);

        var snapshots = queue.GetSnapshots().OrderBy(snapshot => snapshot.EnqueuedAt).ToArray();
        Assert.Equal(6, snapshots.Length);
        Assert.All(snapshots, snapshot => Assert.Equal(ActivityState.Pending, snapshot.State));
    }

    [Fact]
    public void Enqueue_CanDisableOptionalStagesWithoutCreatingHiddenWork()
    {
        var queue = new ActivityQueueEngine();
        var planner = new FirstPlayableBundlePlanner(queue, new FakeFactory());

        var plan = planner.Enqueue(new FirstPlayableBundleOptions(
            RunMaintenance: false,
            RunRetainerSweep: true,
            RunInventoryPressureRelief: true,
            RunDailyProgression: false,
            RunDutyLoop: true,
            ReturnToIdle: true));

        Assert.Equal(
            ["Retainers", "Inventory Relief", "Duty Loop", "Return to Idle"],
            plan.StageNames);
        Assert.Equal(4, queue.GetSnapshots().Count);
    }

    private sealed class FakeFactory : IFirstPlayableActivityFactory
    {
        public IEZActivity CreateMaintenanceActivity() => new FakeActivity("Maintenance", ActivityCategory.Utility);
        public IEZActivity CreateRetainerSweepActivity() => new FakeActivity("Retainers", ActivityCategory.Retainers);
        public IEZActivity CreateInventoryPressureReliefActivity() => new FakeActivity("Inventory Relief", ActivityCategory.Utility);
        public IEZActivity CreateDailyProgressionActivity() => new FakeActivity("Daily Progression", ActivityCategory.DailyWeekly);
        public IEZActivity CreateDutyLoopActivity() => new FakeActivity("Duty Loop", ActivityCategory.Duty);
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

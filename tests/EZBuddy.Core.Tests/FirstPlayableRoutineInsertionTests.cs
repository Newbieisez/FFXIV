using EZBuddy.Core.Bundles;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class FirstPlayableRoutineInsertionTests
{
    [Fact]
    public void BeforeProgressionActivitiesStayAheadOfProgressionWhenEarlierStagesAreDisabled()
    {
        var queue = new ActivityQueueEngine();
        var planner = new FirstPlayableBundlePlanner(queue, new FakeFactory());
        var weekly = new NamedActivity("Weekly Routine", ActivityCategory.DailyWeekly);

        var plan = planner.Enqueue(new FirstPlayableBundleOptions(
            RunMaintenance: false,
            RunRetainerSweep: false,
            RunInventoryPressureRelief: false,
            RunDailyProgression: true,
            RunDutyLoop: true,
            ReturnToIdle: false,
            BeforeProgressionActivities: [weekly]));

        Assert.Equal(
            ["Weekly Routine", "Progression", "Duty"],
            plan.StageNames);

        var snapshots = queue.GetSnapshots()
            .OrderByDescending(snapshot => snapshot.EnqueuedAt)
            .ToArray();
        Assert.Equal(3, snapshots.Length);
    }

    [Fact]
    public void BeforeProgressionActivitiesFollowInventoryPreparation()
    {
        var queue = new ActivityQueueEngine();
        var planner = new FirstPlayableBundlePlanner(queue, new FakeFactory());
        var weekly = new NamedActivity("Weekly Routine", ActivityCategory.DailyWeekly);

        var plan = planner.Enqueue(new FirstPlayableBundleOptions(
            RunMaintenance: true,
            RunRetainerSweep: true,
            RunInventoryPressureRelief: true,
            RunDailyProgression: true,
            RunDutyLoop: false,
            ReturnToIdle: false,
            BeforeProgressionActivities: [weekly]));

        Assert.Equal(
            ["Maintenance", "Retainers", "Inventory", "Weekly Routine", "Progression"],
            plan.StageNames);
    }

    private sealed class FakeFactory : IFirstPlayableActivityFactory
    {
        public IEZActivity CreateMaintenanceActivity() => new NamedActivity("Maintenance", ActivityCategory.Utility);
        public IEZActivity CreateRetainerSweepActivity() => new NamedActivity("Retainers", ActivityCategory.Retainers);
        public IEZActivity CreateInventoryPressureReliefActivity() => new NamedActivity("Inventory", ActivityCategory.Utility);
        public IEZActivity CreateDailyProgressionActivity() => new NamedActivity("Progression", ActivityCategory.Questing);
        public IEZActivity CreateDutyLoopActivity() => new NamedActivity("Duty", ActivityCategory.Duty);
    }

    private sealed class NamedActivity(string name, ActivityCategory category) : IEZActivity
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
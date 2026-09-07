using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Bundles;

public sealed record FirstPlayableBundleOptions(
    bool RunMaintenance = true,
    bool RunRetainerSweep = true,
    bool RunInventoryPressureRelief = true,
    bool RunDailyProgression = true,
    bool RunDutyLoop = true,
    bool ReturnToIdle = true,
    int BasePriority = 100,
    int MaxRetriesPerStage = 2,
    IReadOnlyList<IEZActivity>? BeforeProgressionActivities = null);

public interface IFirstPlayableActivityFactory
{
    IEZActivity CreateMaintenanceActivity();
    IEZActivity CreateRetainerSweepActivity();
    IEZActivity CreateInventoryPressureReliefActivity();
    IEZActivity CreateDailyProgressionActivity();
    IEZActivity CreateDutyLoopActivity();
}

public sealed record FirstPlayableBundlePlan(
    IReadOnlyList<Guid> ActivityIds,
    IReadOnlyList<string> StageNames);

public sealed class FirstPlayableBundlePlanner
{
    private readonly ActivityQueueEngine _queue;
    private readonly IFirstPlayableActivityFactory _factory;

    public FirstPlayableBundlePlanner(ActivityQueueEngine queue, IFirstPlayableActivityFactory factory)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public FirstPlayableBundlePlan Enqueue(FirstPlayableBundleOptions? options = null)
    {
        options ??= new FirstPlayableBundleOptions();
        if (options.MaxRetriesPerStage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Retries cannot be negative.");
        }

        var activities = new List<(IEZActivity Activity, string StopLabel)>();
        if (options.RunMaintenance)
        {
            activities.Add((_factory.CreateMaintenanceActivity(), "Maintenance guardrails satisfied"));
        }

        if (options.RunRetainerSweep)
        {
            activities.Add((_factory.CreateRetainerSweepActivity(), "Retainer sweep complete"));
        }

        if (options.RunInventoryPressureRelief)
        {
            activities.Add((_factory.CreateInventoryPressureReliefActivity(), "Inventory pressure relieved or safely blocked"));
        }

        foreach (var activity in options.BeforeProgressionActivities ?? Array.Empty<IEZActivity>())
        {
            activities.Add((activity, $"{activity.Name} routine complete"));
        }

        if (options.RunDailyProgression)
        {
            activities.Add((_factory.CreateDailyProgressionActivity(), "Daily progression pass complete"));
        }

        if (options.RunDutyLoop)
        {
            activities.Add((_factory.CreateDutyLoopActivity(), "Configured duty goal reached"));
        }

        if (options.ReturnToIdle)
        {
            activities.Add((new IdleCompletionActivity(), "First playable loop returned to idle"));
        }

        var activityIds = new List<Guid>(activities.Count);
        var stageNames = new List<string>(activities.Count);
        var priority = options.BasePriority;

        foreach (var (activity, stopLabel) in activities)
        {
            ValidateStage(activity);
            _queue.Enqueue(new ActivityQueueItem(
                activity,
                Priority: priority--,
                MaxRetries: options.MaxRetriesPerStage,
                ContinueOnFailure: false,
                StopConditionLabel: stopLabel));

            activityIds.Add(activity.Id);
            stageNames.Add(activity.Name);
        }

        return new FirstPlayableBundlePlan(activityIds, stageNames);
    }

    private static void ValidateStage(IEZActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"Activity '{activity.Name}' has an empty ID.");
        }

        if (string.IsNullOrWhiteSpace(activity.Name))
        {
            throw new InvalidOperationException("First playable bundle contains an unnamed activity.");
        }
    }
}

public sealed class IdleCompletionActivity : IEZActivity
{
    private bool _complete;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Return to Idle";
    public ActivityCategory Category => ActivityCategory.Core;
    public bool IsComplete => _complete;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _complete = true;
        return Task.FromResult(ExecutionResult.Complete("Configured first playable loop is complete. EZBuddy has no remaining queued work and is returning to idle."));
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
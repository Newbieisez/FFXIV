using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Bundles;

public sealed record FirstPlayableBundleOptions(
    bool RunMaintenance = true,
    bool RunRetainerSweep = true,
    bool RunDailyProgression = true,
    bool RunDutyLoop = true,
    int BasePriority = 100,
    int MaxRetriesPerStage = 2);

public interface IFirstPlayableActivityFactory
{
    IEZActivity CreateMaintenanceActivity();
    IEZActivity CreateRetainerSweepActivity();
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
        if (options.RunDailyProgression)
        {
            activities.Add((_factory.CreateDailyProgressionActivity(), "Daily progression pass complete"));
        }
        if (options.RunDutyLoop)
        {
            activities.Add((_factory.CreateDutyLoopActivity(), "Configured duty goal reached"));
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

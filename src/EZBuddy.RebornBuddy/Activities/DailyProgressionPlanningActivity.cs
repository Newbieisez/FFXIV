using EZBuddy.Core.Engine;
using EZBuddy.RebornBuddy.Progression;

namespace EZBuddy.RebornBuddy.Activities;

public sealed class DailyProgressionPlanningActivity : IEZActivity
{
    private readonly ProgressionPlannerService _planner;
    private bool _complete;

    public DailyProgressionPlanningActivity(ProgressionPlannerService? planner = null)
    {
        _planner = planner ?? new ProgressionPlannerService();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Daily Progression Scan & Plan";
    public ActivityCategory Category => ActivityCategory.Questing;
    public bool IsComplete => _complete;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ff14bot.Core.Player is not null && !ff14bot.Behavior.CommonBehaviors.IsLoading);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (_complete)
        {
            return ExecutionResult.Complete("Progression planning already completed.");
        }

        var result = await _planner.ScanBuildAndQueueAsync(
            "EZBuddy_DailyProgression",
            priority: 1_000,
            cancellationToken: cancellationToken);

        _complete = true;

        var missing = result.Report.Missing.Count;
        var manual = result.ManualOrUnsupportedNodeIds.Count;
        var generated = result.GeneratedProfile is not null;

        if (!generated && missing == 0)
        {
            return ExecutionResult.Complete("Progression scan found no unresolved catalog requirements.");
        }

        if (!generated && manual > 0)
        {
            return ExecutionResult.Block($"Progression scan found {manual} unresolved requirement(s) that require a supported profile or manual action.");
        }

        var message = $"Progression scan found {missing} unresolved requirement(s); generated OrderBot work was queued ahead of remaining bundle stages.";
        if (manual > 0)
        {
            message += $" {manual} requirement(s) remain manual/unsupported and are visible in the progression checklist.";
        }

        return ExecutionResult.Complete(message);
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

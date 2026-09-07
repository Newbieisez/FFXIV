using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Duties;

public sealed record DutyLevelingProgress(
    int CurrentLevel,
    int FreeInventorySlots);

public interface IDutyLevelingProgressProvider
{
    DutyLevelingProgress Read();
}

public sealed record DutySupportLevelingOptions(
    uint DutyId,
    string ProfilePath,
    DutyAutomationMode Mode = DutyAutomationMode.DutySupport,
    int? TrustId = null,
    int? TargetLevel = null,
    int? MaxRuns = 1,
    int MinimumFreeInventorySlots = 5,
    DutyLootPolicy? LootPolicy = null,
    int PostRunConfirmationTimeoutSeconds = 20)
{
    public DutyLootPolicy EffectiveLootPolicy => LootPolicy ?? new DutyLootPolicy();

    public void Validate()
    {
        if (DutyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DutyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProfilePath);

        if (Mode == DutyAutomationMode.Trust && TrustId is null or <= 0)
        {
            throw new ArgumentException("Trust mode requires a positive TrustId.", nameof(TrustId));
        }

        if (TargetLevel is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetLevel));
        }

        if (MaxRuns is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRuns));
        }

        if (TargetLevel is null && MaxRuns is null)
        {
            throw new InvalidOperationException("Duty Support leveling requires at least one stopping goal: TargetLevel or MaxRuns.");
        }

        if (MinimumFreeInventorySlots is < 0 or > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumFreeInventorySlots));
        }

        if (PostRunConfirmationTimeoutSeconds is < 5 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(PostRunConfirmationTimeoutSeconds));
        }

        EffectiveLootPolicy.Validate();
    }
}

public sealed class DutySupportLevelingActivity : IEZActivity
{
    private enum Phase
    {
        QueueDuty,
        AwaitEntry,
        ProfileRunning,
        PostRun,
        AwaitDutyExit
    }

    private readonly IDutySupportAdapter _dutySupport;
    private readonly IOrderBotAdapter _orderBot;
    private readonly IMagitekAdapter _magitek;
    private readonly IDutyLevelingProgressProvider _progress;
    private readonly IDutyPostRunAdapter? _postRun;
    private readonly DutySupportLevelingOptions _options;
    private Phase _phase = Phase.QueueDuty;
    private DateTimeOffset? _postRunStartedAt;
    private int _completedRuns;
    private bool _complete;

    public DutySupportLevelingActivity(
        IDutySupportAdapter dutySupport,
        IOrderBotAdapter orderBot,
        IMagitekAdapter magitek,
        IDutyLevelingProgressProvider progress,
        DutySupportLevelingOptions options,
        IDutyPostRunAdapter? postRun = null)
    {
        _dutySupport = dutySupport ?? throw new ArgumentNullException(nameof(dutySupport));
        _orderBot = orderBot ?? throw new ArgumentNullException(nameof(orderBot));
        _magitek = magitek ?? throw new ArgumentNullException(nameof(magitek));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _postRun = postRun;
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Duty Support / Trust Leveling";
    public ActivityCategory Category => ActivityCategory.Duty;
    public bool IsComplete => _complete;
    public int CompletedRuns => _completedRuns;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GoalReached(_progress.Read(), out _))
        {
            return true;
        }

        if (!await _magitek.IsCurrentRoutineAsync(cancellationToken))
        {
            return false;
        }

        var orderStatus = await _orderBot.GetStatusAsync(cancellationToken);
        return orderStatus.Health is AdapterHealth.Ready or AdapterHealth.Busy;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_complete)
        {
            return ExecutionResult.Complete(BuildCompletionMessage("Leveling goal already completed."));
        }

        var progress = _progress.Read();
        if (GoalReached(progress, out var goalReason))
        {
            _complete = true;
            return ExecutionResult.Complete(BuildCompletionMessage(goalReason));
        }

        if (progress.FreeInventorySlots < _options.MinimumFreeInventorySlots)
        {
            return ExecutionResult.Block(
                $"Duty loop blocked: {progress.FreeInventorySlots} free inventory slots; {_options.MinimumFreeInventorySlots} required.");
        }

        if (!await _magitek.IsCurrentRoutineAsync(cancellationToken))
        {
            return ExecutionResult.Block("Duty Support leveling requires Magitek to be the active combat routine.");
        }

        switch (_phase)
        {
            case Phase.QueueDuty:
                return await QueueDutyAsync(cancellationToken);

            case Phase.AwaitEntry:
                return await AwaitEntryAsync(cancellationToken);

            case Phase.ProfileRunning:
                return await MonitorProfileAsync(cancellationToken);

            case Phase.PostRun:
                return await ProcessPostRunAsync(progress, cancellationToken);

            case Phase.AwaitDutyExit:
                return await AwaitDutyExitAsync(cancellationToken);

            default:
                return ExecutionResult.Fail("Duty Support leveling reached an unknown phase.");
        }
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task<ExecutionResult> QueueDutyAsync(CancellationToken cancellationToken)
    {
        var request = new DutyAutomationRequest(_options.DutyId, _options.Mode, _options.TrustId);
        var queued = await _dutySupport.EnterAsync(request, cancellationToken);
        if (!queued)
        {
            return ExecutionResult.Retry(
                "Duty Support / Trust registration did not complete successfully.",
                TimeSpan.FromSeconds(3));
        }

        _phase = Phase.AwaitEntry;
        return ExecutionResult.Continue("Duty registration submitted; waiting for queue/zone-in on subsequent ticks.");
    }

    private async Task<ExecutionResult> AwaitEntryAsync(CancellationToken cancellationToken)
    {
        var dutyStatus = await _dutySupport.GetDutyStatusAsync(cancellationToken);
        if (dutyStatus.IsInDungeon)
        {
            var profileStarted = await _orderBot.LoadProfileAsync(_options.ProfilePath, cancellationToken);
            if (!profileStarted)
            {
                return ExecutionResult.Retry(
                    "Duty entered, but OrderBot could not start the configured duty profile.",
                    TimeSpan.FromSeconds(3));
            }

            _phase = Phase.ProfileRunning;
            return ExecutionResult.Yield("Duty entered and verified OrderBot profile handoff started.");
        }

        if (string.Equals(dutyStatus.State, "None", StringComparison.OrdinalIgnoreCase))
        {
            _phase = Phase.QueueDuty;
            return ExecutionResult.Retry(
                "Duty queue returned to None before zone-in; registration will be retried.",
                TimeSpan.FromSeconds(3));
        }

        var advanced = await _dutySupport.AdvanceEntryAsync(cancellationToken);
        if (!advanced)
        {
            return ExecutionResult.Retry(
                $"Duty entry bridge could not safely advance state '{dutyStatus.State}'.",
                TimeSpan.FromSeconds(2));
        }

        return ExecutionResult.Yield($"Waiting for duty entry. Current state: {dutyStatus.State}.");
    }

    private async Task<ExecutionResult> MonitorProfileAsync(CancellationToken cancellationToken)
    {
        if (await _orderBot.IsProfileRunningAsync(cancellationToken))
        {
            return ExecutionResult.Yield($"Duty profile is running. Completed runs: {_completedRuns}.");
        }

        var dutyStatus = await _dutySupport.GetDutyStatusAsync(cancellationToken);
        if (!dutyStatus.IsInDungeon)
        {
            return FinishRun();
        }

        if (_postRun is null)
        {
            return ExecutionResult.Block(
                "The duty profile stopped while still inside the instance, and no post-run completion/loot/exit adapter is configured. EZBuddy will not guess that the duty is complete.");
        }

        _postRunStartedAt = DateTimeOffset.UtcNow;
        _phase = Phase.PostRun;
        return ExecutionResult.Yield("Duty profile ended inside the instance; verifying director completion before loot or exit actions.");
    }

    private async Task<ExecutionResult> ProcessPostRunAsync(
        DutyLevelingProgress progress,
        CancellationToken cancellationToken)
    {
        if (_postRun is null)
        {
            return ExecutionResult.Block("Post-run adapter became unavailable.");
        }

        var postStatus = await _postRun.GetStatusAsync(cancellationToken);
        if (!postStatus.IsDutyComplete)
        {
            var startedAt = _postRunStartedAt ?? DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromSeconds(_options.PostRunConfirmationTimeoutSeconds))
            {
                return ExecutionResult.Block(
                    $"Duty profile stopped, but instance completion was not confirmed within {_options.PostRunConfirmationTimeoutSeconds} seconds. No loot or leave action was taken. Last status: {postStatus.Message}");
            }

            return ExecutionResult.Yield($"Waiting for confirmed duty completion. {postStatus.Message}");
        }

        if (postStatus.IsLootWindowOpen)
        {
            var processed = await _postRun.ProcessLootAsync(
                _options.EffectiveLootPolicy,
                progress.FreeInventorySlots,
                cancellationToken);

            if (!processed)
            {
                return ExecutionResult.Retry(
                    "Duty completion is confirmed, but the loot window could not be processed safely.",
                    TimeSpan.FromSeconds(2));
            }

            return ExecutionResult.Yield("Post-duty loot policy applied; rechecking completion UI before leaving.");
        }

        if (!postStatus.IsLeaveRequested)
        {
            var leaveRequested = await _postRun.RequestLeaveAsync(cancellationToken);
            if (!leaveRequested)
            {
                return ExecutionResult.Retry(
                    "Duty completion is confirmed, but the instance leave request failed.",
                    TimeSpan.FromSeconds(2));
            }
        }

        _phase = Phase.AwaitDutyExit;
        return ExecutionResult.Yield("Instance leave requested; waiting for the next host tick to confirm zone exit.");
    }

    private async Task<ExecutionResult> AwaitDutyExitAsync(CancellationToken cancellationToken)
    {
        var status = await _dutySupport.GetDutyStatusAsync(cancellationToken);
        if (status.IsInDungeon)
        {
            return ExecutionResult.Yield("Waiting for confirmed duty exit.");
        }

        return FinishRun();
    }

    private ExecutionResult FinishRun()
    {
        _completedRuns++;
        _phase = Phase.QueueDuty;
        _postRunStartedAt = null;

        var progress = _progress.Read();
        if (GoalReached(progress, out var goalReason))
        {
            _complete = true;
            return ExecutionResult.Complete(BuildCompletionMessage(goalReason));
        }

        return ExecutionResult.Continue($"Duty run {_completedRuns} completed; preparing the next configured run.");
    }

    private bool GoalReached(DutyLevelingProgress progress, out string reason)
    {
        if (_options.TargetLevel is { } targetLevel && progress.CurrentLevel >= targetLevel)
        {
            reason = $"Target level {targetLevel} reached (current level {progress.CurrentLevel}).";
            return true;
        }

        if (_options.MaxRuns is { } maxRuns && _completedRuns >= maxRuns)
        {
            reason = $"Configured run limit of {maxRuns} reached.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private string BuildCompletionMessage(string reason)
        => $"{reason} Total completed runs: {_completedRuns}.";
}

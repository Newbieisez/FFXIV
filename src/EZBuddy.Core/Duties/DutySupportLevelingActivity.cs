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
    int MinimumFreeInventorySlots = 5)
{
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
    }
}

public sealed class DutySupportLevelingActivity : IEZActivity
{
    private enum Phase
    {
        EnterDuty,
        ProfileRunning,
        AwaitDutyExit
    }

    private readonly IDutySupportAdapter _dutySupport;
    private readonly IOrderBotAdapter _orderBot;
    private readonly IMagitekAdapter _magitek;
    private readonly IDutyLevelingProgressProvider _progress;
    private readonly DutySupportLevelingOptions _options;
    private Phase _phase = Phase.EnterDuty;
    private int _completedRuns;
    private bool _complete;

    public DutySupportLevelingActivity(
        IDutySupportAdapter dutySupport,
        IOrderBotAdapter orderBot,
        IMagitekAdapter magitek,
        IDutyLevelingProgressProvider progress,
        DutySupportLevelingOptions options)
    {
        _dutySupport = dutySupport ?? throw new ArgumentNullException(nameof(dutySupport));
        _orderBot = orderBot ?? throw new ArgumentNullException(nameof(orderBot));
        _magitek = magitek ?? throw new ArgumentNullException(nameof(magitek));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

        if (!await _magitek.IsCurrentRoutineAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var dutyStatus = await _dutySupport.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var orderStatus = await _orderBot.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return dutyStatus.Health is AdapterHealth.Ready or AdapterHealth.Busy &&
               orderStatus.Health is AdapterHealth.Ready or AdapterHealth.Busy;
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

        if (!await _magitek.IsCurrentRoutineAsync(cancellationToken).ConfigureAwait(false))
        {
            return ExecutionResult.Block("Duty Support leveling requires Magitek to be the active combat routine.");
        }

        if (_phase == Phase.ProfileRunning)
        {
            if (await _orderBot.IsProfileRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                return ExecutionResult.Yield($"Duty profile is running. Completed runs: {_completedRuns}.");
            }

            _phase = Phase.AwaitDutyExit;
        }

        if (_phase == Phase.AwaitDutyExit)
        {
            var status = await _dutySupport.GetDutyStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsInDungeon)
            {
                return ExecutionResult.Retry(
                    "The OrderBot duty profile stopped while the character is still inside the duty. Waiting for a clean exit or recovery instead of starting another run.",
                    TimeSpan.FromSeconds(3));
            }

            _completedRuns++;
            _phase = Phase.EnterDuty;

            progress = _progress.Read();
            if (GoalReached(progress, out goalReason))
            {
                _complete = true;
                return ExecutionResult.Complete(BuildCompletionMessage(goalReason));
            }

            return ExecutionResult.Continue($"Duty run {_completedRuns} completed; preparing the next configured run.");
        }

        var request = new DutyAutomationRequest(_options.DutyId, _options.Mode, _options.TrustId);
        var entered = await _dutySupport.EnterAsync(request, cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            return ExecutionResult.Retry("Duty Support / Trust entry did not complete successfully.", TimeSpan.FromSeconds(5));
        }

        var profileStarted = await _orderBot.LoadProfileAsync(_options.ProfilePath, cancellationToken).ConfigureAwait(false);
        if (!profileStarted)
        {
            return ExecutionResult.Retry("Entered the duty, but OrderBot could not start the configured duty profile.", TimeSpan.FromSeconds(3));
        }

        _phase = Phase.ProfileRunning;
        return ExecutionResult.Yield("Duty entered and verified OrderBot profile handoff started.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
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

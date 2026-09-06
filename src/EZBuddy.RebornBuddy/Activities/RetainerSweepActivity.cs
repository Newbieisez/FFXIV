using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Retainers;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Activities;

public sealed record RetainerSweepOptions(
    int MinimumFreeInventorySlots = 6,
    TimeSpan? BellLeaseTimeout = null)
{
    public TimeSpan EffectiveBellLeaseTimeout => BellLeaseTimeout ?? TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (MinimumFreeInventorySlots < 0 || MinimumFreeInventorySlots > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumFreeInventorySlots));
        }

        if (EffectiveBellLeaseTimeout <= TimeSpan.Zero || EffectiveBellLeaseTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(BellLeaseTimeout));
        }
    }
}

public sealed class RetainerSweepActivity : IEZActivity
{
    private readonly IRetainerSweepAdapter _retainerAdapter;
    private readonly IRetainerBellCoordinator _bellCoordinator;
    private readonly RetainerSweepOptions _options;
    private bool _complete;

    public RetainerSweepActivity(
        IRetainerSweepAdapter retainerAdapter,
        IRetainerBellCoordinator bellCoordinator,
        RetainerSweepOptions? options = null)
    {
        _retainerAdapter = retainerAdapter ?? throw new ArgumentNullException(nameof(retainerAdapter));
        _bellCoordinator = bellCoordinator ?? throw new ArgumentNullException(nameof(bellCoordinator));
        _options = options ?? new RetainerSweepOptions();
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Retainer Venture Sweep";
    public ActivityCategory Category => ActivityCategory.Retainers;
    public bool IsComplete => _complete;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ff14bot.Core.Player is null || ff14bot.Behavior.CommonBehaviors.IsLoading)
        {
            return false;
        }

        var status = await _retainerAdapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Health == AdapterHealth.Ready;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (_complete)
        {
            return ExecutionResult.Complete("Retainer venture sweep already completed.");
        }

        var freeSlots = InventoryManager.FreeSlots;
        if (freeSlots < _options.MinimumFreeInventorySlots)
        {
            return ExecutionResult.Block(
                $"Retainer sweep blocked: {freeSlots} free inventory slots; {_options.MinimumFreeInventorySlots} required.");
        }

        await using var lease = await _bellCoordinator.TryAcquireAsync(
            "retainer-venture-sweep",
            _options.EffectiveBellLeaseTimeout,
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            return ExecutionResult.Retry("Summoning bell is owned by another EZBuddy activity.", TimeSpan.FromSeconds(2));
        }

        var succeeded = await _retainerAdapter.SweepCompletedVenturesAsync(cancellationToken).ConfigureAwait(false);
        if (!succeeded)
        {
            return ExecutionResult.Retry("Retainer sweep bridge did not complete successfully.", TimeSpan.FromSeconds(3));
        }

        _complete = true;
        return ExecutionResult.Complete("Retainer venture sweep completed under the shared bell lease.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Retainers;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Activities;

public sealed record RetainerSweepOptions(
    int MinimumFreeInventorySlots = 8,
    int MinimumVentureTokens = 10,
    int TargetVentureTokens = 50,
    uint VentureItemId = 21072,
    IReadOnlyCollection<uint>? ApprovedExpertDeliveryItemIds = null,
    TimeSpan? BellLeaseTimeout = null)
{
    public TimeSpan EffectiveBellLeaseTimeout => BellLeaseTimeout ?? TimeSpan.FromSeconds(10);
    public IReadOnlyCollection<uint> EffectiveApprovedExpertDeliveryItemIds => ApprovedExpertDeliveryItemIds ?? Array.Empty<uint>();

    public void Validate()
    {
        if (MinimumFreeInventorySlots < 0 || MinimumFreeInventorySlots > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumFreeInventorySlots));
        }

        if (MinimumVentureTokens < 0 || TargetVentureTokens < MinimumVentureTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetVentureTokens), "Target venture tokens must be greater than or equal to the minimum threshold.");
        }

        if (VentureItemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VentureItemId));
        }

        if (EffectiveApprovedExpertDeliveryItemIds.Any(itemId => itemId == 0))
        {
            throw new ArgumentException("Approved Expert Delivery item IDs cannot contain zero.", nameof(ApprovedExpertDeliveryItemIds));
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
    private readonly IRetainerBellAccess? _bellAccess;
    private readonly IGrandCompanyAdapter? _grandCompanyAdapter;
    private readonly RetainerSweepOptions _options;
    private bool _complete;

    public RetainerSweepActivity(
        IRetainerSweepAdapter retainerAdapter,
        IRetainerBellCoordinator bellCoordinator,
        RetainerSweepOptions? options = null,
        IRetainerBellAccess? bellAccess = null,
        IGrandCompanyAdapter? grandCompanyAdapter = null)
    {
        _retainerAdapter = retainerAdapter ?? throw new ArgumentNullException(nameof(retainerAdapter));
        _bellCoordinator = bellCoordinator ?? throw new ArgumentNullException(nameof(bellCoordinator));
        _options = options ?? new RetainerSweepOptions();
        _options.Validate();
        _bellAccess = bellAccess;
        _grandCompanyAdapter = grandCompanyAdapter;
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
        return status.Health is AdapterHealth.Ready or AdapterHealth.Busy;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_complete)
        {
            return ExecutionResult.Complete("Retainer venture sweep already completed.");
        }

        var freeSlots = InventoryManager.FreeSlots;
        if (freeSlots < _options.MinimumFreeInventorySlots)
        {
            return ExecutionResult.Block(
                $"Retainer sweep blocked: {freeSlots} free inventory slots; {_options.MinimumFreeInventorySlots} required before venture rewards are collected.");
        }

        var ventureTokens = GetInventoryQuantity(_options.VentureItemId);
        if (ventureTokens < _options.MinimumVentureTokens)
        {
            var refillResult = await TryRefillVentureTokensAsync(ventureTokens, cancellationToken).ConfigureAwait(false);
            if (!refillResult.Success)
            {
                return ExecutionResult.Block(refillResult.Message);
            }

            ventureTokens = GetInventoryQuantity(_options.VentureItemId);
            if (ventureTokens < _options.MinimumVentureTokens)
            {
                return ExecutionResult.Block(
                    $"Venture-token refill completed without reaching the safety floor. Have {ventureTokens}; require {_options.MinimumVentureTokens}.");
            }
        }

        await using var lease = await _bellCoordinator.TryAcquireAsync(
            "retainer-venture-sweep",
            _options.EffectiveBellLeaseTimeout,
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            return ExecutionResult.Retry("Summoning bell is owned by another EZBuddy activity.", TimeSpan.FromSeconds(2));
        }

        if (_bellAccess is not null)
        {
            var bellResult = await _bellAccess.EnsureBellOpenAsync(cancellationToken).ConfigureAwait(false);
            if (!bellResult.Success)
            {
                return ExecutionResult.Retry(bellResult.Message, TimeSpan.FromSeconds(3));
            }
        }

        var succeeded = await _retainerAdapter.SweepCompletedVenturesAsync(cancellationToken).ConfigureAwait(false);
        if (!succeeded)
        {
            return ExecutionResult.Retry("Retainer sweep bridge did not complete successfully.", TimeSpan.FromSeconds(3));
        }

        _complete = true;
        return ExecutionResult.Complete(
            $"Retainer venture sweep completed under the shared bell lease with {InventoryManager.FreeSlots} free inventory slots remaining.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task<(bool Success, string Message)> TryRefillVentureTokensAsync(
        int currentQuantity,
        CancellationToken cancellationToken)
    {
        if (_grandCompanyAdapter is null)
        {
            return (false, $"Only {currentQuantity} Venture tokens remain; {_options.MinimumVentureTokens} are required and no Grand Company refill adapter is configured.");
        }

        var status = await _grandCompanyAdapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Health is not (AdapterHealth.Ready or AdapterHealth.Degraded))
        {
            return (false, $"Venture tokens are below the safety floor and the Grand Company refill bridge is unavailable: {status.Message}");
        }

        if (await _grandCompanyAdapter.EnsureVentureTokensAsync(
                _options.VentureItemId,
                currentQuantity,
                _options.TargetVentureTokens,
                cancellationToken).ConfigureAwait(false))
        {
            return (true, "Venture tokens refilled from existing Grand Company seals.");
        }

        var approvedItems = _options.EffectiveApprovedExpertDeliveryItemIds;
        if (approvedItems.Count == 0)
        {
            return (false,
                "Venture-token purchase failed and no explicitly approved Expert Delivery item IDs are configured. EZBuddy will not hand in inventory automatically without an allowlist.");
        }

        var delivered = await _grandCompanyAdapter.RunExpertDeliveryAsync(approvedItems, cancellationToken).ConfigureAwait(false);
        if (!delivered)
        {
            return (false, "Grand Company Expert Delivery did not complete successfully; Venture-token refill was stopped.");
        }

        var refreshedQuantity = GetInventoryQuantity(_options.VentureItemId);
        var purchased = await _grandCompanyAdapter.EnsureVentureTokensAsync(
            _options.VentureItemId,
            refreshedQuantity,
            _options.TargetVentureTokens,
            cancellationToken).ConfigureAwait(false);

        return purchased
            ? (true, "Approved Expert Delivery items were exchanged and Venture tokens were refilled.")
            : (false, "Expert Delivery completed, but Venture-token purchase still failed.");
    }

    private static int GetInventoryQuantity(uint itemId)
        => InventoryManager.FilledSlots
            .Where(slot => NormalizeItemId(slot.RawItemId) == itemId)
            .Sum(slot => checked((int)slot.Count));

    private static uint NormalizeItemId(uint rawItemId)
    {
        if (rawItemId > 1_000_000U)
        {
            return rawItemId - 1_000_000U;
        }

        if (rawItemId > 500_000U)
        {
            return rawItemId - 500_000U;
        }

        return rawItemId;
    }
}

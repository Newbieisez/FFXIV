using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

public interface IItemQuantityProvider
{
    int GetQuantity(uint itemId);
}

public sealed record GrandCompanyExpertDeliveryOptions(
    IReadOnlyCollection<uint> ApprovedItemIds)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ApprovedItemIds);
        if (ApprovedItemIds.Count == 0 || ApprovedItemIds.Any(itemId => itemId == 0))
        {
            throw new ArgumentException(
                "Grand Company Expert Delivery requires a non-empty explicit item allowlist containing only non-zero item IDs.",
                nameof(ApprovedItemIds));
        }
    }
}

public sealed class GrandCompanyExpertDeliveryActivity : IEZActivity
{
    private readonly IGrandCompanyAdapter _grandCompany;
    private readonly GrandCompanyExpertDeliveryOptions _options;
    private bool _attempted;
    private bool _complete;

    public GrandCompanyExpertDeliveryActivity(
        IGrandCompanyAdapter grandCompany,
        GrandCompanyExpertDeliveryOptions options)
    {
        _grandCompany = grandCompany ?? throw new ArgumentNullException(nameof(grandCompany));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Grand Company Expert Delivery";
    public ActivityCategory Category => ActivityCategory.DailyWeekly;
    public bool IsComplete => _complete;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_attempted || _complete)
        {
            return false;
        }

        var status = await _grandCompany.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Health is AdapterHealth.Ready or AdapterHealth.Degraded;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete)
        {
            return ExecutionResult.Complete("Grand Company Expert Delivery already completed.");
        }

        if (_attempted)
        {
            return ExecutionResult.Block(
                "Expert Delivery already returned an ambiguous failure in this activity. Review inventory and seal state before trying again.");
        }

        var status = await _grandCompany.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Health is not (AdapterHealth.Ready or AdapterHealth.Degraded))
        {
            return ExecutionResult.Block($"Grand Company bridge is not ready: {status.Message}");
        }

        _attempted = true;
        var delivered = await _grandCompany.RunExpertDeliveryAsync(
            _options.ApprovedItemIds,
            cancellationToken).ConfigureAwait(false);
        if (!delivered)
        {
            return ExecutionResult.Block(
                "Approved Expert Delivery did not report a clean success. EZBuddy will not repeat the destructive pass automatically.");
        }

        _complete = true;
        return ExecutionResult.Complete(
            $"Grand Company Expert Delivery completed using {_options.ApprovedItemIds.Count} explicitly approved item ID(s).");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed record VentureTokenRefillOptions(
    uint VentureItemId = 21072,
    int MinimumQuantity = 10,
    int TargetQuantity = 50,
    IReadOnlyCollection<uint>? ApprovedExpertDeliveryItemIds = null)
{
    public IReadOnlyCollection<uint> EffectiveApprovedExpertDeliveryItemIds =>
        ApprovedExpertDeliveryItemIds ?? Array.Empty<uint>();

    public void Validate()
    {
        if (VentureItemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VentureItemId));
        }

        if (MinimumQuantity < 0 || TargetQuantity < MinimumQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TargetQuantity),
                "Target Venture quantity must be greater than or equal to the minimum quantity.");
        }

        if (EffectiveApprovedExpertDeliveryItemIds.Any(itemId => itemId == 0))
        {
            throw new ArgumentException(
                "Approved Expert Delivery item IDs cannot contain zero.",
                nameof(ApprovedExpertDeliveryItemIds));
        }
    }
}

public sealed class VentureTokenRefillActivity : IEZActivity
{
    private readonly IGrandCompanyAdapter _grandCompany;
    private readonly IItemQuantityProvider _quantities;
    private readonly VentureTokenRefillOptions _options;
    private bool _complete;
    private bool _deliveryAttempted;
    private bool _purchaseAttemptedAfterDelivery;

    public VentureTokenRefillActivity(
        IGrandCompanyAdapter grandCompany,
        IItemQuantityProvider quantities,
        VentureTokenRefillOptions? options = null)
    {
        _grandCompany = grandCompany ?? throw new ArgumentNullException(nameof(grandCompany));
        _quantities = quantities ?? throw new ArgumentNullException(nameof(quantities));
        _options = options ?? new VentureTokenRefillOptions();
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Venture Token Refill";
    public ActivityCategory Category => ActivityCategory.Retainers;
    public bool IsComplete => _complete;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete)
        {
            return false;
        }

        if (_quantities.GetQuantity(_options.VentureItemId) >= _options.MinimumQuantity)
        {
            return true;
        }

        var status = await _grandCompany.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Health is AdapterHealth.Ready or AdapterHealth.Degraded;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete)
        {
            return ExecutionResult.Complete("Venture-token reserve already satisfied.");
        }

        var current = _quantities.GetQuantity(_options.VentureItemId);
        if (current >= _options.MinimumQuantity)
        {
            _complete = true;
            return ExecutionResult.Complete(
                $"Venture-token reserve is healthy at {current}; minimum is {_options.MinimumQuantity}.");
        }

        var status = await _grandCompany.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Health is not (AdapterHealth.Ready or AdapterHealth.Degraded))
        {
            return ExecutionResult.Block($"Venture refill requires the Grand Company bridge: {status.Message}");
        }

        if (!_deliveryAttempted)
        {
            var purchased = await _grandCompany.EnsureVentureTokensAsync(
                _options.VentureItemId,
                current,
                _options.TargetQuantity,
                cancellationToken).ConfigureAwait(false);
            if (purchased)
            {
                return CompleteIfReserveReached();
            }

            if (_options.EffectiveApprovedExpertDeliveryItemIds.Count == 0)
            {
                return ExecutionResult.Block(
                    "Venture purchase failed and no explicitly approved Expert Delivery allowlist is configured. EZBuddy will not hand in inventory automatically.");
            }

            _deliveryAttempted = true;
            var delivered = await _grandCompany.RunExpertDeliveryAsync(
                _options.EffectiveApprovedExpertDeliveryItemIds,
                cancellationToken).ConfigureAwait(false);
            if (!delivered)
            {
                return ExecutionResult.Block(
                    "Approved Expert Delivery did not report a clean success; Venture refill stopped without repeating the destructive pass.");
            }

            return ExecutionResult.Continue(
                "Approved Expert Delivery completed; rechecking Venture-token reserve before one final purchase attempt.");
        }

        if (_purchaseAttemptedAfterDelivery)
        {
            return ExecutionResult.Block(
                "Venture-token reserve remains below the configured minimum after the one approved refill sequence.");
        }

        _purchaseAttemptedAfterDelivery = true;
        current = _quantities.GetQuantity(_options.VentureItemId);
        var finalPurchase = await _grandCompany.EnsureVentureTokensAsync(
            _options.VentureItemId,
            current,
            _options.TargetQuantity,
            cancellationToken).ConfigureAwait(false);
        if (!finalPurchase)
        {
            return ExecutionResult.Block(
                "Expert Delivery completed, but the final Venture-token purchase did not report success.");
        }

        return CompleteIfReserveReached();
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private ExecutionResult CompleteIfReserveReached()
    {
        var current = _quantities.GetQuantity(_options.VentureItemId);
        if (current < _options.MinimumQuantity)
        {
            return ExecutionResult.Block(
                $"Grand Company purchase returned success, but only {current} Venture token(s) are visible; {_options.MinimumQuantity} required.");
        }

        _complete = true;
        return ExecutionResult.Complete(
            $"Venture-token reserve restored to {current}; configured target is {_options.TargetQuantity}.");
    }
}
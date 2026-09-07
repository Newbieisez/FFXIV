using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

public sealed record GrandCompanySealState(int CurrentSeals, int MaximumSeals)
{
    public int RemainingCapacity => MaximumSeals - CurrentSeals;

    public void Validate()
    {
        if (CurrentSeals < 0 || MaximumSeals <= 0 || CurrentSeals > MaximumSeals)
        {
            throw new InvalidDataException(
                $"Invalid Grand Company seal state {CurrentSeals}/{MaximumSeals}.");
        }
    }
}

public interface IGrandCompanySealStateProvider
{
    Task<GrandCompanySealState?> GetStateAsync(CancellationToken cancellationToken = default);
}

public sealed record VentureSealPressureOptions(int SealSafetyBuffer = 1000)
{
    public void Validate()
    {
        if (SealSafetyBuffer < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SealSafetyBuffer));
        }
    }
}

/// <summary>
/// Extends the existing Venture reserve routine with one deterministic, user-bounded seal-pressure
/// action. It never buys above the configured Venture target and never uses Expert Delivery merely
/// to relieve seal pressure.
/// </summary>
public sealed class VentureSealPressureActivity : IEZActivity
{
    private readonly IGrandCompanyAdapter _grandCompany;
    private readonly IItemQuantityProvider _quantities;
    private readonly IGrandCompanySealStateProvider _sealState;
    private readonly VentureTokenRefillOptions _ventureOptions;
    private readonly VentureSealPressureOptions _pressureOptions;
    private readonly VentureTokenRefillActivity _reserveRefill;
    private bool _complete;
    private bool _pressurePurchaseAttempted;

    public VentureSealPressureActivity(
        IGrandCompanyAdapter grandCompany,
        IItemQuantityProvider quantities,
        IGrandCompanySealStateProvider sealState,
        VentureTokenRefillOptions? ventureOptions = null,
        VentureSealPressureOptions? pressureOptions = null)
    {
        _grandCompany = grandCompany ?? throw new ArgumentNullException(nameof(grandCompany));
        _quantities = quantities ?? throw new ArgumentNullException(nameof(quantities));
        _sealState = sealState ?? throw new ArgumentNullException(nameof(sealState));
        _ventureOptions = ventureOptions ?? new VentureTokenRefillOptions();
        _pressureOptions = pressureOptions ?? new VentureSealPressureOptions();
        _ventureOptions.Validate();
        _pressureOptions.Validate();
        _reserveRefill = new VentureTokenRefillActivity(_grandCompany, _quantities, _ventureOptions);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Venture Reserve & GC Seal Pressure";
    public ActivityCategory Category => ActivityCategory.Retainers;
    public bool IsComplete => _complete || _reserveRefill.IsComplete;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsComplete)
        {
            return false;
        }

        var current = _quantities.GetQuantity(_ventureOptions.VentureItemId);
        if (current < _ventureOptions.MinimumQuantity)
        {
            return await _reserveRefill.CanExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsComplete)
        {
            return ExecutionResult.Complete("Venture reserve and seal-pressure check already completed.");
        }

        var current = _quantities.GetQuantity(_ventureOptions.VentureItemId);
        if (current < _ventureOptions.MinimumQuantity)
        {
            return await _reserveRefill.ExecuteStepAsync(cancellationToken).ConfigureAwait(false);
        }

        var state = await _sealState.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return ExecutionResult.Block(
                "Grand Company seal pressure could not be verified. EZBuddy will not spend seals or continue into configured GC turn-ins until seal state is available.");
        }

        try
        {
            state.Validate();
        }
        catch (InvalidDataException exception)
        {
            return ExecutionResult.Block(exception.Message);
        }

        if (state.RemainingCapacity > _pressureOptions.SealSafetyBuffer)
        {
            _complete = true;
            return ExecutionResult.Complete(
                $"Venture reserve is healthy at {current}; Grand Company seals have {state.RemainingCapacity:N0} free capacity, above the {_pressureOptions.SealSafetyBuffer:N0} safety buffer.");
        }

        if (current >= _ventureOptions.TargetQuantity)
        {
            return ExecutionResult.Block(
                $"Grand Company seals are within {_pressureOptions.SealSafetyBuffer:N0} of cap, but the configured Venture target of {_ventureOptions.TargetQuantity} is already satisfied. Configure another approved spend path or raise the Venture target before GC turn-ins continue.");
        }

        if (_pressurePurchaseAttempted)
        {
            return ExecutionResult.Block(
                "The one approved Venture purchase for seal-pressure relief was already attempted. Review Grand Company and Venture state before retrying.");
        }

        var status = await _grandCompany.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Health is not (AdapterHealth.Ready or AdapterHealth.Degraded))
        {
            return ExecutionResult.Block($"Seal-pressure relief requires the Grand Company bridge: {status.Message}");
        }

        _pressurePurchaseAttempted = true;
        var purchased = await _grandCompany.EnsureVentureTokensAsync(
            _ventureOptions.VentureItemId,
            current,
            _ventureOptions.TargetQuantity,
            cancellationToken).ConfigureAwait(false);
        if (!purchased)
        {
            return ExecutionResult.Block(
                "The approved Venture purchase did not report a clean success. EZBuddy will not repeat the purchase or run Expert Delivery as a seal-pressure fallback.");
        }

        var visibleQuantity = _quantities.GetQuantity(_ventureOptions.VentureItemId);
        if (visibleQuantity < _ventureOptions.TargetQuantity)
        {
            return ExecutionResult.Block(
                $"The GC purchase returned success, but only {visibleQuantity} Venture token(s) are visible; configured target is {_ventureOptions.TargetQuantity}. No repeat purchase will be attempted automatically.");
        }

        _complete = true;
        return ExecutionResult.Complete(
            $"Grand Company seal pressure relieved by topping Venture tokens from {current} to {visibleQuantity}, within the configured target of {_ventureOptions.TargetQuantity}.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
        => _reserveRefill.OnHaltAsync(cancellationToken);
}

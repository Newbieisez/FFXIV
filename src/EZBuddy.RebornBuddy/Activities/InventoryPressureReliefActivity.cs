using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Activities;

public sealed record InventoryPressureReliefOptions(
    int TargetFreeInventorySlots = 12,
    bool ExtractMateriaBeforeTurnIn = true,
    IReadOnlyCollection<uint>? ApprovedExpertDeliveryItemIds = null)
{
    public IReadOnlyCollection<uint> EffectiveApprovedExpertDeliveryItemIds =>
        ApprovedExpertDeliveryItemIds ?? Array.Empty<uint>();

    public void Validate()
    {
        if (TargetFreeInventorySlots is < 0 or > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetFreeInventorySlots));
        }

        if (EffectiveApprovedExpertDeliveryItemIds.Any(itemId => itemId == 0))
        {
            throw new ArgumentException(
                "Approved Expert Delivery item IDs cannot contain zero.",
                nameof(ApprovedExpertDeliveryItemIds));
        }
    }
}

public sealed class InventoryPressureReliefActivity : IEZActivity
{
    private readonly ILisbethAdapter _lisbeth;
    private readonly IGrandCompanyAdapter _grandCompany;
    private readonly InventoryPressureReliefOptions _options;
    private bool _materiaAttempted;
    private bool _deliveryAttempted;
    private bool _complete;

    public InventoryPressureReliefActivity(
        ILisbethAdapter lisbeth,
        IGrandCompanyAdapter grandCompany,
        InventoryPressureReliefOptions? options = null)
    {
        _lisbeth = lisbeth ?? throw new ArgumentNullException(nameof(lisbeth));
        _grandCompany = grandCompany ?? throw new ArgumentNullException(nameof(grandCompany));
        _options = options ?? new InventoryPressureReliefOptions();
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Inventory Pressure Relief";
    public ActivityCategory Category => ActivityCategory.Utility;
    public bool IsComplete => _complete;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            ff14bot.Core.Player is not null &&
            !ff14bot.Behavior.CommonBehaviors.IsLoading &&
            !ff14bot.Managers.DutyManager.InInstance);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var freeSlots = checked((int)InventoryManager.FreeSlots);
        if (freeSlots >= _options.TargetFreeInventorySlots)
        {
            _complete = true;
            return ExecutionResult.Complete(
                $"Inventory has {freeSlots} free slots; target is {_options.TargetFreeInventorySlots}.");
        }

        if (_options.ExtractMateriaBeforeTurnIn && !_materiaAttempted)
        {
            _materiaAttempted = true;
            var lisbethStatus = await _lisbeth.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (lisbethStatus.Health is AdapterHealth.Ready or AdapterHealth.Busy)
            {
                var extracted = await _lisbeth.ExtractMateriaAsync(cancellationToken).ConfigureAwait(false);
                if (extracted)
                {
                    return ExecutionResult.Continue(
                        "Materia extraction pass completed before destructive inventory relief; rechecking free slots.");
                }
            }
        }

        if (_deliveryAttempted)
        {
            return ExecutionResult.Block(
                $"Inventory pressure remains after the approved cleanup pass: {InventoryManager.FreeSlots} free slots; {_options.TargetFreeInventorySlots} required.");
        }

        var approvedItems = _options.EffectiveApprovedExpertDeliveryItemIds;
        if (approvedItems.Count == 0)
        {
            return ExecutionResult.Block(
                $"Inventory has only {freeSlots} free slots. No Expert Delivery item allowlist is configured, so EZBuddy will not destroy or hand in inventory automatically.");
        }

        _deliveryAttempted = true;
        var gcStatus = await _grandCompany.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (gcStatus.Health is not (AdapterHealth.Ready or AdapterHealth.Degraded))
        {
            return ExecutionResult.Block(
                $"Inventory pressure requires an approved Grand Company turn-in, but the GC bridge is unavailable: {gcStatus.Message}");
        }

        var delivered = await _grandCompany
            .RunExpertDeliveryAsync(approvedItems, cancellationToken)
            .ConfigureAwait(false);

        if (!delivered)
        {
            return ExecutionResult.Block(
                "Approved Grand Company Expert Delivery did not complete; inventory cleanup stopped without attempting broader item disposal.");
        }

        freeSlots = checked((int)InventoryManager.FreeSlots);
        if (freeSlots < _options.TargetFreeInventorySlots)
        {
            return ExecutionResult.Block(
                $"Approved Expert Delivery completed, but only {freeSlots} free slots are available; {_options.TargetFreeInventorySlots} required.");
        }

        _complete = true;
        return ExecutionResult.Complete(
            $"Inventory pressure relieved safely; {freeSlots} free slots are available.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

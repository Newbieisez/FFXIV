using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Activities;

public sealed record MaintenanceOptions(
    int MinimumFreeInventorySlots = 6,
    int RepairBelowPercent = 30,
    uint FoodItemId = 0,
    bool RequireWellFed = false,
    bool AllowMenderFallback = true)
{
    public void Validate()
    {
        if (MinimumFreeInventorySlots < 0 || MinimumFreeInventorySlots > 140)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumFreeInventorySlots));
        }

        if (RepairBelowPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(RepairBelowPercent));
        }

        if (RequireWellFed && FoodItemId == 0)
        {
            throw new ArgumentException("A food item ID is required when Well Fed maintenance is enabled.", nameof(FoodItemId));
        }
    }
}

public sealed class MaintenanceActivity : IEZActivity
{
    private const uint WellFedAuraId = 48;
    private readonly ILisbethAdapter _lisbeth;
    private readonly MaintenanceOptions _options;
    private bool _complete;
    private bool _repairAttempted;
    private bool _foodAttempted;

    public MaintenanceActivity(ILisbethAdapter lisbeth, MaintenanceOptions? options = null)
    {
        _lisbeth = lisbeth ?? throw new ArgumentNullException(nameof(lisbeth));
        _options = options ?? new MaintenanceOptions();
        _options.Validate();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Maintenance Check";
    public ActivityCategory Category => ActivityCategory.Utility;
    public bool IsComplete => _complete;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ff14bot.Core.Player is not null && !ff14bot.Behavior.CommonBehaviors.IsLoading);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var freeSlots = InventoryManager.FreeSlots;
        if (freeSlots < _options.MinimumFreeInventorySlots)
        {
            return ExecutionResult.Block(
                $"Inventory safety gate: {freeSlots} free slots; {_options.MinimumFreeInventorySlots} required. Clear inventory before continuing.");
        }

        var repairNeeded = InventoryManager.EquippedItems.Any(item =>
            item.Item is not null &&
            item.Item.RepairItemId != 0 &&
            item.Condition < _options.RepairBelowPercent);

        if (repairNeeded)
        {
            if (_repairAttempted)
            {
                return ExecutionResult.Retry("Durability is still below the configured threshold after a repair attempt.", TimeSpan.FromSeconds(2));
            }

            _repairAttempted = true;
            var repaired = await _lisbeth.SelfRepairAsync(_options.AllowMenderFallback, cancellationToken).ConfigureAwait(false);
            if (!repaired)
            {
                return ExecutionResult.Block("Repair is required, but Lisbeth repair is unavailable or failed. Repair manually or restore Lisbeth integration.");
            }

            return ExecutionResult.Continue("Repair completed; rechecking durability.");
        }

        if (_options.RequireWellFed && !ff14bot.Core.Player.HasAura(WellFedAuraId))
        {
            var food = InventoryManager.FilledSlots.FirstOrDefault(slot => slot.RawItemId == _options.FoodItemId);
            if (food is null)
            {
                return ExecutionResult.Block($"Well Fed is required, but food item {_options.FoodItemId} is not present in inventory.");
            }

            if (_foodAttempted)
            {
                return ExecutionResult.Retry("Food was used but Well Fed is not detected yet.", TimeSpan.FromSeconds(1));
            }

            _foodAttempted = true;
            if (!food.UseItem())
            {
                return ExecutionResult.Retry($"Could not use food item {_options.FoodItemId} yet.", TimeSpan.FromSeconds(1));
            }

            return ExecutionResult.Continue("Food consumed; waiting for Well Fed confirmation.");
        }

        _complete = true;
        return ExecutionResult.Complete($"Maintenance ready: {freeSlots} free inventory slots, durability acceptable{(_options.RequireWellFed ? ", Well Fed active" : string.Empty)}.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

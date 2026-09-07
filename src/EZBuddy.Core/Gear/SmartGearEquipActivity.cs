using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Gear;

public sealed record GearEquipInstruction(
    GearEquipRequest Request,
    string DisplayName,
    GearStorageLocation SourceLocation,
    string Reason);

public sealed record SmartGearEquipPlan(
    IReadOnlyList<GearEquipInstruction> Instructions,
    IReadOnlyList<string> Warnings)
{
    public bool HasWork => Instructions.Count > 0;
}

public static class SmartGearEquipPlanner
{
    private static readonly HashSet<GearStorageLocation> AutoEquipLocations =
    [
        GearStorageLocation.Inventory,
        GearStorageLocation.ArmoryChest
    ];

    public static SmartGearEquipPlan Build(GearPlan gearPlan)
    {
        ArgumentNullException.ThrowIfNull(gearPlan);

        var instructions = new List<GearEquipInstruction>();
        var warnings = new List<string>();
        var bestEquippedScoreBySlot = gearPlan.Recommendations
            .Where(recommendation => recommendation.Item.IsEquipped)
            .GroupBy(recommendation => recommendation.Item.Slot)
            .ToDictionary(
                group => group.Key,
                group => group.Max(recommendation => recommendation.Score));

        foreach (var recommendation in gearPlan.Recommendations
                     .Where(recommendation => recommendation.Disposition == GearDisposition.EquipBest)
                     .OrderBy(recommendation => recommendation.Item.Slot)
                     .ThenByDescending(recommendation => recommendation.Item.ItemLevel)
                     .ThenBy(recommendation => recommendation.Item.ItemId))
        {
            var item = recommendation.Item;
            if (item.IsEquipped)
            {
                continue;
            }

            if (item.Slot == GearSlot.Ring)
            {
                warnings.Add(
                    $"Ring upgrade '{item.Name}' was not queued automatically because ring placement requires choosing Ring1 vs Ring2 safely.");
                continue;
            }

            if (bestEquippedScoreBySlot.TryGetValue(item.Slot, out var equippedScore) &&
                recommendation.Score <= equippedScore)
            {
                warnings.Add(
                    $"'{item.Name}' was not queued because it does not improve the current equipped {item.Slot} score.");
                continue;
            }

            if (!AutoEquipLocations.Contains(item.Location))
            {
                warnings.Add(
                    $"Upgrade '{item.Name}' is stored in {item.Location}; automatic equip only uses Inventory or Armory Chest items.");
                continue;
            }

            var request = new GearEquipRequest(
                item.ItemId,
                item.Slot,
                item.ItemLevel,
                item.IsHighQuality);
            request.Validate();

            instructions.Add(new GearEquipInstruction(
                request,
                item.Name,
                item.Location,
                recommendation.Reason));
        }

        return new SmartGearEquipPlan(
            instructions,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public sealed class SmartGearEquipActivity : IEZActivity
{
    private readonly IGearEquipmentAdapter _adapter;
    private readonly SmartGearEquipPlan _plan;
    private int _nextInstructionIndex;
    private bool _complete;

    public SmartGearEquipActivity(
        IGearEquipmentAdapter adapter,
        SmartGearEquipPlan plan)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _complete = _plan.Instructions.Count == 0;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name => "Smart Gear Auto-Equip";
    public ActivityCategory Category => ActivityCategory.Utility;
    public bool IsComplete => _complete;
    public int AppliedCount => _nextInstructionIndex;
    public IReadOnlyList<string> Warnings => _plan.Warnings;

    public async Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_complete)
        {
            return true;
        }

        var status = await _adapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Health == AdapterHealth.Ready;
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_complete || _nextInstructionIndex >= _plan.Instructions.Count)
        {
            _complete = true;
            return ExecutionResult.Complete(BuildCompletionMessage());
        }

        var status = await _adapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Health == AdapterHealth.Busy)
        {
            return ExecutionResult.Yield($"Smart Gear is waiting for a safe equipment window. {status.Message}");
        }

        if (status.Health != AdapterHealth.Ready)
        {
            return ExecutionResult.Block($"Smart Gear equipment bridge is not ready: {status.Message}");
        }

        var instruction = _plan.Instructions[_nextInstructionIndex];
        var equipped = await _adapter.EquipAsync(instruction.Request, cancellationToken).ConfigureAwait(false);
        if (!equipped)
        {
            return ExecutionResult.Retry(
                $"Could not equip {instruction.DisplayName}; the owned item or destination slot may have changed.",
                TimeSpan.FromSeconds(2));
        }

        _nextInstructionIndex++;
        if (_nextInstructionIndex >= _plan.Instructions.Count)
        {
            _complete = true;
            return ExecutionResult.Complete(BuildCompletionMessage());
        }

        return ExecutionResult.Continue(
            $"Equipped {instruction.DisplayName}. {_plan.Instructions.Count - _nextInstructionIndex} Smart Gear upgrade(s) remain.");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private string BuildCompletionMessage()
    {
        var warningSuffix = _plan.Warnings.Count == 0
            ? string.Empty
            : $" {_plan.Warnings.Count} item(s) require review.";
        return $"Smart Gear auto-equip complete. {_nextInstructionIndex} upgrade(s) applied.{warningSuffix}";
    }
}

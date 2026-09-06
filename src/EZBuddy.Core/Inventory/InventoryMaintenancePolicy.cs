namespace EZBuddy.Core.Inventory;

public enum InventoryMaintenanceAction
{
    None,
    Repair,
    ExtractMateria,
    Desynthesize
}

public sealed record InventoryItemSnapshot(
    uint ItemId,
    string Name,
    int ItemLevel,
    int DurabilityPercent,
    int SpiritbondPercent,
    bool CanRepair,
    bool CanExtractMateria,
    bool CanDesynthesize,
    bool IsEquipped,
    bool IsProtected,
    bool IsHighQuality = false,
    int MateriaCount = 0);

public sealed record InventoryMaintenanceSettings(
    int RepairBelowDurabilityPercent = 30,
    int ExtractMateriaAtSpiritbondPercent = 100,
    int MaximumDesynthesisItemLevel = 0,
    bool AllowMateriaExtraction = true,
    bool AllowDesynthesis = false,
    IReadOnlySet<uint>? DesynthesisAllowlist = null,
    IReadOnlySet<uint>? ProtectedItemIds = null)
{
    public IReadOnlySet<uint> EffectiveDesynthesisAllowlist => DesynthesisAllowlist ?? new HashSet<uint>();
    public IReadOnlySet<uint> EffectiveProtectedItemIds => ProtectedItemIds ?? new HashSet<uint>();

    public void Validate()
    {
        if (RepairBelowDurabilityPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(RepairBelowDurabilityPercent));
        }

        if (ExtractMateriaAtSpiritbondPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ExtractMateriaAtSpiritbondPercent));
        }

        if (MaximumDesynthesisItemLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDesynthesisItemLevel));
        }
    }
}

public sealed record InventoryMaintenanceDecision(
    InventoryItemSnapshot Item,
    InventoryMaintenanceAction Action,
    string Reason);

public static class InventoryMaintenancePolicy
{
    public static IReadOnlyList<InventoryMaintenanceDecision> Evaluate(
        IEnumerable<InventoryItemSnapshot> items,
        InventoryMaintenanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var decisions = new List<InventoryMaintenanceDecision>();
        foreach (var item in items)
        {
            if (settings.EffectiveProtectedItemIds.Contains(item.ItemId) || item.IsProtected)
            {
                decisions.Add(new InventoryMaintenanceDecision(item, InventoryMaintenanceAction.None, "Protected item; no automatic maintenance action allowed."));
                continue;
            }

            if (item.IsEquipped && item.CanRepair && item.DurabilityPercent < settings.RepairBelowDurabilityPercent)
            {
                decisions.Add(new InventoryMaintenanceDecision(
                    item,
                    InventoryMaintenanceAction.Repair,
                    $"Equipped item durability is {item.DurabilityPercent}%; threshold is {settings.RepairBelowDurabilityPercent}%."));
                continue;
            }

            if (settings.AllowMateriaExtraction &&
                item.CanExtractMateria &&
                item.SpiritbondPercent >= settings.ExtractMateriaAtSpiritbondPercent)
            {
                decisions.Add(new InventoryMaintenanceDecision(
                    item,
                    InventoryMaintenanceAction.ExtractMateria,
                    $"Spiritbond is {item.SpiritbondPercent}%; extraction threshold is {settings.ExtractMateriaAtSpiritbondPercent}%."));
                continue;
            }

            if (settings.AllowDesynthesis &&
                item.CanDesynthesize &&
                !item.IsEquipped &&
                item.MateriaCount == 0 &&
                item.ItemLevel <= settings.MaximumDesynthesisItemLevel &&
                settings.EffectiveDesynthesisAllowlist.Contains(item.ItemId))
            {
                decisions.Add(new InventoryMaintenanceDecision(
                    item,
                    InventoryMaintenanceAction.Desynthesize,
                    $"Item is explicitly allowlisted for desynthesis at item level {item.ItemLevel}."));
                continue;
            }

            decisions.Add(new InventoryMaintenanceDecision(item, InventoryMaintenanceAction.None, "No enabled maintenance rule applies."));
        }

        return decisions;
    }
}

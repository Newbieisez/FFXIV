using EZBuddy.Core.Inventory;

namespace EZBuddy.Core.Tests;

public sealed class InventoryMaintenancePolicyTests
{
    [Fact]
    public void ProtectedItemsNeverReceiveAutomaticActions()
    {
        var item = new InventoryItemSnapshot(
            100,
            "Protected Sword",
            10,
            DurabilityPercent: 1,
            SpiritbondPercent: 100,
            CanRepair: true,
            CanExtractMateria: true,
            CanDesynthesize: true,
            IsEquipped: true,
            IsProtected: true);

        var decision = Assert.Single(InventoryMaintenancePolicy.Evaluate([item], new InventoryMaintenanceSettings(AllowDesynthesis: true, MaximumDesynthesisItemLevel: 999, DesynthesisAllowlist: new HashSet<uint>([100]))));

        Assert.Equal(InventoryMaintenanceAction.None, decision.Action);
    }

    [Fact]
    public void LowDurabilityEquippedGearIsRepairedBeforeOtherActions()
    {
        var item = new InventoryItemSnapshot(
            200,
            "Worn Chest",
            100,
            DurabilityPercent: 20,
            SpiritbondPercent: 100,
            CanRepair: true,
            CanExtractMateria: true,
            CanDesynthesize: false,
            IsEquipped: true,
            IsProtected: false);

        var decision = Assert.Single(InventoryMaintenancePolicy.Evaluate([item], new InventoryMaintenanceSettings(RepairBelowDurabilityPercent: 30)));

        Assert.Equal(InventoryMaintenanceAction.Repair, decision.Action);
    }

    [Fact]
    public void DesynthesisRequiresExplicitAllowlistAndOptIn()
    {
        var item = new InventoryItemSnapshot(
            300,
            "Old Gear",
            50,
            DurabilityPercent: 100,
            SpiritbondPercent: 0,
            CanRepair: false,
            CanExtractMateria: false,
            CanDesynthesize: true,
            IsEquipped: false,
            IsProtected: false);

        var disabled = Assert.Single(InventoryMaintenancePolicy.Evaluate([item], new InventoryMaintenanceSettings(MaximumDesynthesisItemLevel: 100, DesynthesisAllowlist: new HashSet<uint>([300]))));
        var enabled = Assert.Single(InventoryMaintenancePolicy.Evaluate([item], new InventoryMaintenanceSettings(AllowDesynthesis: true, MaximumDesynthesisItemLevel: 100, DesynthesisAllowlist: new HashSet<uint>([300]))));

        Assert.Equal(InventoryMaintenanceAction.None, disabled.Action);
        Assert.Equal(InventoryMaintenanceAction.Desynthesize, enabled.Action);
    }
}

namespace EZBuddy.Core.Loot;

public enum SmartLootAction
{
    Greed,
    Pass,
    ManualReview
}

public sealed record LootItemSnapshot(
    uint ItemId,
    string Name,
    bool IsUpgrade = false,
    bool IsMissingCollection = false,
    bool IsProtected = false,
    bool CanExpertDelivery = false,
    bool CanDesynthesize = false,
    bool IsRare = false,
    int EstimatedValue = 0);

public sealed record SmartLootSettings(
    int FreeInventorySlots,
    int MinimumFreeInventorySlots,
    bool GreedUpgrades = true,
    bool GreedMissingCollections = true,
    bool GreedExpertDeliveryItems = true,
    bool GreedDesynthesisItems = false,
    int MinimumValueToGreed = 0)
{
    public void Validate()
    {
        if (FreeInventorySlots is < 0 or > 140 || MinimumFreeInventorySlots is < 0 or > 140 || MinimumValueToGreed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FreeInventorySlots));
        }
    }
}

public sealed record SmartLootDecision(
    LootItemSnapshot Item,
    SmartLootAction Action,
    string Reason);

public static class SmartLootPolicy
{
    public static SmartLootDecision Decide(LootItemSnapshot item, SmartLootSettings settings)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var inventoryTight = settings.FreeInventorySlots <= settings.MinimumFreeInventorySlots;
        var highPriority = item.IsProtected ||
                           (settings.GreedUpgrades && item.IsUpgrade) ||
                           (settings.GreedMissingCollections && item.IsMissingCollection) ||
                           item.IsRare;

        if (inventoryTight && highPriority)
        {
            return new SmartLootDecision(item, SmartLootAction.ManualReview,
                "Inventory is at the configured floor, but the item is an upgrade, missing collection, rare, or protected; do not silently pass it.");
        }

        if (inventoryTight)
        {
            return new SmartLootDecision(item, SmartLootAction.Pass,
                "Inventory is at or below the configured free-slot floor and the item is not high priority.");
        }

        if (settings.GreedUpgrades && item.IsUpgrade)
        {
            return new SmartLootDecision(item, SmartLootAction.Greed, "Potential gear upgrade.");
        }

        if (settings.GreedMissingCollections && item.IsMissingCollection)
        {
            return new SmartLootDecision(item, SmartLootAction.Greed, "Missing collection item.");
        }

        if (item.IsRare)
        {
            return new SmartLootDecision(item, SmartLootAction.Greed, "Rare item protection rule.");
        }

        if (settings.GreedExpertDeliveryItems && item.CanExpertDelivery)
        {
            return new SmartLootDecision(item, SmartLootAction.Greed, "Useful for approved Grand Company Expert Delivery.");
        }

        if (settings.GreedDesynthesisItems && item.CanDesynthesize)
        {
            return new SmartLootDecision(item, SmartLootAction.Greed, "Useful for an enabled desynthesis workflow.");
        }

        if (item.EstimatedValue >= settings.MinimumValueToGreed && settings.MinimumValueToGreed > 0)
        {
            return new SmartLootDecision(item, SmartLootAction.Greed,
                $"Estimated value {item.EstimatedValue:N0} meets the configured greed floor of {settings.MinimumValueToGreed:N0}.");
        }

        return new SmartLootDecision(item, SmartLootAction.Pass, "No enabled keep/greed rule applies.");
    }
}

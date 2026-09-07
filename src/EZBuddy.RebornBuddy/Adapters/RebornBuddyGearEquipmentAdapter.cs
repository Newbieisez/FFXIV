using Buddy.Coroutines;
using EZBuddy.Core.Adapters;
using EZBuddy.Core.Gear;
using ff14bot.Enums;
using ff14bot.Managers;
using ff14bot.Objects;

namespace EZBuddy.RebornBuddy.Adapters;

/// <summary>
/// Non-destructive RebornBuddy equipment bridge. Only moves an already-owned compatible item from
/// Inventory/Armory into an equipment slot. Rings are intentionally excluded until Ring1/Ring2
/// replacement can be proven safe for duplicate-item cases.
/// </summary>
public sealed class RebornBuddyGearEquipmentAdapter : IGearEquipmentAdapter
{
    private static readonly TimeSpan EquipConfirmationTimeout = TimeSpan.FromSeconds(3);

    public string Key => "rebornbuddy-gear-equipment";
    public string DisplayName => "RebornBuddy Gear Equipment";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ff14bot.Core.Player is null || ff14bot.Behavior.CommonBehaviors.IsLoading)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Busy,
                "A loaded character is required before equipment changes.",
                DateTimeOffset.UtcNow));
        }

        if (ff14bot.Core.Me.InCombat)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Busy,
                "Equipment changes are paused while the character is in combat.",
                DateTimeOffset.UtcNow));
        }

        if (ff14bot.Core.Me.IsCasting)
        {
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Busy,
                "Equipment changes are paused while the character is casting.",
                DateTimeOffset.UtcNow));
        }

        return Task.FromResult(new AdapterStatus(
            Key,
            DisplayName,
            AdapterHealth.Ready,
            "Owned Inventory/Armory upgrades can be equipped. Ring placement remains review-only.",
            DateTimeOffset.UtcNow));
    }

    public async Task<bool> EquipAsync(
        GearEquipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Slot == GearSlot.Ring ||
            ff14bot.Core.Player is null ||
            ff14bot.Behavior.CommonBehaviors.IsLoading ||
            ff14bot.Core.Me.InCombat ||
            ff14bot.Core.Me.IsCasting)
        {
            return false;
        }

        if (!TryMapEquipmentSlot(request.Slot, out var equipmentSlot))
        {
            return false;
        }

        var equippedBag = InventoryManager.GetBagByInventoryBagId(InventoryBagId.EquippedItems);
        var current = equippedBag[equipmentSlot];
        if (Matches(current, request))
        {
            return true;
        }

        var candidate = InventoryManager.FilledInventoryAndArmory
            .Where(slot => slot.IsFilled && slot.BagId != InventoryBagId.EquippedItems)
            .Where(slot => slot.RawItemId == request.ItemId)
            .Where(slot => slot.Item.IsValidForCurrentClass)
            .Where(slot => request.ExpectedItemLevel == 0 || checked((int)slot.Item.ItemLevel) == request.ExpectedItemLevel)
            .Where(slot => slot.IsHighQuality == request.IsHighQuality)
            .OrderByDescending(slot => slot.Item.ItemLevel)
            .FirstOrDefault();

        if (candidate is null)
        {
            return false;
        }

        candidate.Move(current);

        var deadline = DateTimeOffset.UtcNow + EquipConfirmationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Coroutine.Yield();

            var refreshed = InventoryManager
                .GetBagByInventoryBagId(InventoryBagId.EquippedItems)[equipmentSlot];
            if (Matches(refreshed, request))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(BagSlot slot, GearEquipRequest request)
    {
        if (!slot.IsFilled || slot.RawItemId != request.ItemId || slot.IsHighQuality != request.IsHighQuality)
        {
            return false;
        }

        return request.ExpectedItemLevel == 0 ||
               checked((int)slot.Item.ItemLevel) == request.ExpectedItemLevel;
    }

    private static bool TryMapEquipmentSlot(GearSlot gearSlot, out EquipmentSlot equipmentSlot)
    {
        switch (gearSlot)
        {
            case GearSlot.Weapon:
                equipmentSlot = EquipmentSlot.MainHand;
                return true;
            case GearSlot.OffHand:
                equipmentSlot = EquipmentSlot.OffHand;
                return true;
            case GearSlot.Head:
                equipmentSlot = EquipmentSlot.Head;
                return true;
            case GearSlot.Body:
                equipmentSlot = EquipmentSlot.Body;
                return true;
            case GearSlot.Hands:
                equipmentSlot = EquipmentSlot.Hands;
                return true;
            case GearSlot.Legs:
                equipmentSlot = EquipmentSlot.Legs;
                return true;
            case GearSlot.Feet:
                equipmentSlot = EquipmentSlot.Feet;
                return true;
            case GearSlot.Earrings:
                equipmentSlot = EquipmentSlot.Earring;
                return true;
            case GearSlot.Necklace:
                equipmentSlot = EquipmentSlot.Necklace;
                return true;
            case GearSlot.Bracelets:
                equipmentSlot = EquipmentSlot.Bracelet;
                return true;
            default:
                equipmentSlot = default;
                return false;
        }
    }
}

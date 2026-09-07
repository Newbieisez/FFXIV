using EZBuddy.Core.Collections;
using EZBuddy.Core.Economy;
using EZBuddy.Core.Gear;
using EZBuddy.Core.Materia;
using EZBuddy.Core.Procurement;
using EZBuddy.Core.Product;
using EZBuddy.Core.Settings;
using ff14bot.Enums;
using ff14bot.Managers;
using ff14bot.Objects;

namespace EZBuddy.RebornBuddy.Product;

public sealed class RebornBuddyProductSnapshotCollector : IProductSnapshotCollector
{
    public Task<ProductSnapshotBundle> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ff14bot.Core.Player is null || ff14bot.Behavior.CommonBehaviors.IsLoading)
        {
            throw new InvalidOperationException("A loaded character is required before Product Snapshot capture.");
        }

        var characterKey = SettingsPathSanitizer.Sanitize(ff14bot.Core.Player.Name);
        var jobKey = ff14bot.Core.Me.CurrentJob.ToString();
        var gear = CaptureCurrentJobGear(jobKey, cancellationToken);
        var ownedItems = CaptureOwnedInventory(cancellationToken);

        return Task.FromResult(new ProductSnapshotBundle(
            characterKey,
            DateTimeOffset.UtcNow,
            gear,
            Array.Empty<CurrencySnapshot>(),
            Array.Empty<CollectionItemSnapshot>(),
            Array.Empty<MateriaStock>(),
            ownedItems,
            Array.Empty<ProcurementRecipe>(),
            Array.Empty<ProcurementSource>()));
    }

    private static IReadOnlyList<GearItemSnapshot> CaptureCurrentJobGear(
        string jobKey,
        CancellationToken cancellationToken)
    {
        var output = new List<GearItemSnapshot>();
        var equippedBag = InventoryManager.GetBagByInventoryBagId(InventoryBagId.EquippedItems);

        AddEquipped(output, equippedBag[EquipmentSlot.MainHand], GearSlot.Weapon, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.OffHand], GearSlot.OffHand, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Head], GearSlot.Head, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Body], GearSlot.Body, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Hands], GearSlot.Hands, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Legs], GearSlot.Legs, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Feet], GearSlot.Feet, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Earring], GearSlot.Earrings, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Necklace], GearSlot.Necklace, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Bracelet], GearSlot.Bracelets, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Ring1], GearSlot.Ring, jobKey);
        AddEquipped(output, equippedBag[EquipmentSlot.Ring2], GearSlot.Ring, jobKey);

        foreach (var slot in InventoryManager.FilledInventoryAndArmory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!slot.IsFilled || slot.BagId == InventoryBagId.EquippedItems || !slot.Item.IsValidForCurrentClass)
            {
                continue;
            }

            var gearSlot = TryMapGearSlot(slot);
            if (!gearSlot.HasValue)
            {
                continue;
            }

            output.Add(ToSnapshot(
                slot,
                gearSlot.Value,
                jobKey,
                IsArmoryBag(slot.BagId) ? GearStorageLocation.ArmoryChest : GearStorageLocation.Inventory,
                isEquipped: false));
        }

        return output;
    }

    private static IReadOnlyDictionary<uint, int> CaptureOwnedInventory(CancellationToken cancellationToken)
    {
        var owned = new Dictionary<uint, int>();
        foreach (var slot in InventoryManager.FilledSlots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!slot.IsFilled || slot.RawItemId == 0)
            {
                continue;
            }

            var quantity = checked((int)slot.Count);
            owned[slot.RawItemId] = owned.GetValueOrDefault(slot.RawItemId) + quantity;
        }

        return owned;
    }

    private static void AddEquipped(
        ICollection<GearItemSnapshot> output,
        BagSlot slot,
        GearSlot gearSlot,
        string jobKey)
    {
        if (!slot.IsFilled)
        {
            return;
        }

        output.Add(ToSnapshot(
            slot,
            gearSlot,
            jobKey,
            GearStorageLocation.Equipped,
            isEquipped: true));
    }

    private static GearItemSnapshot ToSnapshot(
        BagSlot slot,
        GearSlot gearSlot,
        string jobKey,
        GearStorageLocation location,
        bool isEquipped)
        => new(
            slot.RawItemId,
            slot.Item.CurrentLocaleName,
            gearSlot,
            checked((int)slot.Item.ItemLevel),
            [jobKey],
            location,
            Stats: null,
            IsEquipped: isEquipped,
            IsProtected: isEquipped,
            CanExpertDelivery: false,
            CanDesynthesize: false,
            CanSell: false,
            IsUnique: false,
            IsHighQuality: slot.IsHighQuality,
            MateriaCount: 0);

    private static GearSlot? TryMapGearSlot(BagSlot slot)
    {
        var byBag = slot.BagId switch
        {
            InventoryBagId.Armory_MainHand => GearSlot.Weapon,
            InventoryBagId.Armory_OffHand => GearSlot.OffHand,
            InventoryBagId.Armory_Helmet => GearSlot.Head,
            InventoryBagId.Armory_Chest => GearSlot.Body,
            InventoryBagId.Armory_Glove => GearSlot.Hands,
            InventoryBagId.Armory_Pants => GearSlot.Legs,
            InventoryBagId.Armory_Boots => GearSlot.Feet,
            InventoryBagId.Armory_Earrings => GearSlot.Earrings,
            InventoryBagId.Armory_Necklace => GearSlot.Necklace,
            InventoryBagId.Armory_Writs => GearSlot.Bracelets,
            InventoryBagId.Armory_Rings => GearSlot.Ring,
            _ => (GearSlot?)null
        };

        if (byBag.HasValue)
        {
            return byBag;
        }

        return slot.Item.EquipmentCatagory switch
        {
            ItemUiCategory.Head => GearSlot.Head,
            ItemUiCategory.Body => GearSlot.Body,
            ItemUiCategory.Hands => GearSlot.Hands,
            ItemUiCategory.Legs => GearSlot.Legs,
            ItemUiCategory.Feet => GearSlot.Feet,
            ItemUiCategory.Earrings => GearSlot.Earrings,
            ItemUiCategory.Necklace => GearSlot.Necklace,
            ItemUiCategory.Bracelets => GearSlot.Bracelets,
            ItemUiCategory.Ring => GearSlot.Ring,
            _ => null
        };
    }

    private static bool IsArmoryBag(InventoryBagId bagId)
        => bagId is
            InventoryBagId.Armory_MainHand or
            InventoryBagId.Armory_OffHand or
            InventoryBagId.Armory_Helmet or
            InventoryBagId.Armory_Chest or
            InventoryBagId.Armory_Glove or
            InventoryBagId.Armory_Belt or
            InventoryBagId.Armory_Pants or
            InventoryBagId.Armory_Boots or
            InventoryBagId.Armory_Earrings or
            InventoryBagId.Armory_Necklace or
            InventoryBagId.Armory_Writs or
            InventoryBagId.Armory_Rings;
}

using System.Reflection;
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
    private const string LlamaLocalPlayerExtensionsType = "LlamaLibrary.Extensions.LocalPlayerExtensions";
    private const string LlamaBagSlotExtensionsType = "LlamaLibrary.Extensions.BagSlotExtensions";

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
        var currencies = CaptureCurrencies();

        return Task.FromResult(new ProductSnapshotBundle(
            characterKey,
            DateTimeOffset.UtcNow,
            gear,
            currencies,
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
        var mainHand = equippedBag[EquipmentSlot.MainHand];
        var offHand = equippedBag[EquipmentSlot.OffHand];
        var mainHandCategory = mainHand.IsFilled ? mainHand.Item.EquipmentCatagory : (ItemUiCategory?)null;
        var offHandCategory = offHand.IsFilled ? offHand.Item.EquipmentCatagory : (ItemUiCategory?)null;

        AddEquipped(output, mainHand, GearSlot.Weapon, jobKey);
        AddEquipped(output, offHand, GearSlot.OffHand, jobKey);
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

            var gearSlot = TryMapGearSlot(slot, mainHandCategory, offHandCategory);
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

    private static IReadOnlyList<CurrencySnapshot> CaptureCurrencies()
    {
        if (!TryInvokeOptionalLlama(LlamaLocalPlayerExtensionsType, "GCSeals", [ff14bot.Core.Me], out var sealsValue) ||
            !TryInvokeOptionalLlama(LlamaLocalPlayerExtensionsType, "MaxGCSeals", [ff14bot.Core.Me], out var maxValue))
        {
            return Array.Empty<CurrencySnapshot>();
        }

        try
        {
            var current = Convert.ToInt32(sealsValue);
            var cap = Convert.ToInt32(maxValue);
            if (cap <= 0 || current < 0 || current > cap)
            {
                return Array.Empty<CurrencySnapshot>();
            }

            var safetyBuffer = cap > 1
                ? Math.Min(1000, Math.Max(1, cap / 10))
                : 0;

            return
            [
                new CurrencySnapshot(
                    "gc-seals",
                    "Grand Company Seals",
                    current,
                    cap,
                    ProjectedIncoming: 0,
                    SafetyBuffer: safetyBuffer)
            ];
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return Array.Empty<CurrencySnapshot>();
        }
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
            CanDesynthesize: slot.CanDesynthesize,
            CanSell: false,
            IsUnique: false,
            IsHighQuality: slot.IsHighQuality,
            MateriaCount: TryGetMateriaCount(slot));

    private static int TryGetMateriaCount(BagSlot slot)
    {
        if (!TryInvokeOptionalLlama(LlamaBagSlotExtensionsType, "MateriaCount", [slot], out var value))
        {
            return 0;
        }

        try
        {
            return Math.Clamp(Convert.ToInt32(value), 0, 5);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static GearSlot? TryMapGearSlot(
        BagSlot slot,
        ItemUiCategory? mainHandCategory,
        ItemUiCategory? offHandCategory)
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

        var category = slot.Item.EquipmentCatagory;
        var standardSlot = category switch
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
            _ => (GearSlot?)null
        };

        if (standardSlot.HasValue)
        {
            return standardSlot;
        }

        if (mainHandCategory.HasValue && category == mainHandCategory.Value)
        {
            return GearSlot.Weapon;
        }

        if (offHandCategory.HasValue && category == offHandCategory.Value)
        {
            return GearSlot.OffHand;
        }

        return null;
    }

    private static bool TryInvokeOptionalLlama(
        string typeName,
        string methodName,
        object?[] arguments,
        out object? result)
    {
        result = null;
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (type is null)
                {
                    continue;
                }

                var method = type
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    .FirstOrDefault(candidate => ParametersMatch(candidate.GetParameters(), arguments));
                if (method is null)
                {
                    return false;
                }

                result = method.Invoke(null, arguments);
                return true;
            }
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (MethodAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, object?[] arguments)
    {
        if (parameters.Length != arguments.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var argument = arguments[index];
            if (argument is null)
            {
                if (parameters[index].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[index].ParameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameters[index].ParameterType.IsInstanceOfType(argument))
            {
                return false;
            }
        }

        return true;
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

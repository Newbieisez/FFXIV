using EZBuddy.Core.Adapters;

namespace EZBuddy.Core.Gear;

public sealed record GearEquipRequest(
    uint ItemId,
    GearSlot Slot,
    int ExpectedItemLevel = 0,
    bool IsHighQuality = false)
{
    public void Validate()
    {
        if (ItemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ItemId));
        }

        if (ExpectedItemLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ExpectedItemLevel));
        }
    }
}

/// <summary>
/// Non-destructive equipment bridge. This contract can equip an owned item but has no API for
/// selling, desynthesizing, discarding, or Grand Company turn-ins.
/// </summary>
public interface IGearEquipmentAdapter : IEZAdapter
{
    Task<bool> EquipAsync(
        GearEquipRequest request,
        CancellationToken cancellationToken = default);
}

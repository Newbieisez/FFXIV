using EZBuddy.Core.Duties;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Duties;

public sealed class RebornBuddyDutyLevelingProgressProvider : IDutyLevelingProgressProvider
{
    public DutyLevelingProgress Read()
    {
        var player = ff14bot.Core.Player;
        return new DutyLevelingProgress(
            CurrentLevel: player?.ClassLevel ?? 0,
            FreeInventorySlots: checked((int)InventoryManager.FreeSlots));
    }
}

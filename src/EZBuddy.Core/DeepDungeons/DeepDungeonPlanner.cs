using EZBuddy.Core.Planning;

namespace EZBuddy.Core.DeepDungeons;

public sealed record DeepDungeonSnapshot(
    string DungeonKey, int CurrentFloor, int FloorSetEnd, int AetherpoolArm, int AetherpoolArmor,
    int FreeInventorySlots, int MinimumFreeInventorySlots, bool PassageOpen, bool CairnOfReturnNeeded,
    bool PartyMemberKO, bool BossFloor, IReadOnlyDictionary<string, int> Pomanders);

public sealed record DeepDungeonOptions(
    int MinimumAetherpool = 0,
    bool OpenSilverChestsWhenSafe = true,
    bool OpenBronzeChests = false,
    bool PrioritizeExitWhenInventoryTight = true);

public static class DeepDungeonPlanner
{
    public static IReadOnlyList<PlannedWork> Build(DeepDungeonSnapshot snapshot, DeepDungeonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new DeepDungeonOptions();
        if (string.IsNullOrWhiteSpace(snapshot.DungeonKey) || snapshot.CurrentFloor <= 0 || snapshot.FloorSetEnd < snapshot.CurrentFloor)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        var work = new List<PlannedWork>();
        if (snapshot.PartyMemberKO && snapshot.CairnOfReturnNeeded)
            return [new("revive", "Use Cairn of Return", 1000, PlanRisk.High, "A party member is KO and revival is required before ordinary progression.")];

        if (snapshot.BossFloor)
            return [new("boss", "Clear floor-set boss", 900, PlanRisk.High, "Boss floor detected; combat provider owns rotation/mechanics while the planner suppresses chest detours.")];

        var tight = snapshot.FreeInventorySlots <= snapshot.MinimumFreeInventorySlots;
        if (tight && options.PrioritizeExitWhenInventoryTight)
            work.Add(new("exit", "Prioritize Passage", 800, PlanRisk.Medium, "Inventory is at the configured safety floor; optional chest detours are suppressed."));

        work.Add(snapshot.PassageOpen
            ? new PlannedWork("passage", "Advance through Passage", 700, PlanRisk.Low, "Passage is open and advancing is the safest deterministic progress action.")
            : new PlannedWork("mobs", "Defeat required enemies", 600, PlanRisk.Medium, "Passage is not open; continue objective combat until the unlock condition is met."));

        var low = snapshot.AetherpoolArm < options.MinimumAetherpool || snapshot.AetherpoolArmor < options.MinimumAetherpool;
        if (!tight && options.OpenSilverChestsWhenSafe)
            work.Add(new("silver", "Consider silver chest", low ? 650 : 350, PlanRisk.Medium, low ? "Aetherpool is below the configured target." : "Silver chest is optional because Aetherpool target is already met."));
        if (!tight && options.OpenBronzeChests)
            work.Add(new("bronze", "Consider bronze chest", 200, PlanRisk.Medium, "Optional loot detour is enabled and inventory capacity is above the safety floor."));

        return work.OrderByDescending(x => x.Priority).ToArray();
    }
}

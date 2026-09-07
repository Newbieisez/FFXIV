using EZBuddy.Core.Planning;

namespace EZBuddy.Core.IslandSanctuary;

public sealed record IslandSnapshot(
    int FreeInventorySlots, int MinimumFreeInventorySlots, int ReadyCrops, int EmptyCropPlots,
    int ReadyPastureAnimals, int EmptyPastureSlots, int GranariesReady, int WorkshopDaysUnscheduled,
    bool WorkshopPlanVerified, bool HasRequiredSeeds, bool HasRequiredFeed);

public static class IslandSanctuaryPlanner
{
    public static IReadOnlyList<PlannedWork> Build(IslandSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var work = new List<PlannedWork>();
        var tight = snapshot.FreeInventorySlots <= snapshot.MinimumFreeInventorySlots;

        if (snapshot.ReadyCrops > 0)
            work.Add(new("crops-harvest", "Harvest ready crops", 800, tight ? PlanRisk.ManualReview : PlanRisk.Low,
                tight ? "Inventory is at the safety floor; harvesting may create overflow." : $"{snapshot.ReadyCrops} crop plot(s) are ready."));
        if (snapshot.EmptyCropPlots > 0)
            work.Add(new("crops-plant", "Plant empty crop plots", 600, snapshot.HasRequiredSeeds ? PlanRisk.Low : PlanRisk.ManualReview,
                snapshot.HasRequiredSeeds ? $"{snapshot.EmptyCropPlots} empty crop plot(s) can be replanted." : "Required seed stock is missing."));
        if (snapshot.ReadyPastureAnimals > 0)
            work.Add(new("pasture-collect", "Collect pasture leavings", 750, tight ? PlanRisk.ManualReview : PlanRisk.Low,
                tight ? "Inventory is at the safety floor." : $"{snapshot.ReadyPastureAnimals} pasture animal(s) have collectable leavings."));
        if (snapshot.EmptyPastureSlots > 0 && !snapshot.HasRequiredFeed)
            work.Add(new("pasture-feed", "Restore pasture feed stock", 500, PlanRisk.ManualReview, "Pasture capacity is available but required feed stock is missing."));
        if (snapshot.GranariesReady > 0)
            work.Add(new("granary", "Collect and redeploy granaries", 700, PlanRisk.Low, $"{snapshot.GranariesReady} granary expedition(s) are ready."));
        if (snapshot.WorkshopDaysUnscheduled > 0)
            work.Add(new("workshop", "Schedule island workshop", 650, snapshot.WorkshopPlanVerified ? PlanRisk.Low : PlanRisk.ManualReview,
                snapshot.WorkshopPlanVerified ? $"{snapshot.WorkshopDaysUnscheduled} unscheduled workshop day(s) can use the verified plan." : "Workshop days are unscheduled, but no verified production plan is configured."));

        return work.OrderByDescending(x => x.Priority).ToArray();
    }
}

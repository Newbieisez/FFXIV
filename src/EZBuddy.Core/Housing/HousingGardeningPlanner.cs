using EZBuddy.Core.Planning;

namespace EZBuddy.Core.Housing;

public enum HousingLotteryState { None, EntryAvailable, Entered, WonClaimRequired, LostRefundAvailable }

public sealed record GardenPlotSnapshot(
    string PlotKey, string CropName, bool Empty, bool ReadyToHarvest,
    bool NeedsWater, bool CanFertilize, bool SeedApproved, bool HarvestApproved);

public sealed record HousingSnapshot(
    HousingLotteryState LotteryState, DateTimeOffset? LotteryDeadlineUtc, IReadOnlyList<GardenPlotSnapshot> GardenPlots);

public static class HousingGardeningPlanner
{
    public static IReadOnlyList<PlannedWork> Build(HousingSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var work = new List<PlannedWork>();

        switch (snapshot.LotteryState)
        {
            case HousingLotteryState.EntryAvailable:
                work.Add(new("lottery-entry", "Review housing lottery entry", 1000, PlanRisk.ManualReview,
                    snapshot.LotteryDeadlineUtc is { } deadline ? $"Entry is available until {deadline:u}; purchasing remains a user-reviewed action." : "Lottery entry is available; purchasing remains user-reviewed."));
                break;
            case HousingLotteryState.WonClaimRequired:
                work.Add(new("lottery-claim", "Claim won housing plot", 1000, PlanRisk.ManualReview, "A won plot requires user-reviewed claim handling before the deadline."));
                break;
            case HousingLotteryState.LostRefundAvailable:
                work.Add(new("lottery-refund", "Retrieve housing lottery refund", 900, PlanRisk.Low, "A refund is available from a completed unsuccessful lottery entry."));
                break;
        }

        foreach (var plot in snapshot.GardenPlots)
        {
            if (plot.ReadyToHarvest)
                work.Add(new(plot.PlotKey + ":harvest", $"Harvest {plot.CropName}", 800,
                    plot.HarvestApproved ? PlanRisk.Low : PlanRisk.ManualReview,
                    plot.HarvestApproved ? "Crop is ready and harvesting is explicitly approved." : "Crop is ready, but harvesting is not approved."));
            else if (plot.NeedsWater)
                work.Add(new(plot.PlotKey + ":water", $"Water {plot.CropName}", 750, PlanRisk.Low, "Crop needs watering."));

            if (plot.Empty)
                work.Add(new(plot.PlotKey + ":plant", "Plant empty garden plot", 600,
                    plot.SeedApproved ? PlanRisk.Low : PlanRisk.ManualReview,
                    plot.SeedApproved ? "An approved seed plan exists for this plot." : "Plot is empty, but no approved seed is configured."));
            else if (plot.CanFertilize)
                work.Add(new(plot.PlotKey + ":fertilize", $"Fertilize {plot.CropName}", 300, PlanRisk.Low, "Fertilization is available as an optional maintenance action."));
        }

        return work.OrderByDescending(x => x.Priority).ToArray();
    }
}

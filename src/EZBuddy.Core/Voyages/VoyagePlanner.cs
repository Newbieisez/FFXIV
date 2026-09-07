using EZBuddy.Core.Planning;

namespace EZBuddy.Core.Voyages;

public sealed record VoyageVesselSnapshot(
    string VesselKey, string Name, bool IsDeployed, DateTimeOffset? ReturnAtUtc,
    int ConditionPercent, int MinimumConditionPercent, int RequiredFuel, int AvailableFuel,
    int RequiredRepairKits, int AvailableRepairKits, string? ConfiguredRouteKey, bool RouteVerified);

public static class VoyagePlanner
{
    public static IReadOnlyList<PlannedWork> Build(IEnumerable<VoyageVesselSnapshot> vessels, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(vessels);
        var work = new List<PlannedWork>();
        foreach (var v in vessels)
        {
            if (v.IsDeployed && v.ReturnAtUtc is { } returnAt && returnAt > nowUtc)
            {
                work.Add(new(v.VesselKey + ":wait", $"Wait for {v.Name}", 50, PlanRisk.Low, $"Vessel returns at {returnAt:u}."));
                continue;
            }
            if (v.ConditionPercent < v.MinimumConditionPercent || v.AvailableRepairKits < v.RequiredRepairKits)
            {
                work.Add(new(v.VesselKey + ":repair", $"Repair {v.Name}", 900, PlanRisk.ManualReview, "Condition or repair-kit stock is below the configured deployment floor."));
                continue;
            }
            if (v.AvailableFuel < v.RequiredFuel)
            {
                work.Add(new(v.VesselKey + ":fuel", $"Refuel {v.Name}", 850, PlanRisk.ManualReview, "Insufficient configured fuel is available for the next voyage."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(v.ConfiguredRouteKey) || !v.RouteVerified)
            {
                work.Add(new(v.VesselKey + ":route", $"Review route for {v.Name}", 800, PlanRisk.ManualReview, "No verified voyage route is configured."));
                continue;
            }
            work.Add(new(v.VesselKey + ":deploy", $"Deploy {v.Name}", 700, PlanRisk.Medium, $"Verified route '{v.ConfiguredRouteKey}' is ready and resource floors are satisfied."));
        }
        return work.OrderByDescending(x => x.Priority).ToArray();
    }
}

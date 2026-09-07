using EZBuddy.Core.Planning;

namespace EZBuddy.Core.FieldOperations;

public sealed record FieldEventSnapshot(
    string Key, string Name, int RewardScore, int EstimatedMinutes, int DangerScore,
    bool Active, bool Reachable, bool RequiresParty = false, bool IsCriticalEngagement = false);

public sealed record FieldOperationSnapshot(
    string ZoneKey, int ProgressValue, int ProgressFloorToProtect, bool HasRequiredLoadout,
    int PartySize, IReadOnlyList<FieldEventSnapshot> Events);

public static class FieldOperationPlanner
{
    public static IReadOnlyList<PlannedWork> Build(FieldOperationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.ZoneKey) || snapshot.ProgressValue < 0 || snapshot.ProgressFloorToProtect < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        if (!snapshot.HasRequiredLoadout)
            return [new("loadout", "Restore configured field-operation loadout", 1000, PlanRisk.ManualReview,
                "Required actions/items are not available; event automation is blocked until the loadout is validated.")];

        var protect = snapshot.ProgressValue <= snapshot.ProgressFloorToProtect;
        return snapshot.Events.Where(x => x.Active && x.Reachable).Select(x =>
        {
            var partyBlocked = x.RequiresParty && snapshot.PartySize <= 1;
            var score = (x.RewardScore * 100d) / Math.Max(1, x.EstimatedMinutes) - x.DangerScore * (protect ? 30 : 10);
            return new PlannedWork(x.Key, x.Name, (int)Math.Round(score),
                partyBlocked ? PlanRisk.ManualReview : x.IsCriticalEngagement ? PlanRisk.High : PlanRisk.Medium,
                partyBlocked ? "Event requires a party under the configured policy." : protect
                    ? "Progress protection is active, so high-danger events are heavily deprioritized."
                    : "Ranked by reward-per-minute with a danger penalty.");
        }).OrderByDescending(x => x.Priority).ToArray();
    }
}

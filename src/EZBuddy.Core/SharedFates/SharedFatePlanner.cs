using EZBuddy.Core.Planning;

namespace EZBuddy.Core.SharedFates;

public sealed record SharedFateRegionSnapshot(
    string RegionKey, string Name, int CompletedFates, int RequiredFates,
    bool HasActiveReachableFate, int EstimatedMinutesToNextCompletion, int Priority = 0);

public sealed record SharedFateSnapshot(
    int Gemstones, int GemstoneCap, int ProjectedGemstones, int SafetyBuffer,
    IReadOnlyList<SharedFateRegionSnapshot> Regions, bool HasApprovedSpendRule);

public static class SharedFatePlanner
{
    public static IReadOnlyList<PlannedWork> Build(SharedFateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Gemstones < 0 || snapshot.GemstoneCap <= 0 || snapshot.SafetyBuffer < 0 || snapshot.SafetyBuffer >= snapshot.GemstoneCap)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        var work = new List<PlannedWork>();
        var safeCeiling = snapshot.GemstoneCap - snapshot.SafetyBuffer;
        if (snapshot.Gemstones + snapshot.ProjectedGemstones > safeCeiling)
            work.Add(new("spend", "Protect Bicolor Gemstone cap", 1000,
                snapshot.HasApprovedSpendRule ? PlanRisk.Low : PlanRisk.ManualReview,
                snapshot.HasApprovedSpendRule
                    ? "Projected gemstone income exceeds the configured safe ceiling; execute only the approved spend rule."
                    : "Projected gemstone income exceeds the safe ceiling, but no approved spend rule exists."));

        work.AddRange(snapshot.Regions.Where(r => r.CompletedFates < r.RequiredFates && r.HasActiveReachableFate).Select(r =>
        {
            var remaining = r.RequiredFates - r.CompletedFates;
            var score = r.Priority * 100 + 1000 / Math.Max(1, remaining) + 100 / Math.Max(1, r.EstimatedMinutesToNextCompletion);
            return new PlannedWork(r.RegionKey, $"Advance Shared FATE: {r.Name}", score, PlanRisk.Medium,
                $"{remaining} completion(s) remain for the configured regional target.");
        }));

        return work.OrderByDescending(x => x.Priority).ToArray();
    }
}

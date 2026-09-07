using EZBuddy.Core.Planning;

namespace EZBuddy.Core.Levequests;

public enum LeveType { Craft, Gather, Combat }

public sealed record LeveOption(
    string Key, string Name, LeveType Type, int AllowanceCost, int RewardScore,
    int EstimatedMinutes, bool VerifiedExecutor, int Priority = 0);

public sealed record LeveSnapshot(
    int CurrentAllowances, int AllowanceCap, int PreserveAllowances,
    int BurnWhenAtOrAbove, int MaximumLevesThisRun, IReadOnlyList<LeveOption> Options);

public static class LevequestPlanner
{
    public static IReadOnlyList<PlannedWork> Build(LeveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.CurrentAllowances < 0 || snapshot.AllowanceCap <= 0 || snapshot.CurrentAllowances > snapshot.AllowanceCap ||
            snapshot.PreserveAllowances < 0 || snapshot.BurnWhenAtOrAbove < 0 || snapshot.MaximumLevesThisRun <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (snapshot.CurrentAllowances < snapshot.BurnWhenAtOrAbove) return Array.Empty<PlannedWork>();

        var spendable = Math.Max(0, snapshot.CurrentAllowances - snapshot.PreserveAllowances);
        return snapshot.Options.Where(x => x.AllowanceCost > 0 && x.AllowanceCost <= spendable).Select(x =>
            new PlannedWork(x.Key, x.Name,
                x.Priority * 100 + x.RewardScore * 100 / Math.Max(1, x.EstimatedMinutes),
                x.VerifiedExecutor ? PlanRisk.Low : PlanRisk.ManualReview,
                x.VerifiedExecutor
                    ? $"Allowance pressure is active; this verified {x.Type} leve is eligible for burn-down."
                    : "Leve is attractive but no verified execution provider is registered."))
            .OrderByDescending(x => x.Priority).Take(snapshot.MaximumLevesThisRun).ToArray();
    }
}

namespace EZBuddy.Core.Routines;

public enum AlliedSocietyQuestKind
{
    Combat,
    Craft,
    Gather,
    Delivery,
    Manual
}

public sealed record AlliedSocietyQuestSnapshot(
    string QuestKey,
    string Name,
    string SocietyKey,
    AlliedSocietyQuestKind Kind,
    int ReputationReward,
    int EstimatedMinutes,
    bool Accepted,
    bool Completed,
    bool VerifiedExecutor,
    bool RequiresCombat = false,
    int Priority = 0);

public sealed record AlliedSocietySnapshot(
    int DailyAllowancesRemaining,
    int MaximumQuestsThisRun,
    IReadOnlySet<string> EnabledSocieties,
    IReadOnlyList<AlliedSocietyQuestSnapshot> Quests);

public sealed record AlliedSocietyPlanStep(
    string QuestKey,
    string Name,
    AlliedSocietyQuestKind Kind,
    int Priority,
    bool CanAutomate,
    string Reason);

public static class AlliedSocietyPlanner
{
    public static IReadOnlyList<AlliedSocietyPlanStep> Build(AlliedSocietySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DailyAllowancesRemaining < 0 || snapshot.MaximumQuestsThisRun <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        var limit = Math.Min(snapshot.DailyAllowancesRemaining, snapshot.MaximumQuestsThisRun);
        if (limit <= 0) return Array.Empty<AlliedSocietyPlanStep>();

        return snapshot.Quests
            .Where(q => !q.Completed && snapshot.EnabledSocieties.Contains(q.SocietyKey))
            .Select(q => new AlliedSocietyPlanStep(
                q.QuestKey,
                q.Name,
                q.Kind,
                q.Priority * 100 + q.ReputationReward * 100 / Math.Max(1, q.EstimatedMinutes) + (q.Accepted ? 1000 : 0),
                q.VerifiedExecutor && q.Kind != AlliedSocietyQuestKind.Manual,
                q.VerifiedExecutor && q.Kind != AlliedSocietyQuestKind.Manual
                    ? q.Accepted ? "Already accepted; prioritize completion before taking new daily quests." : "Verified executor is available for this enabled society quest."
                    : "Quest remains visible for review because no verified executor is registered."))
            .OrderByDescending(step => step.Priority)
            .Take(limit)
            .ToArray();
    }
}

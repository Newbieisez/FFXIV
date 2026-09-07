using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class AlliedSocietyPlannerTests
{
    [Fact]
    public void Planner_PrioritizesAcceptedVerifiedQuestsAndRespectsDailyLimit()
    {
        var enabled = new HashSet<string>(["society-a"], StringComparer.OrdinalIgnoreCase);
        var snapshot = new AlliedSocietySnapshot(
            DailyAllowancesRemaining: 2,
            MaximumQuestsThisRun: 3,
            EnabledSocieties: enabled,
            Quests:
            [
                new AlliedSocietyQuestSnapshot("accepted", "Accepted Quest", "society-a", AlliedSocietyQuestKind.Combat, 60, 5, Accepted: true, Completed: false, VerifiedExecutor: true),
                new AlliedSocietyQuestSnapshot("new", "New Quest", "society-a", AlliedSocietyQuestKind.Craft, 60, 5, Accepted: false, Completed: false, VerifiedExecutor: true),
                new AlliedSocietyQuestSnapshot("disabled", "Other Society", "society-b", AlliedSocietyQuestKind.Combat, 999, 1, Accepted: false, Completed: false, VerifiedExecutor: true)
            ]);

        var plan = AlliedSocietyPlanner.Build(snapshot);

        Assert.Equal(2, plan.Count);
        Assert.Equal("accepted", plan[0].QuestKey);
        Assert.DoesNotContain(plan, step => step.QuestKey == "disabled");
    }

    [Fact]
    public void Planner_KeepsUnsupportedQuestVisibleButDoesNotClaimAutomation()
    {
        var enabled = new HashSet<string>(["society-a"], StringComparer.OrdinalIgnoreCase);
        var plan = AlliedSocietyPlanner.Build(new AlliedSocietySnapshot(
            1, 1, enabled,
            [new AlliedSocietyQuestSnapshot("manual", "Manual Quest", "society-a", AlliedSocietyQuestKind.Manual, 50, 10, false, false, false)]));

        var step = Assert.Single(plan);
        Assert.False(step.CanAutomate);
    }
}

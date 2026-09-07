using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class WondrousTailsPlannerTests
{
    [Fact]
    public void NoJournal_PlansPickupOnly()
    {
        var snapshot = new WondrousTailsJournalSnapshot(false, 0, 0, []);

        var plan = WondrousTailsPlanner.Build(snapshot);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(WondrousTailsPlanAction.PickUpJournal, step.Action);
    }

    [Fact]
    public void NineSeals_PlansTurnIn()
    {
        var snapshot = Snapshot(seals: 9, secondChance: 0, objectives: BuildObjectives(completedCount: 9));

        var plan = WondrousTailsPlanner.Build(snapshot);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(WondrousTailsPlanAction.TurnInJournal, step.Action);
    }

    [Fact]
    public void Planner_UsesOnlyVerifiedAutomationPaths()
    {
        var objectives = BuildObjectives(completedCount: 0).ToArray();
        objectives[0] = objectives[0] with { HasVerifiedEzBuddyRoute = true, Priority = 10 };
        objectives[1] = objectives[1] with { HasVerifiedOrderBotProfile = true, Priority = 5 };
        var snapshot = Snapshot(0, 0, objectives);

        var plan = WondrousTailsPlanner.Build(snapshot, new WondrousTailsPlannerOptions(MaximumDutiesThisRun: 2));

        Assert.Equal(2, plan.Steps.Count(step => step.Action == WondrousTailsPlanAction.RunObjective));
        Assert.DoesNotContain(plan.Steps, step => step.ObjectiveKey == "obj-03");
        Assert.Contains(plan.UnautomatableObjectives, value => value == "Objective 3");
    }

    [Fact]
    public void Planner_PrefersPriorityThenEstimatedDuration()
    {
        var objectives = BuildObjectives(completedCount: 0).ToArray();
        objectives[0] = objectives[0] with { HasVerifiedEzBuddyRoute = true, Priority = 10, EstimatedMinutes = 20 };
        objectives[1] = objectives[1] with { HasVerifiedEzBuddyRoute = true, Priority = 20, EstimatedMinutes = 50 };
        objectives[2] = objectives[2] with { HasVerifiedEzBuddyRoute = true, Priority = 20, EstimatedMinutes = 5 };
        var snapshot = Snapshot(8, 0, objectives);

        var plan = WondrousTailsPlanner.Build(snapshot, new WondrousTailsPlannerOptions(MaximumDutiesThisRun: 1));

        var step = Assert.Single(plan.Steps.Where(step => step.Action == WondrousTailsPlanAction.RunObjective));
        Assert.Equal("obj-03", step.ObjectiveKey);
    }

    [Fact]
    public void Retry_IsAdvisoryAndStopsFuturePlanningUntilRescan()
    {
        var objectives = BuildObjectives(completedCount: 16)
            .Select((objective, index) => index == 0
                ? objective with { HasVerifiedEzBuddyRoute = true, EstimatedMinutes = 2 }
                : objective)
            .ToArray();
        var snapshot = Snapshot(5, 1, objectives);

        var plan = WondrousTailsPlanner.Build(
            snapshot,
            new WondrousTailsPlannerOptions(AllowSecondChanceRetry: true));

        var retry = Assert.Single(plan.Steps.Where(step => step.Action == WondrousTailsPlanAction.UseRetry));
        Assert.Equal("obj-01", retry.ObjectiveKey);
        Assert.Contains("rescan", retry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShuffleAvailability_FollowsOfficialSealAndPointBounds()
    {
        var objectives = BuildObjectives(completedCount: 0);

        Assert.False(WondrousTailsPlanner.Build(Snapshot(2, 9, objectives)).ShuffleIsAvailable);
        Assert.True(WondrousTailsPlanner.Build(Snapshot(3, 2, objectives)).ShuffleIsAvailable);
        Assert.True(WondrousTailsPlanner.Build(Snapshot(7, 2, objectives)).ShuffleIsAvailable);
        Assert.False(WondrousTailsPlanner.Build(Snapshot(8, 9, objectives)).ShuffleIsAvailable);
        Assert.False(WondrousTailsPlanner.Build(Snapshot(5, 1, objectives)).ShuffleIsAvailable);
    }

    [Fact]
    public void CountLines_ComputesRowsColumnsAndDiagonals()
    {
        var grid = new bool[16];
        grid[0] = grid[1] = grid[2] = grid[3] = true;
        grid[4] = grid[8] = grid[12] = true;
        grid[5] = grid[10] = grid[15] = true;

        Assert.Equal(3, WondrousTailsPlanner.CountLines(grid));
    }

    [Fact]
    public void ActiveJournal_RequiresSixteenObjectives()
    {
        var snapshot = new WondrousTailsJournalSnapshot(true, 0, 0, []);

        Assert.Throws<InvalidDataException>(snapshot.Validate);
    }

    private static WondrousTailsJournalSnapshot Snapshot(
        int seals,
        int secondChance,
        IReadOnlyList<WondrousTailsObjectiveSnapshot> objectives)
        => new(true, seals, secondChance, objectives);

    private static IReadOnlyList<WondrousTailsObjectiveSnapshot> BuildObjectives(int completedCount)
        => Enumerable.Range(1, 16)
            .Select(index => new WondrousTailsObjectiveSnapshot(
                $"obj-{index:D2}",
                $"Objective {index}",
                Completed: index <= completedCount,
                EligibleQueueDutyIds: [(uint)(1000 + index)],
                HasVerifiedEzBuddyRoute: false,
                EstimatedMinutes: 10 + index))
            .ToArray();
}

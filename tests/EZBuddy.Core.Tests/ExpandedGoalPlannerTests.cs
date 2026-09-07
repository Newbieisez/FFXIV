using EZBuddy.Core.Goals;

namespace EZBuddy.Core.Tests;

public sealed class ExpandedGoalPlannerTests
{
    [Theory]
    [InlineData(GoalType.DeepDungeonProgress, "deep-dungeons")]
    [InlineData(GoalType.TreasureMaps, "treasure-hunts")]
    [InlineData(GoalType.FieldOperationProgress, "field-operations")]
    [InlineData(GoalType.SharedFateRanks, "shared-fates")]
    [InlineData(GoalType.IslandMaintenance, "island")]
    [InlineData(GoalType.VoyageManagement, "voyages")]
    [InlineData(GoalType.BurnLeveAllowances, "levequests")]
    [InlineData(GoalType.DomanEnclaveDonations, "doman-enclave")]
    [InlineData(GoalType.HousingAndGardening, "housing-gardening")]
    public void NewGoalTypes_ExposeTheirPrimaryPlanner(GoalType goalType, string capability)
    {
        var available = new HashSet<string>([capability], StringComparer.OrdinalIgnoreCase);
        var plan = GoalPlanner.Build(new GoalRequest(goalType, "test"), available);

        Assert.True(plan.CanStart);
        Assert.Contains(plan.Steps, step => step.CapabilityKey == capability && step.Available);
    }
}

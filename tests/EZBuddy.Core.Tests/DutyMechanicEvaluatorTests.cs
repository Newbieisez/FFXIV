using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Tests;

public sealed class DutyMechanicEvaluatorTests
{
    [Fact]
    public void MatchingAvoidRule_ProducesMovementPriorityDirective()
    {
        var profile = BuildProfile(
            new BossMechanicRule(
                "wide-aoe",
                BossMechanicKind.AvoidRadius,
                ActionId: 555,
                Radius: 12));
        var evaluator = new DutyMechanicEvaluator();

        var directive = evaluator.Evaluate(
            profile,
            new DutyMechanicObservation(BossNpcId: 100, CastActionId: 555));

        Assert.NotNull(directive);
        Assert.True(directive.RequiresMovementOverride);
        Assert.Equal("wide-aoe", directive.Rule.Id);
    }

    [Fact]
    public void UnmatchedCast_ProducesNoDirective()
    {
        var profile = BuildProfile(
            new BossMechanicRule(
                "known-cast",
                BossMechanicKind.Gaze,
                ActionId: 777));
        var evaluator = new DutyMechanicEvaluator();

        var directive = evaluator.Evaluate(
            profile,
            new DutyMechanicObservation(BossNpcId: 100, CastActionId: 999));

        Assert.Null(directive);
    }

    [Fact]
    public void GazeRule_DoesNotClaimMovementOwnership()
    {
        var profile = BuildProfile(
            new BossMechanicRule(
                "gaze",
                BossMechanicKind.Gaze,
                ActionId: 777));
        var evaluator = new DutyMechanicEvaluator();

        var directive = evaluator.Evaluate(
            profile,
            new DutyMechanicObservation(BossNpcId: 100, CastActionId: 777));

        Assert.NotNull(directive);
        Assert.False(directive.RequiresMovementOverride);
    }

    private static DutyNavigationProfile BuildProfile(BossMechanicRule rule)
        => new(
            DutyId: 1,
            Name: "Mechanic Test",
            MinimumLevel: 15,
            MaximumLevel: 100,
            Objectives:
            [
                new DutyObjectiveNode("start", DutyObjectiveKind.Waypoint, new DutyPoint(0, 0, 0))
            ],
            Bosses:
            [
                new BossMechanicProfile(100, "Test Boss", [rule])
            ]);
}

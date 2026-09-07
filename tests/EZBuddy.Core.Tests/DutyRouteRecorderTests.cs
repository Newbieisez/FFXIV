using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Tests;

public sealed class DutyRouteRecorderTests
{
    [Fact]
    public void MovementSampling_UsesDistanceAndHeadingThresholds()
    {
        var recorder = CreateRecorder();
        recorder.Sample(Frame(0, 0, 0, 0));
        recorder.Sample(Frame(1, 0, 0, 0));
        recorder.Sample(Frame(1.1f, 0, 0, DegreesToRadians(40)));
        recorder.Sample(Frame(5, 0, 0, DegreesToRadians(40)));

        var profile = recorder.BuildProfile();

        Assert.Equal(3, profile.Objectives.Count(node => node.Kind == DutyObjectiveKind.Waypoint));
    }

    [Fact]
    public void CombatTransition_AddsBossBoundaryOncePerEngagement()
    {
        var recorder = CreateRecorder();
        recorder.Sample(Frame(0, 0, 0, 0, inCombat: false));
        recorder.Sample(Frame(1, 0, 0, 0, inCombat: true));
        recorder.Sample(Frame(2, 0, 0, 0, inCombat: true));
        recorder.Sample(Frame(3, 0, 0, 0, inCombat: false));
        recorder.Sample(Frame(4, 0, 0, 0, inCombat: true));

        var profile = recorder.BuildProfile();

        Assert.Equal(2, profile.Objectives.Count(node => node.Kind == DutyObjectiveKind.BossBoundary));
    }

    [Fact]
    public void InteractionCapture_SnapsTargetAndDeduplicatesObject()
    {
        var recorder = CreateRecorder();
        var interaction = new DutyInteractionSnapshot(
            2001001,
            "Test Door",
            DutyObjectiveKind.Door,
            new DutyPoint(2, 0, 0),
            true,
            2f);

        recorder.Sample(Frame(0, 0, 0, 0) with { Interaction = interaction });
        recorder.Sample(Frame(0.2f, 0, 0, 0) with { Interaction = interaction });

        var profile = recorder.BuildProfile();
        var door = Assert.Single(profile.Objectives, node => node.Kind == DutyObjectiveKind.Door);
        Assert.Equal((uint)2001001, door.ObjectId);
    }

    [Fact]
    public void CastCapture_CreatesInertObservationScaffoldAndDeduplicates()
    {
        var recorder = CreateRecorder();
        var cast = new DutyCastSnapshot(
            999,
            "Test Boss",
            12345,
            2.5f,
            new DutyPoint(10, 0, 10),
            new DutyPoint(-2, 0, 1));

        recorder.Sample(Frame(0, 0, 0, 0) with { ActiveCasts = [cast] });
        recorder.Sample(Frame(1, 0, 0, 0) with { ActiveCasts = [cast] });

        var profile = recorder.BuildProfile();
        var boss = Assert.Single(profile.Bosses);
        var rule = Assert.Single(boss.Rules);
        Assert.Equal(BossMechanicKind.CustomProvider, rule.Kind);
        Assert.Equal("recorded-observation", rule.ProviderKey);
        Assert.Equal((uint)12345, rule.ActionId);
    }

    [Fact]
    public void TerritoryMismatch_FailsClosed()
    {
        var recorder = CreateRecorder();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            recorder.Sample(Frame(0, 0, 0, 0) with { TerritoryId = 999 }));

        Assert.Contains("territory mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DutyRouteRecorder CreateRecorder()
        => new(4, 1036, "Recorder Test", 15, 100);

    private static DutyRecorderFrame Frame(
        float x,
        float y,
        float z,
        float heading,
        bool inCombat = false)
        => new(
            1036,
            new DutyPoint(x, y, z),
            heading,
            inCombat,
            DateTimeOffset.UtcNow);

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}

using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Tests;

public sealed class DutyRouteQualityAnalyzerTests
{
    [Fact]
    public void CleanRoute_IsReadyForLiveVerification()
    {
        var profile = BuildProfile(
            territoryId: 837,
            objectives:
            [
                Node("n1", 0), Node("n2", 5), Node("n3", 10), Node("n4", 15),
                Node("n5", 20), Node("n6", 25), Node("n7", 30), Node("n8", 35)
            ]);

        var report = new DutyRouteQualityAnalyzer().Analyze(profile);

        Assert.True(report.ReadyForLiveVerification);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void MissingTerritory_IsBlockingError()
    {
        var profile = BuildProfile(
            territoryId: 0,
            objectives:
            [
                Node("n1", 0), Node("n2", 5), Node("n3", 10), Node("n4", 15),
                Node("n5", 20), Node("n6", 25), Node("n7", 30), Node("n8", 35)
            ]);

        var report = new DutyRouteQualityAnalyzer().Analyze(profile);

        Assert.False(report.ReadyForLiveVerification);
        Assert.Contains(report.Issues, issue => issue.Code == "TERRITORY_ID_MISSING" && issue.Severity == DutyRouteQualitySeverity.Error);
    }

    [Fact]
    public void LargeRouteGap_IsBlockingError()
    {
        var profile = BuildProfile(
            territoryId: 837,
            objectives:
            [
                Node("n1", 0), Node("n2", 5), Node("n3", 10), Node("n4", 15),
                Node("n5", 200), Node("n6", 205), Node("n7", 210), Node("n8", 215)
            ]);

        var report = new DutyRouteQualityAnalyzer().Analyze(profile);

        Assert.False(report.ReadyForLiveVerification);
        Assert.Contains(report.Issues, issue => issue.Code == "ROUTE_GAP_ERROR" && issue.ObjectiveId == "n5");
    }

    [Fact]
    public void RecorderObservations_AreWarningsUntilClassified()
    {
        var boss = new BossMechanicProfile(
            1234,
            "Boss",
            [new BossMechanicRule(
                "observed-55",
                BossMechanicKind.CustomProvider,
                ActionId: 55,
                ProviderKey: "recorded-observation")]);
        var profile = BuildProfile(
            territoryId: 837,
            objectives:
            [
                Node("n1", 0), Node("n2", 5), Node("n3", 10),
                new DutyObjectiveNode("boss", DutyObjectiveKind.BossBoundary, new DutyPoint(15, 0, 0)),
                Node("n5", 20), Node("n6", 25), Node("n7", 30), Node("n8", 35)
            ],
            bosses: [boss]);

        var report = new DutyRouteQualityAnalyzer().Analyze(profile);

        Assert.True(report.ReadyForLiveVerification);
        Assert.Equal(1, report.RecordedObservationCount);
        Assert.Contains(report.Issues, issue => issue.Code == "RECORDED_MECHANIC_UNVALIDATED" && issue.Severity == DutyRouteQualitySeverity.Warning);
    }

    [Fact]
    public void RequiredChest_AndUnverifiedInteraction_AreWarnings()
    {
        var profile = BuildProfile(
            territoryId: 837,
            objectives:
            [
                Node("n1", 0), Node("n2", 5), Node("n3", 10), Node("n4", 15),
                new DutyObjectiveNode("chest", DutyObjectiveKind.Chest, new DutyPoint(20, 0, 0), Required: true),
                new DutyObjectiveNode("door", DutyObjectiveKind.Door, new DutyPoint(25, 0, 0), ObjectId: 999, Notes: "Classification requires developer verification."),
                Node("n7", 30), Node("n8", 35)
            ]);

        var report = new DutyRouteQualityAnalyzer().Analyze(profile);

        Assert.True(report.ReadyForLiveVerification);
        Assert.Contains(report.Issues, issue => issue.Code == "REQUIRED_CHEST");
        Assert.Contains(report.Issues, issue => issue.Code == "UNVERIFIED_CLASSIFICATION");
    }

    [Fact]
    public void NearDuplicateEquivalentNodes_AreFlagged()
    {
        var profile = BuildProfile(
            territoryId: 837,
            objectives:
            [
                Node("n1", 0), Node("n2", 0.1f), Node("n3", 5), Node("n4", 10),
                Node("n5", 15), Node("n6", 20), Node("n7", 25), Node("n8", 30)
            ]);

        var report = new DutyRouteQualityAnalyzer().Analyze(profile);

        Assert.Contains(report.Issues, issue => issue.Code == "NEAR_DUPLICATE_NODE" && issue.ObjectiveId == "n2");
    }

    private static DutyNavigationProfile BuildProfile(
        uint territoryId,
        IReadOnlyList<DutyObjectiveNode> objectives,
        IReadOnlyList<BossMechanicProfile>? bosses = null)
        => new(
            DutyId: 676,
            Name: "Test Duty",
            MinimumLevel: 15,
            MaximumLevel: 100,
            Objectives: objectives,
            Bosses: bosses ?? [],
            SourceLabel: "EZBuddy clean-room recorder",
            TerritoryId: territoryId);

    private static DutyObjectiveNode Node(string id, float x)
        => new(id, DutyObjectiveKind.Waypoint, new DutyPoint(x, 0, 0), Radius: 1.5f);
}

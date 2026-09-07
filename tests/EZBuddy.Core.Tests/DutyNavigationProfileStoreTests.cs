using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Tests;

public sealed class DutyNavigationProfileStoreTests
{
    [Fact]
    public async Task Store_RoundTripsValidatedProfileAtomically()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDutyNavigationProfileStore(root);
            var profile = new DutyNavigationProfile(
                DutyId: 837,
                Name: "Original Test Route",
                MinimumLevel: 71,
                MaximumLevel: 72,
                Objectives:
                [
                    new DutyObjectiveNode("start", DutyObjectiveKind.Waypoint, new DutyPoint(1, 2, 3)),
                    new DutyObjectiveNode("switch", DutyObjectiveKind.Switch, new DutyPoint(4, 5, 6), ObjectId: 100)
                ],
                Bosses:
                [
                    new BossMechanicProfile(
                        200,
                        "Test Boss",
                        [new BossMechanicRule("avoid", BossMechanicKind.AvoidRadius, ActionId: 300, Radius: 8)])
                ],
                SourceLabel: "EZBuddy test fixture");

            await store.SaveAsync(profile, token);
            var loaded = store.Load(profile.DutyId);

            Assert.NotNull(loaded);
            Assert.Equal(profile.Name, loaded.Name);
            Assert.Equal(2, loaded.Objectives.Count);
            Assert.Single(loaded.Bosses);
            Assert.False(File.Exists(Path.Combine(root, $"duty-{profile.DutyId}.json.tmp")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Import_RejectsInvalidInteractiveNodeWithoutObjectId()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDutyNavigationProfileStore(root);
            var invalid = new DutyNavigationProfile(
                1,
                "Invalid",
                15,
                16,
                [new DutyObjectiveNode("door", DutyObjectiveKind.Door, new DutyPoint(0, 0, 0))],
                Array.Empty<BossMechanicProfile>());
            var json = System.Text.Json.JsonSerializer.Serialize(invalid);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => store.ImportAsync(json, token));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

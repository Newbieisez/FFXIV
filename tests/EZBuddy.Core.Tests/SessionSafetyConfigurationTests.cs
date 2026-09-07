using EZBuddy.Core.Safety;

namespace EZBuddy.Core.Tests;

public sealed class SessionSafetyConfigurationTests
{
    [Fact]
    public async Task Store_MissingFile_ReturnsDisabledDefault()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        var store = new JsonSessionSafetyConfigurationStore(Path.Combine(root, "safety.json"));

        var configuration = await store.LoadAsync(token);

        Assert.False(configuration.Enabled);
        Assert.Equal(240, configuration.MaximumContinuousRuntimeMinutes);
        Assert.Equal(120, configuration.BreakReminderAfterMinutes);
    }

    [Fact]
    public async Task Store_RoundTripsEnabledConfigurationAtomically()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "safety.json");
        var store = new JsonSessionSafetyConfigurationStore(path);
        var expected = new SessionSafetyConfiguration(
            Enabled: true,
            MaximumContinuousRuntimeMinutes: 180,
            BreakReminderAfterMinutes: 90,
            StuckAfterNoProgressMinutes: 8);

        await store.SaveAsync(expected, token);
        var loaded = await store.LoadAsync(token);

        Assert.Equal(expected, loaded);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Store_InvalidSavedConfiguration_FailsClosedToDefault()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "safety.json");
        await File.WriteAllTextAsync(path,
            "{\"enabled\":true,\"maximumContinuousRuntimeMinutes\":5,\"breakReminderAfterMinutes\":10,\"stuckAfterNoProgressMinutes\":2}",
            token);
        var store = new JsonSessionSafetyConfigurationStore(path);

        var loaded = await store.LoadAsync(token);

        Assert.Equal(SessionSafetyConfiguration.Default, loaded);
        Assert.False(loaded.Enabled);
    }
}

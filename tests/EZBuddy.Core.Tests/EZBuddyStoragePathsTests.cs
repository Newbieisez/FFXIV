using EZBuddy.Core.Settings;

namespace EZBuddy.Core.Tests;

public sealed class EZBuddyStoragePathsTests
{
    [Fact]
    public void PathsShareOneRootAndSanitizeCharacterKeys()
    {
        var hostRoot = Path.Combine(Path.GetTempPath(), "EZBuddy-StoragePaths-Test");
        var paths = new EZBuddyStoragePaths(hostRoot);

        var settings = paths.GetCharacterSettingsPath("Character/One");
        var checkpoint = paths.GetRuntimeCheckpointPath("Character/One");
        var replay = paths.GetDecisionReplayPath("Character/One");
        var snapshot = paths.GetProductSnapshotPath("Character/One");

        Assert.StartsWith(paths.RootDirectory, settings, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.RuntimeDirectory, checkpoint, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.RuntimeDirectory, replay, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.ProductDirectory, snapshot, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Character_One.json", Path.GetFileName(settings));
        Assert.Equal("Character_One.resume.json", Path.GetFileName(checkpoint));
        Assert.Equal("Character_One.decisions.jsonl", Path.GetFileName(replay));
        Assert.Equal("Character_One.snapshot.json", Path.GetFileName(snapshot));
    }

    [Fact]
    public void EmptyCharacterKeyFallsBackToDefault()
    {
        var paths = new EZBuddyStoragePaths(Path.GetTempPath());

        Assert.Equal("default.json", Path.GetFileName(paths.GetCharacterSettingsPath("   ")));
        Assert.Equal("default.snapshot.json", Path.GetFileName(paths.GetProductSnapshotPath(null!)));
    }
}

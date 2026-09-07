using EZBuddy.Core.Settings;

namespace EZBuddy.RebornBuddy.Settings;

public sealed class RebornBuddySettingsStoragePathProvider : ISettingsStoragePathProvider
{
    private readonly string _rootDirectory;

    public RebornBuddySettingsStoragePathProvider(string? rebornBuddyBaseDirectory = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(rebornBuddyBaseDirectory)
            ? AppDomain.CurrentDomain.BaseDirectory
            : Path.GetFullPath(rebornBuddyBaseDirectory);

        _rootDirectory = Path.Combine(baseDirectory, "Settings", "EZBuddy");
    }

    public string GetSettingsFilePath(string profileOrCharacterId)
        => Path.Combine(
            _rootDirectory,
            SettingsPathSanitizer.Sanitize(profileOrCharacterId) + ".json");
}

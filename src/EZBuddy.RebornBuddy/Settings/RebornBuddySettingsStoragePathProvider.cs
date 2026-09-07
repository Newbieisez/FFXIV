using EZBuddy.Core.Settings;

namespace EZBuddy.RebornBuddy.Settings;

public sealed class RebornBuddySettingsStoragePathProvider : ISettingsStoragePathProvider
{
    private readonly EZBuddyStoragePaths _paths;

    public RebornBuddySettingsStoragePathProvider(string? rebornBuddyBaseDirectory = null)
    {
        _paths = new EZBuddyStoragePaths(rebornBuddyBaseDirectory);
    }

    public string GetSettingsFilePath(string profileOrCharacterId)
        => _paths.GetCharacterSettingsPath(profileOrCharacterId);
}

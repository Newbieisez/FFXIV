namespace EZBuddy.Core.Settings;

/// <summary>
/// Centralizes all host-local EZBuddy storage paths so plugins, providers and settings stores
/// cannot silently drift into different directory layouts.
/// </summary>
public sealed class EZBuddyStoragePaths
{
    public EZBuddyStoragePaths(string? hostBaseDirectory = null)
    {
        HostBaseDirectory = string.IsNullOrWhiteSpace(hostBaseDirectory)
            ? AppDomain.CurrentDomain.BaseDirectory
            : Path.GetFullPath(hostBaseDirectory);

        RootDirectory = Path.Combine(HostBaseDirectory, "Settings", "EZBuddy");
        RuntimeDirectory = Path.Combine(RootDirectory, "Runtime");
        ProductDirectory = Path.Combine(RootDirectory, "Product");
    }

    public string HostBaseDirectory { get; }
    public string RootDirectory { get; }
    public string RuntimeDirectory { get; }
    public string ProductDirectory { get; }

    public string GetCharacterSettingsPath(string profileOrCharacterId)
        => Path.Combine(RootDirectory, SafeKey(profileOrCharacterId) + ".json");

    public string GetRuntimeCheckpointPath(string profileOrCharacterId)
        => Path.Combine(RuntimeDirectory, SafeKey(profileOrCharacterId) + ".resume.json");

    public string GetDecisionReplayPath(string profileOrCharacterId)
        => Path.Combine(RuntimeDirectory, SafeKey(profileOrCharacterId) + ".decisions.jsonl");

    public string GetProductSnapshotPath(string profileOrCharacterId)
        => Path.Combine(ProductDirectory, SafeKey(profileOrCharacterId) + ".snapshot.json");

    private static string SafeKey(string? value)
        => SettingsPathSanitizer.Sanitize(value);
}

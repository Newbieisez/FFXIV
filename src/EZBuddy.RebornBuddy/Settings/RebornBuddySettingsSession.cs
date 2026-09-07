using EZBuddy.Core.Settings;

namespace EZBuddy.RebornBuddy.Settings;

public static class RebornBuddySettingsSession
{
    private static readonly object Sync = new();
    private static JsonEZBuddySettingsManager? _manager;
    private static string _characterKey = string.Empty;

    public static IObservableSettings GetOrCreate()
    {
        var key = GetCharacterKey();

        lock (Sync)
        {
            if (_manager is not null && string.Equals(_characterKey, key, StringComparison.Ordinal))
            {
                return _manager;
            }

            var previous = _manager;
            var store = new JsonEZBuddySettingsStore(
                new RebornBuddySettingsStoragePathProvider(),
                key);
            var next = new JsonEZBuddySettingsManager(store);
            next.LoadAsync().GetAwaiter().GetResult();

            _manager = next;
            _characterKey = key;

            if (previous is not null)
            {
                try
                {
                    previous.FlushAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // A character/session transition should not crash the bot because a stale settings flush failed.
                }
            }

            return next;
        }
    }

    public static async Task FlushPendingSavesAsync(CancellationToken cancellationToken = default)
    {
        JsonEZBuddySettingsManager? manager;
        lock (Sync)
        {
            manager = _manager;
        }

        if (manager is not null)
        {
            await manager.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public static EZBuddySettings Current
        => GetOrCreate().Current;

    private static string GetCharacterKey()
    {
        try
        {
            var playerName = ff14bot.Core.Player?.Name;
            return string.IsNullOrWhiteSpace(playerName) ? "default" : playerName;
        }
        catch
        {
            return "default";
        }
    }
}

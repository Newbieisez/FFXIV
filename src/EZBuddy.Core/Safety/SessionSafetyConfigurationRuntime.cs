namespace EZBuddy.Core.Safety;

/// <summary>
/// Host-configured bridge used by UI surfaces to edit the active character's persisted session
/// safety configuration without learning host-specific storage paths.
/// </summary>
public static class SessionSafetyConfigurationRuntime
{
    private static ISessionSafetyConfigurationStore? _store;

    public static ISessionSafetyConfigurationStore? Store
    {
        get => Volatile.Read(ref _store);
        set => Volatile.Write(ref _store, value);
    }
}

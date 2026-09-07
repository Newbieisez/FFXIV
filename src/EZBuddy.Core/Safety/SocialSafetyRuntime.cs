namespace EZBuddy.Core.Safety;

public static class SocialSafetyRuntime
{
    private static SocialSafetyMonitor? _monitor;

    public static SocialSafetyMonitor? Monitor
    {
        get => Volatile.Read(ref _monitor);
        set => Volatile.Write(ref _monitor, value);
    }
}

namespace EZBuddy.Core.Duties;

public static class DutyRouteRecorderRuntime
{
    private static readonly object Sync = new();
    private static IDutyRouteRecorderController? _controller;

    public static IDutyRouteRecorderController? Controller
    {
        get
        {
            lock (Sync)
            {
                return _controller;
            }
        }
    }

    public static void Configure(IDutyRouteRecorderController? controller)
    {
        lock (Sync)
        {
            _controller = controller;
        }
    }
}

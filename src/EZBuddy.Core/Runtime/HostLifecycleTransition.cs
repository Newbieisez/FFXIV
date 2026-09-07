namespace EZBuddy.Core.Runtime;

public static class HostLifecycleTransition
{
    private static int _internalTransitionDepth;

    public static bool IsInternalTransition => Volatile.Read(ref _internalTransitionDepth) > 0;

    public static IDisposable BeginInternalTransition()
    {
        Interlocked.Increment(ref _internalTransitionDepth);
        return new TransitionScope();
    }

    private sealed class TransitionScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Decrement(ref _internalTransitionDepth);
        }
    }
}

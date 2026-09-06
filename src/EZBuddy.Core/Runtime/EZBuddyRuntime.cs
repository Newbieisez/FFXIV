using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Retainers;

namespace EZBuddy.Core.Runtime;

public static class EZBuddyRuntime
{
    private static readonly Lazy<ActivityQueueEngine> QueueLazy = new(() =>
        new ActivityQueueEngine(executionGate: LicenseExecutionGuard.Instance));
    private static readonly Lazy<AdapterRegistry> AdapterRegistryLazy = new(() => new AdapterRegistry());
    private static readonly Lazy<RetainerBellCoordinator> RetainerBellCoordinatorLazy = new(() => new RetainerBellCoordinator());

    public static ActivityQueueEngine Queue => QueueLazy.Value;
    public static AdapterRegistry Adapters => AdapterRegistryLazy.Value;
    public static RetainerBellCoordinator RetainerBellCoordinator => RetainerBellCoordinatorLazy.Value;
}

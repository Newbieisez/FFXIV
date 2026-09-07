using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Tests;

public sealed class DutyObjectiveNodeExecutorTests
{
    [Fact]
    public async Task Waypoint_CompletesInsideArrivalRadius()
    {
        var host = new FakeNodeHost(new DutyNodeHostSnapshot(100, new DutyPoint(1, 0, 1), 20, false));
        var executor = new DutyObjectiveNodeExecutor(host);
        var node = new DutyObjectiveNode("wp", DutyObjectiveKind.Waypoint, new DutyPoint(1.5f, 0, 1), 1.5f);

        var result = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);

        Assert.Equal(NodeExecutionResult.Completed, result);
        Assert.Equal(1, host.StopCalls);
        Assert.Equal(0, host.MoveCalls);
    }

    [Fact]
    public async Task Chest_SkipsWhenInventoryAtSafetyFloor()
    {
        var host = new FakeNodeHost(new DutyNodeHostSnapshot(100, new DutyPoint(0, 0, 0), 5, false));
        var executor = new DutyObjectiveNodeExecutor(host, new DutyObjectiveExecutorOptions(MinimumDutyFreeSlots: 5));
        var node = new DutyObjectiveNode("chest", DutyObjectiveKind.Chest, new DutyPoint(2, 0, 0), 3f, 2000, Required: false);

        var result = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);

        Assert.Equal(NodeExecutionResult.Completed, result);
        Assert.Equal(0, host.InteractCalls);
    }

    [Fact]
    public async Task Interaction_ClicksOnlyOnceUntilTargetBecomesUnavailable()
    {
        var host = new FakeNodeHost(new DutyNodeHostSnapshot(100, new DutyPoint(0, 0, 0), 20, false))
        {
            Interactable = new DutyInteractableState(2001, new DutyPoint(1, 0, 0), true, true, 1f)
        };
        var executor = new DutyObjectiveNodeExecutor(host);
        var node = new DutyObjectiveNode("door", DutyObjectiveKind.Door, new DutyPoint(1, 0, 0), 3f, 2001);

        var first = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);
        var second = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);
        host.Interactable = host.Interactable with { IsTargetable = false };
        var third = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);

        Assert.Equal(NodeExecutionResult.InProgress, first);
        Assert.Equal(NodeExecutionResult.InProgress, second);
        Assert.Equal(NodeExecutionResult.Completed, third);
        Assert.Equal(1, host.InteractCalls);
    }

    [Fact]
    public async Task BossBoundary_HoldsRouteWhileCombatIsActive()
    {
        var host = new FakeNodeHost(new DutyNodeHostSnapshot(100, new DutyPoint(0, 0, 0), 20, true));
        var executor = new DutyObjectiveNodeExecutor(host);
        var node = new DutyObjectiveNode("boss", DutyObjectiveKind.BossBoundary, new DutyPoint(0, 0, 0), 2f);

        var active = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);
        host.State = host.State with { InCombat = false };
        var complete = await executor.ExecuteAsync(node, TestContext.Current.CancellationToken);

        Assert.Equal(NodeExecutionResult.InProgress, active);
        Assert.Equal(NodeExecutionResult.Completed, complete);
        Assert.Equal(0, host.MoveCalls);
        Assert.Equal(0, host.InteractCalls);
    }

    [Fact]
    public async Task RouteRunner_AdvancesOneNodeAtATime()
    {
        var host = new FakeNodeHost(new DutyNodeHostSnapshot(100, new DutyPoint(0, 0, 0), 20, false));
        var executor = new DutyObjectiveNodeExecutor(host);
        var profile = new DutyNavigationProfile(
            4,
            "Test",
            15,
            100,
            [
                new DutyObjectiveNode("one", DutyObjectiveKind.Waypoint, new DutyPoint(0, 0, 0), 1.5f),
                new DutyObjectiveNode("two", DutyObjectiveKind.Waypoint, new DutyPoint(0, 0, 0), 1.5f)
            ],
            [],
            TerritoryId: 100);
        var runner = new DutyObjectiveRouteRunner(profile, executor);

        var first = await runner.TickAsync(TestContext.Current.CancellationToken);
        var second = await runner.TickAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NodeExecutionResult.InProgress, first);
        Assert.Equal(NodeExecutionResult.Completed, second);
        Assert.True(runner.IsComplete);
    }

    private sealed class FakeNodeHost(DutyNodeHostSnapshot state) : IDutyObjectiveNodeHost
    {
        public DutyNodeHostSnapshot State { get; set; } = state;
        public DutyInteractableState? Interactable { get; set; }
        public int MoveCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int InteractCalls { get; private set; }

        public DutyNodeHostSnapshot ReadState() => State;

        public Task<bool> MoveTowardAsync(DutyPoint position, float arrivalRadius, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MoveCalls++;
            return Task.FromResult(true);
        }

        public Task<DutyInteractableState?> FindInteractableAsync(uint objectId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Interactable);
        }

        public Task<bool> StopMovementAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> FaceAndInteractAsync(uint objectId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InteractCalls++;
            return Task.FromResult(true);
        }
    }
}

using EZBuddy.Core.Adapters;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class DutyNativeRouteLifecycleTests
{
    [Fact]
    public async Task NativeRoute_UsesNodeRunnerAndNeverLoadsOrderBotProfile()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new PersistentDutyAdapter();
        var orderBot = new FakeOrderBotAdapter();
        var native = new OneTickNativeRunner();
        var postRun = new FakePostRunAdapter(duty.MarkExited);
        var activity = new DutySupportLevelingActivity(
            duty,
            orderBot,
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20, 100)),
            new DutySupportLevelingOptions(
                DutyId: 4,
                ProfilePath: string.Empty,
                MaxRuns: 1,
                TerritoryId: 100,
                UseNativeRoute: true),
            postRun,
            native);

        var queue = await activity.ExecuteStepAsync(token);
        var entry = await activity.ExecuteStepAsync(token);
        var route = await activity.ExecuteStepAsync(token);
        var loot = await activity.ExecuteStepAsync(token);
        var leave = await activity.ExecuteStepAsync(token);
        var exit = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Continue, queue.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, entry.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, route.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, loot.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, leave.Disposition);
        Assert.Equal(ExecutionDisposition.Complete, exit.Disposition);
        Assert.Equal(1, native.TickCalls);
        Assert.Equal(0, orderBot.LoadCalls);
        Assert.Equal(1, activity.CompletedRuns);
    }

    [Fact]
    public async Task NativeRoute_WrongTerritoryBlocksBeforeFirstNodeTick()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new PersistentDutyAdapter();
        var native = new OneTickNativeRunner();
        var activity = new DutySupportLevelingActivity(
            duty,
            new FakeOrderBotAdapter(),
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20, 999)),
            new DutySupportLevelingOptions(
                DutyId: 4,
                ProfilePath: string.Empty,
                MaxRuns: 1,
                TerritoryId: 100,
                UseNativeRoute: true),
            nativeRoute: native);

        _ = await activity.ExecuteStepAsync(token);
        var entry = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Block, entry.Disposition);
        Assert.Equal(0, native.TickCalls);
    }

    private sealed class StaticProgressProvider(DutyLevelingProgress progress) : IDutyLevelingProgressProvider
    {
        public DutyLevelingProgress Read() => progress;
    }

    private sealed class OneTickNativeRunner : IDutyInInstanceRunner
    {
        public int TickCalls { get; private set; }
        public bool IsComplete => TickCalls > 0;

        public Task<NodeExecutionResult> TickAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TickCalls++;
            return Task.FromResult(NodeExecutionResult.Completed);
        }
    }

    private sealed class PersistentDutyAdapter : IDutySupportAdapter
    {
        private bool _entered;
        private bool _exited;
        public string Key => "native-duty";
        public string DisplayName => "Native Duty";
        public void MarkExited() => _exited = true;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));

        public Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inDungeon = _entered && !_exited;
            return Task.FromResult(new DutyAutomationStatus(
                inDungeon ? "InDungeon" : "None",
                false,
                false,
                false,
                inDungeon));
        }

        public Task<bool> EnterAsync(DutyAutomationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entered = true;
            return Task.FromResult(true);
        }

        public Task<bool> AdvanceEntryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeOrderBotAdapter : IOrderBotAdapter
    {
        public int LoadCalls { get; private set; }
        public string Key => "fake-orderbot";
        public string DisplayName => "Fake OrderBot";

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));

        public Task<bool> LoadProfileAsync(string profilePathOrIdentifier, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> IsProfileRunningAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class FakeMagitekAdapter : IMagitekAdapter
    {
        public string Key => "fake-magitek";
        public string DisplayName => "Fake Magitek";
        public bool IsRequiredForCombat => true;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));

        public Task<bool> IsCurrentRoutineAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakePostRunAdapter(Action onLeave) : IDutyPostRunAdapter
    {
        private bool _lootOpen = true;
        private bool _leaveRequested;

        public Task<DutyPostRunStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DutyPostRunStatus(true, _lootOpen, _leaveRequested, "Complete"));

        public Task<bool> ProcessLootAsync(
            DutyLootPolicy policy,
            int freeInventorySlots,
            CancellationToken cancellationToken = default)
        {
            _lootOpen = false;
            return Task.FromResult(true);
        }

        public Task<bool> RequestLeaveAsync(CancellationToken cancellationToken = default)
        {
            _leaveRequested = true;
            onLeave();
            return Task.FromResult(true);
        }
    }
}

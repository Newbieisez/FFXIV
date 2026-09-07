using EZBuddy.Core.Adapters;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class DutySupportLevelingActivityTests
{
    [Fact]
    public async Task ReachingTargetLevelCompletesWithoutEnteringDuty()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new FakeDutyAdapter();
        var activity = CreateActivity(
            duty,
            new StaticProgressProvider(new DutyLevelingProgress(50, 20)),
            new DutySupportLevelingOptions(1, "profile.xml", TargetLevel: 50, MaxRuns: null));

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.Equal(0, duty.EnterCalls);
    }

    [Fact]
    public async Task LowInventoryBlocksBeforeQueueing()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new FakeDutyAdapter();
        var activity = CreateActivity(
            duty,
            new StaticProgressProvider(new DutyLevelingProgress(40, 2)),
            new DutySupportLevelingOptions(1, "profile.xml", MaxRuns: 1, MinimumFreeInventorySlots: 5));

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Block, result.Disposition);
        Assert.Equal(0, duty.EnterCalls);
    }

    [Fact]
    public async Task EntryAndProfileHandoffOccurAcrossSeparateTicks()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new FakeDutyAdapter(stayQueuedBeforeEntry: true);
        var orderBot = new FakeOrderBotAdapter();
        var activity = new DutySupportLevelingActivity(
            duty,
            orderBot,
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20)),
            new DutySupportLevelingOptions(1, "profile.xml", MaxRuns: 1));

        var queueTick = await activity.ExecuteStepAsync(token);
        var waitTick = await activity.ExecuteStepAsync(token);
        var entryTick = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Continue, queueTick.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, waitTick.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, entryTick.Disposition);
        Assert.Equal(1, duty.EnterCalls);
        Assert.Equal(1, duty.AdvanceCalls);
        Assert.Equal(1, orderBot.LoadCalls);
    }

    [Fact]
    public async Task OneRunGoalCompletesAfterProfileAndDutyExit()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new FakeDutyAdapter();
        var orderBot = new FakeOrderBotAdapter();
        var activity = new DutySupportLevelingActivity(
            duty,
            orderBot,
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20)),
            new DutySupportLevelingOptions(1, "profile.xml", MaxRuns: 1));

        var first = await activity.ExecuteStepAsync(token);
        var second = await activity.ExecuteStepAsync(token);
        var third = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, second.Disposition);
        Assert.Equal(ExecutionDisposition.Complete, third.Disposition);
        Assert.Equal(1, activity.CompletedRuns);
        Assert.Equal(1, duty.EnterCalls);
        Assert.Equal(1, orderBot.LoadCalls);
    }

    [Fact]
    public async Task CompletedInstanceProcessesLootAndLeavesAcrossSeparateTicks()
    {
        var token = TestContext.Current.CancellationToken;
        var duty = new PersistentDutyAdapter();
        var postRun = new FakePostRunAdapter(duty.MarkExited);
        var activity = new DutySupportLevelingActivity(
            duty,
            new FakeOrderBotAdapter(),
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20)),
            new DutySupportLevelingOptions(
                1,
                "profile.xml",
                MaxRuns: 1,
                LootPolicy: new DutyLootPolicy(DutyLootAction.Greed, PassAtOrBelowFreeSlots: 3)),
            postRun);

        var queue = await activity.ExecuteStepAsync(token);
        var entered = await activity.ExecuteStepAsync(token);
        var verifyCompletion = await activity.ExecuteStepAsync(token);
        var loot = await activity.ExecuteStepAsync(token);
        var leave = await activity.ExecuteStepAsync(token);
        var exited = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Continue, queue.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, entered.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, verifyCompletion.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, loot.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, leave.Disposition);
        Assert.Equal(ExecutionDisposition.Complete, exited.Disposition);
        Assert.Equal(1, postRun.LootCalls);
        Assert.Equal(1, postRun.LeaveCalls);
        Assert.Equal(1, activity.CompletedRuns);
    }

    private static DutySupportLevelingActivity CreateActivity(
        FakeDutyAdapter duty,
        IDutyLevelingProgressProvider progress,
        DutySupportLevelingOptions options)
        => new(
            duty,
            new FakeOrderBotAdapter(),
            new FakeMagitekAdapter(),
            progress,
            options);

    private sealed class StaticProgressProvider(DutyLevelingProgress progress) : IDutyLevelingProgressProvider
    {
        public DutyLevelingProgress Read() => progress;
    }

    private sealed class FakeDutyAdapter(bool stayQueuedBeforeEntry = false) : IDutySupportAdapter
    {
        private bool _entered;
        private bool _profilePhaseObserved;

        public int EnterCalls { get; private set; }
        public int AdvanceCalls { get; private set; }
        public string Key => "fake-duty";
        public string DisplayName => "Fake Duty";

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));
        }

        public Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_entered)
            {
                return Task.FromResult(new DutyAutomationStatus("None", false, false, false, false));
            }

            if (stayQueuedBeforeEntry && AdvanceCalls == 0)
            {
                return Task.FromResult(new DutyAutomationStatus("InQueue", true, false, false, false));
            }

            if (!_profilePhaseObserved)
            {
                _profilePhaseObserved = true;
                return Task.FromResult(new DutyAutomationStatus("InDungeon", false, false, false, true));
            }

            return Task.FromResult(new DutyAutomationStatus("None", false, false, false, false));
        }

        public Task<bool> EnterAsync(DutyAutomationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnterCalls++;
            _entered = true;
            return Task.FromResult(true);
        }

        public Task<bool> AdvanceEntryAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdvanceCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class PersistentDutyAdapter : IDutySupportAdapter
    {
        private bool _entered;
        private bool _exited;

        public string Key => "persistent-duty";
        public string DisplayName => "Persistent Duty";

        public void MarkExited() => _exited = true;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));
        }

        public Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inDungeon = _entered && !_exited;
            return Task.FromResult(new DutyAutomationStatus(
                inDungeon ? "InDungeon" : _entered ? "None" : "None",
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
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    private sealed class FakePostRunAdapter(Action onLeave) : IDutyPostRunAdapter
    {
        private bool _lootOpen = true;
        private bool _leaveRequested;

        public int LootCalls { get; private set; }
        public int LeaveCalls { get; private set; }

        public Task<DutyPostRunStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DutyPostRunStatus(
                IsDutyComplete: true,
                IsLootWindowOpen: _lootOpen,
                IsLeaveRequested: _leaveRequested,
                Message: "Complete"));
        }

        public Task<bool> ProcessLootAsync(
            DutyLootPolicy policy,
            int freeInventorySlots,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LootCalls++;
            _lootOpen = false;
            return Task.FromResult(true);
        }

        public Task<bool> RequestLeaveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeaveCalls++;
            _leaveRequested = true;
            onLeave();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeOrderBotAdapter : IOrderBotAdapter
    {
        public int LoadCalls { get; private set; }
        public string Key => "fake-orderbot";
        public string DisplayName => "Fake OrderBot";

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));
        }

        public Task<bool> LoadProfileAsync(string profilePathOrIdentifier, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> IsProfileRunningAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class FakeMagitekAdapter : IMagitekAdapter
    {
        public string Key => "fake-magitek";
        public string DisplayName => "Fake Magitek";
        public bool IsRequiredForCombat => true;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));
        }

        public Task<bool> IsCurrentRoutineAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }
}

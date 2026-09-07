using EZBuddy.Core.Adapters;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class DutySupportLevelingActivityTests
{
    [Fact]
    public async Task ReachingTargetLevelCompletesWithoutEnteringDuty()
    {
        var duty = new FakeDutyAdapter();
        var activity = CreateActivity(
            duty,
            new StaticProgressProvider(new DutyLevelingProgress(50, 20)),
            new DutySupportLevelingOptions(1, "profile.xml", TargetLevel: 50, MaxRuns: null));

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.Equal(0, duty.EnterCalls);
    }

    [Fact]
    public async Task LowInventoryBlocksBeforeQueueing()
    {
        var duty = new FakeDutyAdapter();
        var activity = CreateActivity(
            duty,
            new StaticProgressProvider(new DutyLevelingProgress(40, 2)),
            new DutySupportLevelingOptions(1, "profile.xml", MaxRuns: 1, MinimumFreeInventorySlots: 5));

        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Block, result.Disposition);
        Assert.Equal(0, duty.EnterCalls);
    }

    [Fact]
    public async Task EntryAndProfileHandoffOccurAcrossSeparateTicks()
    {
        var duty = new FakeDutyAdapter(stayQueuedBeforeEntry: true);
        var orderBot = new FakeOrderBotAdapter();
        var activity = new DutySupportLevelingActivity(
            duty,
            orderBot,
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20)),
            new DutySupportLevelingOptions(1, "profile.xml", MaxRuns: 1));

        var queueTick = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var waitTick = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var entryTick = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

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
        var duty = new FakeDutyAdapter();
        var orderBot = new FakeOrderBotAdapter();
        var activity = new DutySupportLevelingActivity(
            duty,
            orderBot,
            new FakeMagitekAdapter(),
            new StaticProgressProvider(new DutyLevelingProgress(40, 20)),
            new DutySupportLevelingOptions(1, "profile.xml", MaxRuns: 1));

        var first = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var second = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var third = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.Equal(ExecutionDisposition.Yield, second.Disposition);
        Assert.Equal(ExecutionDisposition.Complete, third.Disposition);
        Assert.Equal(1, activity.CompletedRuns);
        Assert.Equal(1, duty.EnterCalls);
        Assert.Equal(1, orderBot.LoadCalls);
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
            => Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));

        public Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default)
        {
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
            EnterCalls++;
            _entered = true;
            return Task.FromResult(true);
        }

        public Task<bool> AdvanceEntryAsync(CancellationToken cancellationToken = default)
        {
            AdvanceCalls++;
            return Task.FromResult(true);
        }
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
}

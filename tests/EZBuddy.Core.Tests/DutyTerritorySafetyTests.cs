using EZBuddy.Core.Adapters;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class DutyTerritorySafetyTests
{
    [Fact]
    public async Task WrongTerritory_BlocksBeforeOrderBotProfileStarts()
    {
        var token = TestContext.Current.CancellationToken;
        var orderBot = new FakeOrderBot();
        var activity = new DutySupportLevelingActivity(
            new InDungeonDutyAdapter(),
            orderBot,
            new FakeMagitek(),
            new StaticProgress(new DutyLevelingProgress(71, 20, TerritoryId: 999)),
            new DutySupportLevelingOptions(
                DutyId: 676,
                ProfilePath: "profile.xml",
                MaxRuns: 1,
                TerritoryId: 837));

        var queued = await activity.ExecuteStepAsync(token);
        var zoneIn = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Continue, queued.Disposition);
        Assert.Equal(ExecutionDisposition.Block, zoneIn.Disposition);
        Assert.Equal(0, orderBot.LoadCalls);
        Assert.Contains("does not match configured territory 837", zoneIn.Message, StringComparison.Ordinal);
    }

    private sealed class StaticProgress(DutyLevelingProgress progress) : IDutyLevelingProgressProvider
    {
        public DutyLevelingProgress Read() => progress;
    }

    private sealed class InDungeonDutyAdapter : IDutySupportAdapter
    {
        public string Key => "territory-duty";
        public string DisplayName => "Territory Duty";

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));

        public Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DutyAutomationStatus("InDungeon", false, false, false, true));
        }

        public Task<bool> EnterAsync(DutyAutomationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<bool> AdvanceEntryAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeOrderBot : IOrderBotAdapter
    {
        public int LoadCalls { get; private set; }
        public string Key => "fake-order";
        public string DisplayName => "Fake Order";

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Ready", DateTimeOffset.UtcNow));

        public Task<bool> LoadProfileAsync(string profilePathOrIdentifier, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> IsProfileRunningAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class FakeMagitek : IMagitekAdapter
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

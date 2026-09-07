using EZBuddy.Core.Engine;
using EZBuddy.Core.GoldSaucer;

namespace EZBuddy.Core.Tests;

public sealed class JumboCactpotActivityTests
{
    [Fact]
    public async Task Activity_ClaimsPrizesBeforeBuyingTicketsOnePerTick()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new ScriptedHost(
            new JumboCactpotHostSnapshot(0, 1, true),
            new JumboCactpotHostSnapshot(0, 0, true),
            new JumboCactpotHostSnapshot(1, 0, true),
            new JumboCactpotHostSnapshot(2, 0, true),
            new JumboCactpotHostSnapshot(3, 0, true));
        var options = new JumboCactpotActivityOptions([
            new JumboCactpotNumber(1, 2, 3, 4),
            new JumboCactpotNumber(2, 3, 4, 5),
            new JumboCactpotNumber(3, 4, 5, 6)
        ]);
        var activity = new JumboCactpotActivity(host, options);

        var results = new List<ExecutionResult>();
        for (var tick = 0; tick < 5; tick++)
        {
            results.Add(await activity.ExecuteStepAsync(token));
        }

        Assert.True(activity.IsComplete);
        Assert.Equal(1, host.ClaimCalls);
        Assert.Equal(3, host.Purchased.Count);
        Assert.Equal("1234", host.Purchased[0].ToString());
        Assert.Equal(1, host.CloseCalls);
        Assert.Equal(ExecutionDisposition.Complete, results[^1].Disposition);
    }

    [Fact]
    public async Task BusyHost_YieldsWithoutMutation()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new ScriptedHost(new JumboCactpotHostSnapshot(0, 0, true, IsBusy: true));
        var activity = new JumboCactpotActivity(
            host,
            new JumboCactpotActivityOptions([new JumboCactpotNumber(1, 1, 1, 1)]));

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Yield, result.Disposition);
        Assert.Equal(0, host.ClaimCalls);
        Assert.Empty(host.Purchased);
    }

    [Fact]
    public async Task PurchaseUnavailable_CompletesWithoutInventingDrawTiming()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new ScriptedHost(new JumboCactpotHostSnapshot(0, 0, false));
        var activity = new JumboCactpotActivity(
            host,
            new JumboCactpotActivityOptions([new JumboCactpotNumber(1, 2, 3, 4)]));

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.Empty(host.Purchased);
    }

    [Fact]
    public void Options_RejectMoreThanThreeOrDuplicateTickets()
    {
        Assert.Throws<ArgumentException>(() => new JumboCactpotActivityOptions([
            new JumboCactpotNumber(1, 1, 1, 1),
            new JumboCactpotNumber(2, 2, 2, 2),
            new JumboCactpotNumber(3, 3, 3, 3),
            new JumboCactpotNumber(4, 4, 4, 4)
        ]).Validate());

        Assert.Throws<ArgumentException>(() => new JumboCactpotActivityOptions([
            new JumboCactpotNumber(1, 1, 1, 1),
            new JumboCactpotNumber(1, 1, 1, 1)
        ]).Validate());
    }

    [Fact]
    public void Number_RejectsDigitOutsideCurrentOfficialRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JumboCactpotNumber(0, 1, 2, 3).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new JumboCactpotNumber(1, 2, 3, 10).Validate());
    }

    private sealed class ScriptedHost : IJumboCactpotHost
    {
        private readonly Queue<JumboCactpotHostSnapshot> _snapshots;

        public ScriptedHost(params JumboCactpotHostSnapshot[] snapshots)
        {
            _snapshots = new Queue<JumboCactpotHostSnapshot>(snapshots);
        }

        public int ClaimCalls { get; private set; }
        public List<JumboCactpotNumber> Purchased { get; } = [];
        public int CloseCalls { get; private set; }

        public Task<JumboCactpotHostSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_snapshots.Count == 0)
            {
                throw new InvalidOperationException("No Jumbo Cactpot snapshot remains.");
            }

            return Task.FromResult(_snapshots.Dequeue());
        }

        public Task<bool> ClaimNextPrizeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> PurchaseTicketAsync(JumboCactpotNumber number, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Purchased.Add(number);
            return Task.FromResult(true);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCalls++;
            return Task.CompletedTask;
        }
    }
}

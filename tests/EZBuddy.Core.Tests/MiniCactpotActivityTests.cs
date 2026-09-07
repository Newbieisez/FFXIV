using EZBuddy.Core.Engine;
using EZBuddy.Core.GoldSaucer;

namespace EZBuddy.Core.Tests;

public sealed class MiniCactpotActivityTests
{
    [Fact]
    public async Task Activity_ExecutesOneUiActionPerTickThroughCompleteTicket()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new ScriptedHost(
            Snapshot(false, 1, [null, null, null, null, null, null, null, null, null]),
            Snapshot(true, 0, [1, null, null, null, null, null, null, null, null]),
            Snapshot(true, 0, [1, 2, null, null, null, null, null, null, null]),
            Snapshot(true, 0, [1, 2, 3, null, null, null, null, null, null]),
            Snapshot(true, 0, [1, 2, 3, 4, null, null, null, null, null]),
            Snapshot(true, 0, [1, 2, 3, 4, null, null, null, null, null], prizeReady: true),
            Snapshot(false, 0, [null, null, null, null, null, null, null, null, null]));

        var activity = new MiniCactpotActivity(host, new MiniCactpotActivityOptions(MaximumTickets: 1));

        var results = new List<ExecutionResult>();
        for (var tick = 0; tick < 7; tick++)
        {
            results.Add(await activity.ExecuteStepAsync(token));
        }

        Assert.True(activity.IsComplete);
        Assert.Equal(1, activity.CompletedTickets);
        Assert.Equal(1, host.OpenCalls);
        Assert.Equal(3, host.RevealCalls.Count);
        Assert.Single(host.SelectedLines);
        Assert.Equal(1, host.ClaimCalls);
        Assert.Equal(1, host.CloseCalls);
        Assert.Equal(ExecutionDisposition.Complete, results[^1].Disposition);
        Assert.Equal(6, host.TotalUiMutationsBeforeClose);
    }

    [Fact]
    public async Task BusyWindow_YieldsWithoutMutatingUi()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new ScriptedHost(
            Snapshot(true, 2, [1, null, null, null, null, null, null, null, null], isBusy: true));
        var activity = new MiniCactpotActivity(host);

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Yield, result.Disposition);
        Assert.Equal(0, host.TotalUiMutationsBeforeClose);
    }

    [Fact]
    public async Task Halt_ClosesHostWindow()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new ScriptedHost(
            Snapshot(true, 2, [1, null, null, null, null, null, null, null, null]));
        var activity = new MiniCactpotActivity(host);

        await activity.OnHaltAsync(token);

        Assert.Equal(1, host.CloseCalls);
    }

    private static MiniCactpotHostSnapshot Snapshot(
        bool open,
        int tickets,
        IReadOnlyList<int?> cells,
        bool prizeReady = false,
        bool isBusy = false)
        => new(open, tickets, cells, prizeReady, isBusy);

    private sealed class ScriptedHost : IMiniCactpotHost
    {
        private readonly Queue<MiniCactpotHostSnapshot> _snapshots;

        public ScriptedHost(params MiniCactpotHostSnapshot[] snapshots)
        {
            _snapshots = new Queue<MiniCactpotHostSnapshot>(snapshots);
        }

        public int OpenCalls { get; private set; }
        public List<int> RevealCalls { get; } = [];
        public List<IReadOnlyList<int>> SelectedLines { get; } = [];
        public int ClaimCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int TotalUiMutationsBeforeClose => OpenCalls + RevealCalls.Count + SelectedLines.Count + ClaimCalls;

        public Task<MiniCactpotHostSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_snapshots.Count == 0)
            {
                throw new InvalidOperationException("No scripted Mini Cactpot snapshot remains.");
            }

            return Task.FromResult(_snapshots.Dequeue());
        }

        public Task<bool> OpenNextTicketAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> RevealCellAsync(int cellIndex, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevealCalls.Add(cellIndex);
            return Task.FromResult(true);
        }

        public Task<bool> SelectLineAsync(IReadOnlyList<int> cellIndexes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectedLines.Add(cellIndexes.ToArray());
            return Task.FromResult(true);
        }

        public Task<bool> ClaimPrizeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimCalls++;
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

using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class RoutineCompletionActivityTests
{
    [Fact]
    public async Task Complete_RecordsRoutineHistory()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new InMemoryRoutineCompletionStore();
        var completedAt = new DateTimeOffset(2026, 9, 7, 4, 0, 0, TimeSpan.Zero);
        var activity = new RoutineCompletionActivity(
            "mini-cactpot",
            new ResultActivity(ExecutionResult.Complete("done")),
            store,
            () => completedAt);

        var result = await activity.ExecuteStepAsync(token);
        var saved = await store.GetLatestAsync("mini-cactpot", token);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.NotNull(saved);
        Assert.Equal(completedAt, saved.CompletedAtUtc);
    }

    [Theory]
    [InlineData(ExecutionDisposition.Block)]
    [InlineData(ExecutionDisposition.Fail)]
    [InlineData(ExecutionDisposition.Retry)]
    [InlineData(ExecutionDisposition.Yield)]
    [InlineData(ExecutionDisposition.Continue)]
    public async Task NonCompleteResult_DoesNotRecordRoutineHistory(ExecutionDisposition disposition)
    {
        var token = TestContext.Current.CancellationToken;
        var store = new InMemoryRoutineCompletionStore();
        var activity = new RoutineCompletionActivity(
            "test-routine",
            new ResultActivity(new ExecutionResult(disposition, "not complete")),
            store);

        await activity.ExecuteStepAsync(token);
        var saved = await store.GetLatestAsync("test-routine", token);

        Assert.False(activity.IsComplete);
        Assert.Null(saved);
    }

    [Fact]
    public async Task Halt_DelegatesWithoutRecordingCompletion()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new InMemoryRoutineCompletionStore();
        var inner = new ResultActivity(ExecutionResult.Continue("running"));
        var activity = new RoutineCompletionActivity("routine", inner, store);

        await activity.OnHaltAsync(token);

        Assert.True(inner.Halted);
        Assert.Null(await store.GetLatestAsync("routine", token));
    }

    private sealed class ResultActivity : IEZActivity
    {
        private readonly ExecutionResult _result;

        public ResultActivity(ExecutionResult result)
        {
            _result = result;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public string Name => "Result Activity";
        public ActivityCategory Category => ActivityCategory.Utility;
        public bool IsComplete => false;
        public bool Halted { get; private set; }

        public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }

        public Task OnHaltAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Halted = true;
            return Task.CompletedTask;
        }
    }
}

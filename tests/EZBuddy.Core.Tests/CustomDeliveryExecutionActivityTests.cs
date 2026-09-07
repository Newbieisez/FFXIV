using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class CustomDeliveryExecutionActivityTests
{
    [Fact]
    public async Task ExplicitSelectionCompletesExactlyOnce()
    {
        var adapter = new FakeCustomDeliveryAdapter(runResult: true);
        var activity = new CustomDeliveryExecutionActivity(
            adapter,
            new CustomDeliveryExecutionOptions(["ameliance", "margrat"], "Carpenter"));

        Assert.True(await activity.CanExecuteAsync(TestContext.Current.CancellationToken));
        var result = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.True(activity.IsComplete);
        Assert.Equal(1, adapter.RunCalls);
        Assert.Equal(["ameliance", "margrat"], adapter.LastClients);
        Assert.Equal("Carpenter", adapter.LastCraftingClass);
    }

    [Fact]
    public async Task AmbiguousFailureBlocksAndIsNotAutomaticallyRepeated()
    {
        var adapter = new FakeCustomDeliveryAdapter(runResult: false);
        var activity = new CustomDeliveryExecutionActivity(
            adapter,
            new CustomDeliveryExecutionOptions(["anden"]));

        var first = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);
        var second = await activity.ExecuteStepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionDisposition.Block, first.Disposition);
        Assert.Equal(ExecutionDisposition.Block, second.Disposition);
        Assert.False(activity.IsComplete);
        Assert.Equal(1, adapter.RunCalls);
    }

    [Fact]
    public void EmptySelectionIsRejected()
    {
        var adapter = new FakeCustomDeliveryAdapter(runResult: true);
        Assert.Throws<ArgumentException>(() =>
            new CustomDeliveryExecutionActivity(
                adapter,
                new CustomDeliveryExecutionOptions(Array.Empty<string>())));
    }

    private sealed class FakeCustomDeliveryAdapter(bool runResult) : ICustomDeliveryAdapter
    {
        public string Key => "fake-custom-deliveries";
        public string DisplayName => "Fake Custom Deliveries";
        public int RunCalls { get; private set; }
        public IReadOnlyList<string> LastClients { get; private set; } = [];
        public string LastCraftingClass { get; private set; } = string.Empty;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                AdapterHealth.Ready,
                "ready",
                DateTimeOffset.UtcNow));
        }

        public Task<bool> RunSelectedAsync(
            IReadOnlyCollection<string> clientKeys,
            string craftingClassKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunCalls++;
            LastClients = clientKeys.ToArray();
            LastCraftingClass = craftingClassKey;
            return Task.FromResult(runResult);
        }
    }
}
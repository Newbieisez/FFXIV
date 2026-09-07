using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class RunLoopControllerTests
{
    [Fact]
    public async Task StartPauseResume_AreAppliedOnlyWhenHostConsumesSignals()
    {
        var token = TestContext.Current.CancellationToken;
        var queue = new ActivityQueueEngine();
        var controller = new RunLoopController(queue);

        await controller.StartAsync(token);
        Assert.Equal(RunLoopState.Idle, controller.State);
        Assert.False(queue.IsStarted);

        await controller.ApplyPendingSignalsAsync(token);
        Assert.Equal(RunLoopState.Running, controller.State);
        Assert.True(queue.IsRunning);

        await controller.PauseAsync(token);
        Assert.Equal(RunLoopState.Running, controller.State);

        await controller.ApplyPendingSignalsAsync(token);
        Assert.Equal(RunLoopState.Paused, controller.State);
        Assert.True(queue.IsPaused);

        await controller.ResumeAsync(token);
        await controller.ApplyPendingSignalsAsync(token);
        Assert.Equal(RunLoopState.Running, controller.State);
        Assert.True(queue.IsRunning);
    }

    [Fact]
    public async Task GentleStop_HaltsCurrentActivityAtNextTickBoundaryAndReturnsIdle()
    {
        var token = TestContext.Current.CancellationToken;
        var queue = new ActivityQueueEngine();
        var controller = new RunLoopController(queue);
        var activity = new BoundaryActivity();
        queue.Enqueue(new ActivityQueueItem(activity));

        await controller.StartAsync(token);
        await controller.ApplyPendingSignalsAsync(token);

        var first = await queue.TickAsync(token);
        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.NotNull(queue.CurrentActivity);

        await controller.StopAsync(token);
        await controller.ApplyPendingSignalsAsync(token);

        Assert.Equal(RunLoopState.Stopping, controller.State);
        Assert.True(queue.IsGentleStopRequested);

        var stopResult = await queue.TickAsync(token);
        controller.ObserveEngineState();

        Assert.Equal(ExecutionDisposition.Complete, stopResult.Disposition);
        Assert.Equal(RunLoopState.Idle, controller.State);
        Assert.True(activity.HaltCalled);
        Assert.True(queue.IsPaused);
        Assert.Null(queue.CurrentActivity);
    }

    private sealed class BoundaryActivity : IEZActivity
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name => "Boundary Test";
        public ActivityCategory Category => ActivityCategory.Utility;
        public bool IsComplete => false;
        public bool HaltCalled { get; private set; }

        public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecutionResult.Continue("Running until safe stop boundary."));
        }

        public Task OnHaltAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HaltCalled = true;
            return Task.CompletedTask;
        }
    }
}

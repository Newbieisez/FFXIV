using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Tests;

public sealed class RunLoopControllerTests
{
    [Fact]
    public async Task StartPauseResume_AreAppliedOnlyWhenHostConsumesSignals()
    {
        var queue = new ActivityQueueEngine();
        var controller = new RunLoopController(queue);

        await controller.StartAsync();
        Assert.Equal(RunLoopState.Idle, controller.State);
        Assert.False(queue.IsStarted);

        await controller.ApplyPendingSignalsAsync();
        Assert.Equal(RunLoopState.Running, controller.State);
        Assert.True(queue.IsRunning);

        await controller.PauseAsync();
        Assert.Equal(RunLoopState.Running, controller.State);

        await controller.ApplyPendingSignalsAsync();
        Assert.Equal(RunLoopState.Paused, controller.State);
        Assert.True(queue.IsPaused);

        await controller.ResumeAsync();
        await controller.ApplyPendingSignalsAsync();
        Assert.Equal(RunLoopState.Running, controller.State);
        Assert.True(queue.IsRunning);
    }

    [Fact]
    public async Task GentleStop_HaltsCurrentActivityAtNextTickBoundaryAndReturnsIdle()
    {
        var queue = new ActivityQueueEngine();
        var controller = new RunLoopController(queue);
        var activity = new BoundaryActivity();
        queue.Enqueue(new ActivityQueueItem(activity));

        await controller.StartAsync();
        await controller.ApplyPendingSignalsAsync();

        var first = await queue.TickAsync();
        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.NotNull(queue.CurrentActivity);

        await controller.StopAsync();
        await controller.ApplyPendingSignalsAsync();

        Assert.Equal(RunLoopState.Stopping, controller.State);
        Assert.True(queue.IsGentleStopRequested);

        var stopResult = await queue.TickAsync();
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
            => Task.FromResult(true);

        public Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Continue("Running until safe stop boundary."));

        public Task OnHaltAsync(CancellationToken cancellationToken = default)
        {
            HaltCalled = true;
            return Task.CompletedTask;
        }
    }
}

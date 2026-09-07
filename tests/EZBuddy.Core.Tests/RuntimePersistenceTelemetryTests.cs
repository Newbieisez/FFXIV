using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;

namespace EZBuddy.Core.Tests;

public sealed class RuntimePersistenceTelemetryTests
{
    [Fact]
    public async Task Sink_DeduplicatesStatePersistsCompletionAndMarksCleanShutdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddyTests", Guid.NewGuid().ToString("N"));
        var checkpointPath = Path.Combine(root, "resume.json");
        var replayPath = Path.Combine(root, "replay.jsonl");
        var checkpointStore = new JsonResumeCheckpointStore(checkpointPath);
        var recorder = new JsonLinesDecisionReplayRecorder(replayPath);
        var sink = new RuntimePersistenceTelemetrySink(checkpointStore, recorder, "session-test");
        var activityId = Guid.NewGuid();
        var enqueued = DateTimeOffset.UtcNow.AddMinutes(-1);
        var started = DateTimeOffset.UtcNow.AddSeconds(-30);

        var running = new ActivityRuntimeSnapshot(
            activityId, "Mini Cactpot", ActivityCategory.GoldSaucer, ActivityState.Running,
            0, enqueued, started, null, "Executing.");

        await sink.PublishAsync(running, cancellationToken);
        await sink.PublishAsync(running, cancellationToken);

        var replayLines = await File.ReadAllLinesAsync(replayPath, cancellationToken);
        Assert.Single(replayLines);

        var completed = running with
        {
            State = ActivityState.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            LastMessage = "Ticket completed."
        };
        await sink.PublishAsync(completed, cancellationToken);

        var checkpoint = await checkpointStore.LoadAsync(cancellationToken);
        Assert.NotNull(checkpoint);
        Assert.False(checkpoint.CleanShutdown);
        Assert.Null(checkpoint.CurrentActivityKey);
        Assert.Contains("Mini Cactpot", checkpoint.CompletedRoutineKeys);

        await sink.MarkCleanShutdownAsync(cancellationToken);
        checkpoint = await checkpointStore.LoadAsync(cancellationToken);
        Assert.NotNull(checkpoint);
        Assert.True(checkpoint.CleanShutdown);
        Assert.Equal("Idle", checkpoint.CurrentStage);

        replayLines = await File.ReadAllLinesAsync(replayPath, cancellationToken);
        Assert.Equal(3, replayLines.Length);
    }
}

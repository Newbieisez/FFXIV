using System.Collections.Concurrent;
using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Runtime;

public interface IRuntimeSessionPersistence : IActivityTelemetrySink
{
    string SessionId { get; }
    Task MarkCleanShutdownAsync(CancellationToken cancellationToken = default);
}

public sealed class RuntimePersistenceTelemetrySink : IRuntimeSessionPersistence
{
    private readonly IResumeCheckpointStore _checkpointStore;
    private readonly IDecisionReplayRecorder _decisionRecorder;
    private readonly ConcurrentDictionary<Guid, string> _lastFingerprints = new();
    private readonly object _completedSync = new();
    private readonly HashSet<string> _completedActivities = new(StringComparer.OrdinalIgnoreCase);

    public RuntimePersistenceTelemetrySink(
        IResumeCheckpointStore checkpointStore,
        IDecisionReplayRecorder decisionRecorder,
        string? sessionId = null)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _decisionRecorder = decisionRecorder ?? throw new ArgumentNullException(nameof(decisionRecorder));
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
    }

    public string SessionId { get; }

    public async Task PublishAsync(ActivityRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var fingerprint = $"{snapshot.State}|{snapshot.Attempts}|{snapshot.LastMessage}";
        if (_lastFingerprints.TryGetValue(snapshot.ActivityId, out var previous) && string.Equals(previous, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _lastFingerprints[snapshot.ActivityId] = fingerprint;
        if (snapshot.State == ActivityState.Completed)
        {
            lock (_completedSync)
            {
                _completedActivities.Add(snapshot.Name);
            }
        }

        string[] completed;
        lock (_completedSync)
        {
            completed = _completedActivities.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var terminal = snapshot.State is ActivityState.Completed or ActivityState.Failed or ActivityState.Cancelled;
        var checkpoint = new ResumeCheckpoint(
            SessionId,
            terminal ? null : snapshot.Name,
            snapshot.State.ToString(),
            completed,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["activityId"] = snapshot.ActivityId.ToString("N"),
                ["category"] = snapshot.Category.ToString(),
                ["attempts"] = snapshot.Attempts.ToString(),
                ["message"] = snapshot.LastMessage
            },
            DateTimeOffset.UtcNow,
            CleanShutdown: false);

        await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        await _decisionRecorder.RecordAsync(new DecisionReplayEvent(
            "ActivityQueue",
            snapshot.State.ToString(),
            snapshot.LastMessage,
            terminal ? "Terminal" : "Active",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["activity"] = snapshot.Name,
                ["activityId"] = snapshot.ActivityId.ToString("N"),
                ["category"] = snapshot.Category.ToString(),
                ["attempts"] = snapshot.Attempts.ToString()
            }), cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkCleanShutdownAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _checkpointStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        string[] completed;
        lock (_completedSync)
        {
            completed = _completedActivities.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var checkpoint = existing is null
            ? new ResumeCheckpoint(SessionId, null, "Idle", completed, null, DateTimeOffset.UtcNow, CleanShutdown: true)
            : existing with
            {
                CurrentActivityKey = null,
                CurrentStage = "Idle",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                CleanShutdown = true
            };

        await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        await _decisionRecorder.RecordAsync(new DecisionReplayEvent(
            "Runtime",
            "Shutdown",
            "Host requested normal shutdown.",
            "Clean",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }
}

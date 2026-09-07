using System.Text.Json;

namespace EZBuddy.Core.Runtime;

public sealed record ResumeCheckpoint(
    string SessionId,
    string? CurrentActivityKey,
    string? CurrentStage,
    IReadOnlyList<string> CompletedRoutineKeys,
    IReadOnlyDictionary<string, string>? Metadata,
    DateTimeOffset UpdatedAtUtc,
    bool CleanShutdown = false);

public interface IResumeCheckpointStore
{
    Task<ResumeCheckpoint?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ResumeCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonResumeCheckpointStore : IResumeCheckpointStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonResumeCheckpointStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<ResumeCheckpoint?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                await using var stream = File.OpenRead(_path);
                return await JsonSerializer.DeserializeAsync<ResumeCheckpoint>(stream, Options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ResumeCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record DecisionReplayEvent(
    string Component,
    string Decision,
    string Reason,
    string Outcome,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, string>? Fields = null);

public interface IDecisionReplayRecorder
{
    Task RecordAsync(DecisionReplayEvent replayEvent, CancellationToken cancellationToken = default);
}

public sealed class JsonLinesDecisionReplayRecorder : IDecisionReplayRecorder
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLinesDecisionReplayRecorder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task RecordAsync(DecisionReplayEvent replayEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replayEvent);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(replayEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

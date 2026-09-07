using System.Text;
using System.Text.Json;

namespace EZBuddy.Core.Routines;

public sealed class JsonRoutineCompletionStore : IRoutineCompletionStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, RoutineCompletion>? _latest;

    public JsonRoutineCompletionStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public string FilePath => _filePath;

    public async Task<RoutineCompletion?> GetLatestAsync(
        string routineKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routineKey);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            _latest!.TryGetValue(routineKey, out var completion);
            return completion;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAsync(
        RoutineCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentException.ThrowIfNullOrWhiteSpace(completion.RoutineKey);
        if (completion.CompletedAtUtc == default)
        {
            throw new ArgumentException("Routine completion timestamp is required.", nameof(completion));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (_latest!.TryGetValue(completion.RoutineKey, out var existing) &&
                existing.CompletedAtUtc > completion.CompletedAtUtc)
            {
                return;
            }

            _latest[completion.RoutineKey] = completion with
            {
                RoutineKey = completion.RoutineKey.Trim(),
                CompletedAtUtc = completion.CompletedAtUtc.ToUniversalTime()
            };

            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_latest is not null)
        {
            return;
        }

        if (!File.Exists(_filePath))
        {
            _latest = new Dictionary<string, RoutineCompletion>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<RoutineCompletionDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Routine completion ledger is empty or invalid.");

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported routine completion ledger schema {document.SchemaVersion}.");
            }

            _latest = new Dictionary<string, RoutineCompletion>(StringComparer.OrdinalIgnoreCase);
            foreach (var completion in document.Completions)
            {
                if (string.IsNullOrWhiteSpace(completion.RoutineKey) || completion.CompletedAtUtc == default)
                {
                    throw new InvalidDataException("Routine completion ledger contains an invalid entry.");
                }

                if (!_latest.TryGetValue(completion.RoutineKey, out var existing) ||
                    completion.CompletedAtUtc >= existing.CompletedAtUtc)
                {
                    _latest[completion.RoutineKey] = completion with
                    {
                        CompletedAtUtc = completion.CompletedAtUtc.ToUniversalTime()
                    };
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Routine completion ledger is corrupt. EZBuddy will not silently reset routine history because that could repeat daily/weekly actions.",
                exception);
        }
    }

    private async Task SaveUnsafeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new RoutineCompletionDocument(
            CurrentSchemaVersion,
            _latest!.Values
                .OrderBy(completion => completion.RoutineKey, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var tempPath = _filePath + ".tmp";

        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed record RoutineCompletionDocument(
        int SchemaVersion,
        IReadOnlyList<RoutineCompletion> Completions);
}

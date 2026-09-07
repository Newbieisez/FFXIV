using System.Text;
using System.Text.Json;

namespace EZBuddy.Core.Safety;

public sealed record SessionSafetyConfiguration(
    bool Enabled = false,
    int MaximumContinuousRuntimeMinutes = 240,
    int BreakReminderAfterMinutes = 120,
    int StuckAfterNoProgressMinutes = 10,
    bool GentleStopAtMaximumRuntime = true,
    bool PauseWhenStuck = true,
    bool NotifyWhenQueueComplete = true)
{
    public static SessionSafetyConfiguration Default { get; } = new();

    public SessionSafetyPolicy ToPolicy()
    {
        Validate();
        return new SessionSafetyPolicy(
            TimeSpan.FromMinutes(MaximumContinuousRuntimeMinutes),
            TimeSpan.FromMinutes(BreakReminderAfterMinutes),
            TimeSpan.FromMinutes(StuckAfterNoProgressMinutes),
            GentleStopAtMaximumRuntime,
            PauseWhenStuck,
            NotifyWhenQueueComplete);
    }

    public void Validate()
    {
        if (MaximumContinuousRuntimeMinutes is < 1 or > 1440)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumContinuousRuntimeMinutes));
        }

        if (BreakReminderAfterMinutes is < 1 or > 1440 || BreakReminderAfterMinutes > MaximumContinuousRuntimeMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(BreakReminderAfterMinutes));
        }

        if (StuckAfterNoProgressMinutes is < 1 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(StuckAfterNoProgressMinutes));
        }
    }
}

public interface ISessionSafetyConfigurationStore
{
    string FilePath { get; }
    Task<SessionSafetyConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SessionSafetyConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed class JsonSessionSafetyConfigurationStore : ISessionSafetyConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSessionSafetyConfigurationStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<SessionSafetyConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return SessionSafetyConfiguration.Default;
            }

            try
            {
                var json = await File.ReadAllTextAsync(FilePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<SessionSafetyConfiguration>(json, JsonOptions);
                if (loaded is null)
                {
                    return SessionSafetyConfiguration.Default;
                }

                loaded.Validate();
                return loaded;
            }
            catch (JsonException)
            {
                return SessionSafetyConfiguration.Default;
            }
            catch (IOException)
            {
                return SessionSafetyConfiguration.Default;
            }
            catch (ArgumentOutOfRangeException)
            {
                return SessionSafetyConfiguration.Default;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SessionSafetyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(configuration, JsonOptions);
            var temp = FilePath + ".tmp";
            await File.WriteAllTextAsync(temp, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}

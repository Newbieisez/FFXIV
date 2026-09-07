using System.Text;
using System.Text.Json;
using EZBuddy.Core.Adapters;
using EZBuddy.Core.Duties;

namespace EZBuddy.Core.Settings;

public sealed record FirstPlayableLoopSettings(
    uint DutyId = 0,
    string DutyProfilePath = "",
    DutyAutomationMode DutyMode = DutyAutomationMode.DutySupport,
    int? TrustId = null,
    int? TargetLevel = null,
    int MaxRuns = 1,
    int MinimumDutyFreeSlots = 6,
    int InventoryTargetFreeSlots = 12,
    int MinimumRetainerFreeSlots = 8,
    bool AutoRepairGear = true,
    int AutoRepairThresholdPercent = 30,
    bool AutoExtractMateria = true,
    uint FoodItemId = 0,
    bool RequireWellFed = false,
    bool RunMaintenance = true,
    bool RunRetainers = true,
    bool RunInventoryPressureRelief = true,
    bool RunDailyProgression = true,
    bool RunDutyLoop = true,
    bool ReturnToIdle = true,
    IReadOnlyList<uint>? ApprovedExpertDeliveryItemIds = null,
    uint DutyTerritoryId = 0,
    DutyLootAction DutyLootAction = DutyLootAction.Greed,
    int DutyLootPassAtOrBelowFreeSlots = 3)
{
    // DutyId is retained for backward-compatible settings JSON. It specifically means
    // the RebornBuddy/Llama queue-registration ID, not the in-instance territory/map ID.
    public uint QueueDutyId => DutyId;

    public IReadOnlyList<uint> EffectiveApprovedExpertDeliveryItemIds =>
        ApprovedExpertDeliveryItemIds ?? Array.Empty<uint>();

    public DutyLootPolicy EffectiveDutyLootPolicy =>
        new(DutyLootAction, DutyLootPassAtOrBelowFreeSlots);

    public IReadOnlyList<string> Validate(bool requireDutyProfileExists = false)
    {
        var errors = new List<string>();

        if (RunDutyLoop)
        {
            if (QueueDutyId == 0)
            {
                errors.Add("Select a Duty Support/Trust queue registration ID before running the loop.");
            }

            if (string.IsNullOrWhiteSpace(DutyProfilePath))
            {
                errors.Add("Select a verified OrderBot duty profile before running the duty stage.");
            }
            else if (requireDutyProfileExists && !File.Exists(Path.GetFullPath(DutyProfilePath)))
            {
                errors.Add($"Duty profile does not exist: {DutyProfilePath}");
            }

            if (DutyMode == DutyAutomationMode.Trust && (!TrustId.HasValue || TrustId.Value <= 0))
            {
                errors.Add("Trust mode requires a positive Trust configuration ID.");
            }

            if (TargetLevel is < 1 or > 100)
            {
                errors.Add("Target level must be between 1 and 100 when configured.");
            }

            if (MaxRuns is < 1 or > 1000)
            {
                errors.Add("Maximum duty runs must be between 1 and 1000.");
            }

            if (!Enum.IsDefined(DutyLootAction))
            {
                errors.Add("Duty loot action is invalid.");
            }

            if (DutyLootPassAtOrBelowFreeSlots is < 0 or > 140)
            {
                errors.Add("Duty loot pass-at free-slot threshold must be between 0 and 140.");
            }
        }

        if (MinimumDutyFreeSlots is < 0 or > 140)
        {
            errors.Add("Minimum duty free inventory slots must be between 0 and 140.");
        }

        if (InventoryTargetFreeSlots is < 0 or > 140)
        {
            errors.Add("Inventory target free slots must be between 0 and 140.");
        }

        if (InventoryTargetFreeSlots < MinimumDutyFreeSlots)
        {
            errors.Add("Inventory target free slots cannot be lower than the duty safety floor.");
        }

        if (MinimumRetainerFreeSlots is < 0 or > 140)
        {
            errors.Add("Minimum retainer free inventory slots must be between 0 and 140.");
        }

        if (AutoRepairThresholdPercent is < 0 or > 100)
        {
            errors.Add("Auto-repair threshold must be between 0 and 100 percent.");
        }

        if (RequireWellFed && FoodItemId == 0)
        {
            errors.Add("Select a food item ID when Well Fed maintenance is enabled.");
        }

        if (EffectiveApprovedExpertDeliveryItemIds.Any(itemId => itemId == 0))
        {
            errors.Add("Approved Expert Delivery item IDs cannot contain zero.");
        }

        return errors;
    }
}

public sealed record EZBuddySettings(
    FirstPlayableLoopSettings FirstPlayableLoop,
    int SchemaVersion = 1)
{
    public static EZBuddySettings Default { get; } =
        new(new FirstPlayableLoopSettings());
}

public interface ISettingsStoragePathProvider
{
    string GetSettingsFilePath(string profileOrCharacterId);
}

public sealed class DefaultSettingsStoragePathProvider : ISettingsStoragePathProvider
{
    private readonly string _rootDirectory;

    public DefaultSettingsStoragePathProvider(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(Path.GetTempPath(), "EZBuddy", "Settings");
    }

    public string GetSettingsFilePath(string profileOrCharacterId)
    {
        var safeId = SettingsPathSanitizer.Sanitize(profileOrCharacterId);
        return Path.Combine(_rootDirectory, safeId + ".json");
    }
}

public static class SettingsPathSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Trim()
            .Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch)
            .ToArray();

        var safe = new string(chars).Trim();
        if (safe.Length == 0)
        {
            return "default";
        }

        return safe.Length <= 100 ? safe : safe[..100];
    }
}

public interface IEZBuddySettingsStore
{
    string FilePath { get; }
    Task<EZBuddySettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(EZBuddySettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonEZBuddySettingsStore : IEZBuddySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonEZBuddySettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EZBuddy",
            "EZBuddy.settings.json");
    }

    public JsonEZBuddySettingsStore(ISettingsStoragePathProvider pathProvider, string profileOrCharacterId)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        FilePath = pathProvider.GetSettingsFilePath(profileOrCharacterId);
    }

    public string FilePath { get; }

    public async Task<EZBuddySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return EZBuddySettings.Default;
            }

            try
            {
                var json = await File.ReadAllTextAsync(FilePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return EZBuddySettings.Default;
                }

                var settings = JsonSerializer.Deserialize<EZBuddySettings>(json, JsonOptions);
                return settings is { SchemaVersion: 1 }
                    ? settings
                    : EZBuddySettings.Default;
            }
            catch (JsonException)
            {
                return EZBuddySettings.Default;
            }
            catch (IOException)
            {
                return EZBuddySettings.Default;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(EZBuddySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalized = settings with { SchemaVersion = 1 };
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            var tempPath = FilePath + ".tmp";

            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}

public interface IObservableSettings : IAsyncDisposable
{
    EZBuddySettings Current { get; }
    string FilePath { get; }
    event EventHandler<EZBuddySettings>? SettingsChanged;
    Task LoadAsync(CancellationToken cancellationToken = default);
    void Update(Func<EZBuddySettings, EZBuddySettings> update);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonEZBuddySettingsManager : IObservableSettings
{
    private readonly object _sync = new();
    private readonly IEZBuddySettingsStore _store;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _saveDebounceCts;
    private Task _pendingSave = Task.CompletedTask;
    private EZBuddySettings _current = EZBuddySettings.Default;
    private bool _disposed;

    public JsonEZBuddySettingsManager(
        IEZBuddySettingsStore store,
        TimeSpan? debounce = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(650);
    }

    public EZBuddySettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public string FilePath => _store.FilePath;

    public event EventHandler<EZBuddySettings>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _current = loaded;
        }

        SettingsChanged?.Invoke(this, loaded);
    }

    public void Update(Func<EZBuddySettings, EZBuddySettings> update)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(update);

        EZBuddySettings next;
        lock (_sync)
        {
            next = update(_current) ?? throw new InvalidOperationException("Settings update returned null.");
            _current = next with { SchemaVersion = 1 };
            next = _current;
        }

        SettingsChanged?.Invoke(this, next);
        ScheduleDebouncedSave();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        CancellationTokenSource? debounce;
        Task pending;
        lock (_sync)
        {
            debounce = _saveDebounceCts;
            _saveDebounceCts = null;
            pending = _pendingSave;
        }

        debounce?.Cancel();
        debounce?.Dispose();

        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled debounce is expected when Flush forces an immediate atomic save.
        }

        await _store.SaveAsync(Current, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
        }
    }

    private void ScheduleDebouncedSave()
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            cts = new CancellationTokenSource();
            _saveDebounceCts = cts;
            _pendingSave = DebouncedSaveAsync(cts.Token);
        }
    }

    private async Task DebouncedSaveAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(Current, cancellationToken).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

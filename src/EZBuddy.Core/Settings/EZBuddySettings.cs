using System.Text;
using System.Text.Json;
using EZBuddy.Core.Adapters;

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
    uint FoodItemId = 0,
    bool RequireWellFed = false,
    bool RunMaintenance = true,
    bool RunRetainers = true,
    bool RunInventoryPressureRelief = true,
    bool RunDailyProgression = true,
    bool RunDutyLoop = true,
    bool ReturnToIdle = true,
    IReadOnlyList<uint>? ApprovedExpertDeliveryItemIds = null)
{
    public IReadOnlyList<uint> EffectiveApprovedExpertDeliveryItemIds =>
        ApprovedExpertDeliveryItemIds ?? Array.Empty<uint>();

    public IReadOnlyList<string> Validate(bool requireDutyProfileExists = false)
    {
        var errors = new List<string>();

        if (RunDutyLoop)
        {
            if (DutyId == 0)
            {
                errors.Add("Select a Duty Support/Trust duty before running the loop.");
            }

            if (string.IsNullOrWhiteSpace(DutyProfilePath))
            {
                errors.Add("Select a verified OrderBot duty profile before running the duty stage.");
            }
            else if (requireDutyProfileExists && !File.Exists(Path.GetFullPath(DutyProfilePath)))
            {
                errors.Add($"Duty profile does not exist: {DutyProfilePath}");
            }

            if (DutyMode == DutyAutomationMode.Trust && (!TrustId.HasValue || TrustId.Value < 0))
            {
                errors.Add("Trust mode requires a valid Trust configuration ID.");
            }

            if (TargetLevel is < 1 or > 100)
            {
                errors.Add("Target level must be between 1 and 100 when configured.");
            }

            if (MaxRuns is < 1 or > 1000)
            {
                errors.Add("Maximum duty runs must be between 1 and 1000.");
            }
        }

        if (MinimumDutyFreeSlots is < 0 or > 140)
        {
            errors.Add("Minimum free inventory slots must be between 0 and 140.");
        }

        if (InventoryTargetFreeSlots is < 0 or > 140)
        {
            errors.Add("Inventory target free slots must be between 0 and 140.");
        }

        if (InventoryTargetFreeSlots < MinimumDutyFreeSlots)
        {
            errors.Add("Inventory target free slots cannot be lower than the duty safety floor.");
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

    public JsonEZBuddySettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EZBuddy",
            "EZBuddy.settings.json");
    }

    public string FilePath { get; }

    public async Task<EZBuddySettings> LoadAsync(CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(EZBuddySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

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
}

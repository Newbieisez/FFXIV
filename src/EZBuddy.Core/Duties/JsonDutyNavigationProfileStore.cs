using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EZBuddy.Core.Duties;

public interface IDutyNavigationProfileStore
{
    string DirectoryPath { get; }
    IReadOnlyList<DutyNavigationProfile> LoadAll();
    DutyNavigationProfile? Load(uint dutyId);
    Task SaveAsync(DutyNavigationProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(uint dutyId, CancellationToken cancellationToken = default);
    Task<DutyNavigationProfile> ImportAsync(string json, CancellationToken cancellationToken = default);
    string Export(DutyNavigationProfile profile);
}

public sealed class JsonDutyNavigationProfileStore : IDutyNavigationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public JsonDutyNavigationProfileStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EZBuddy",
            "DutyProfiles");
    }

    public string DirectoryPath { get; }

    public IReadOnlyList<DutyNavigationProfile> LoadAll()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return Array.Empty<DutyNavigationProfile>();
        }

        var profiles = new List<DutyNavigationProfile>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var profile = Deserialize(File.ReadAllText(path, Encoding.UTF8));
                profiles.Add(profile);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
            {
                // A bad profile must not prevent valid profiles from loading.
            }
        }

        return profiles
            .OrderBy(profile => profile.MinimumLevel)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DutyNavigationProfile? Load(uint dutyId)
        => LoadAll().FirstOrDefault(profile => profile.DutyId == dutyId);

    public async Task SaveAsync(DutyNavigationProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = GetPath(profile.DutyId);
            var tempPath = path + ".tmp";
            var json = Export(profile);
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DeleteAsync(uint dutyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dutyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyId));
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPath(dutyId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<DutyNavigationProfile> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > 2_000_000)
        {
            throw new InvalidDataException("Duty navigation profile exceeds the 2 MB import limit.");
        }

        var profile = Deserialize(json);
        await SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public string Export(DutyNavigationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    private static DutyNavigationProfile Deserialize(string json)
    {
        var profile = JsonSerializer.Deserialize<DutyNavigationProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("Duty navigation profile JSON is empty or invalid.");
        profile.Validate();
        return profile;
    }

    private string GetPath(uint dutyId)
        => Path.Combine(DirectoryPath, $"duty-{dutyId}.json");
}

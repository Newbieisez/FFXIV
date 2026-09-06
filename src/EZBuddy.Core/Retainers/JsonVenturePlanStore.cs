using System.Text.Json;

namespace EZBuddy.Core.Retainers;

public sealed class JsonVenturePlanStore : IVenturePlanStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonVenturePlanStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EZBuddy",
            "venture-plans");
    }

    public IReadOnlyList<VenturePlan> GetAll()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<VenturePlan>();
        }

        var plans = new List<VenturePlan>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var plan = DeserializeAndValidate(File.ReadAllText(path));
                plans.Add(plan);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
            {
                // Invalid plan files are ignored by enumeration and can be surfaced by diagnostics.
            }
        }

        return plans.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public VenturePlan? Get(Guid id)
        => GetAll().FirstOrDefault(plan => plan.Id == id);

    public async Task SaveAsync(VenturePlan plan, CancellationToken cancellationToken = default)
    {
        Validate(plan);
        Directory.CreateDirectory(_directory);

        var path = GetPlanPath(plan.Id);
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(plan with { SchemaVersion = CurrentSchemaVersion }, _jsonOptions);

        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, true);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPlanPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<string> ExportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Get(id) ?? throw new FileNotFoundException($"Venture plan {id} was not found.");
        return await Task.FromResult(JsonSerializer.Serialize(plan, _jsonOptions)).ConfigureAwait(false);
    }

    public async Task<VenturePlan> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Venture plan import is empty.");
        }

        if (json.Length > 1_000_000)
        {
            throw new InvalidDataException("Venture plan import exceeds the maximum supported size.");
        }

        var imported = DeserializeAndValidate(json);
        var plan = imported with { Id = Guid.NewGuid(), SchemaVersion = CurrentSchemaVersion };
        await SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        return plan;
    }

    public VenturePlan Duplicate(Guid id, string? newName = null)
    {
        var source = Get(id) ?? throw new FileNotFoundException($"Venture plan {id} was not found.");
        var entries = source.Entries.Select(entry => entry with { Id = Guid.NewGuid() }).ToArray();
        return source with
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(newName) ? source.Name + " Copy" : newName.Trim(),
            Entries = entries,
            SchemaVersion = CurrentSchemaVersion
        };
    }

    private VenturePlan DeserializeAndValidate(string json)
    {
        var plan = JsonSerializer.Deserialize<VenturePlan>(json, _jsonOptions)
            ?? throw new InvalidDataException("Venture plan JSON did not contain a valid plan object.");
        Validate(plan);
        return plan;
    }

    private static void Validate(VenturePlan plan)
    {
        if (plan.SchemaVersion is < 1 or > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported venture plan schema version {plan.SchemaVersion}.");
        }

        if (plan.Id == Guid.Empty)
        {
            throw new InvalidDataException("Venture plan must have an ID.");
        }

        if (string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 100)
        {
            throw new InvalidDataException("Venture plan name must contain 1-100 characters.");
        }

        if (string.IsNullOrWhiteSpace(plan.RetainerJob) || plan.RetainerJob.Length > 50)
        {
            throw new InvalidDataException("Venture plan retainer job is invalid.");
        }

        if (plan.Entries.Count > 500)
        {
            throw new InvalidDataException("Venture plan exceeds the 500-entry limit.");
        }

        foreach (var entry in plan.Entries)
        {
            if (entry.Id == Guid.Empty)
            {
                throw new InvalidDataException("Every venture plan entry must have an ID.");
            }

            if (entry.ConditionThreshold is < 0)
            {
                throw new InvalidDataException("Venture condition thresholds cannot be negative.");
            }

            if (entry.Iterations is <= 0)
            {
                throw new InvalidDataException("Venture entry iterations must be positive when provided.");
            }
        }
    }

    private string GetPlanPath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".json");
}

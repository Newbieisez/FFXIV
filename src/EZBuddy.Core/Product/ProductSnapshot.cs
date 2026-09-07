using System.Text.Json;
using EZBuddy.Core.Collections;
using EZBuddy.Core.Economy;
using EZBuddy.Core.Gear;
using EZBuddy.Core.Materia;
using EZBuddy.Core.Procurement;

namespace EZBuddy.Core.Product;

public sealed record ProductSnapshotBundle(
    string CharacterKey,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<GearItemSnapshot> Gear,
    IReadOnlyList<CurrencySnapshot> Currencies,
    IReadOnlyList<CollectionItemSnapshot> Collections,
    IReadOnlyList<MateriaStock> Materia,
    IReadOnlyDictionary<uint, int> OwnedItems,
    IReadOnlyList<ProcurementRecipe> Recipes,
    IReadOnlyList<ProcurementSource> Sources,
    int SchemaVersion = 1)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CharacterKey);
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported Product Snapshot schema version {SchemaVersion}.");
        }

        foreach (var currency in Currencies)
        {
            currency.Validate();
        }

        foreach (var materia in Materia)
        {
            materia.Validate();
        }

        foreach (var recipe in Recipes)
        {
            recipe.Validate();
        }

        if (OwnedItems.Any(pair => pair.Key == 0 || pair.Value < 0))
        {
            throw new InvalidDataException("Owned-item quantities must use non-zero item IDs and non-negative quantities.");
        }
    }
}

public interface IProductSnapshotStore
{
    string FilePath { get; }
    Task<ProductSnapshotBundle?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ProductSnapshotBundle snapshot, CancellationToken cancellationToken = default);
}

public sealed class JsonProductSnapshotStore : IProductSnapshotStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonProductSnapshotStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<ProductSnapshotBundle?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            try
            {
                await using var stream = File.OpenRead(FilePath);
                var snapshot = await JsonSerializer.DeserializeAsync<ProductSnapshotBundle>(stream, Options, cancellationToken).ConfigureAwait(false);
                snapshot?.Validate();
                return snapshot;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ProductSnapshotBundle snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = FilePath + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record ProductAnalysisRequest(
    string JobKey,
    int MinimumKeepItemLevel,
    IReadOnlyDictionary<string, double>? GearStatWeights = null,
    IReadOnlyList<CurrencySpendOption>? CurrencySpendOptions = null,
    uint? ProcurementTargetItemId = null,
    int ProcurementTargetQuantity = 0,
    IReadOnlyList<GearMeldRequest>? MeldRequests = null);

public sealed record ProductAnalysisResult(
    GearPlan? Gear,
    CurrencyCapPlan Currency,
    IReadOnlyList<CollectionTarget> Collections,
    ProcurementPlan? Procurement,
    IReadOnlyList<MateriaPlan> Materia,
    IReadOnlyList<string> Warnings);

public static class ProductSnapshotAnalyzer
{
    public static ProductAnalysisResult Analyze(ProductSnapshotBundle snapshot, ProductAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        snapshot.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobKey);

        var warnings = new List<string>();
        GearPlan? gear = null;
        if (snapshot.Gear.Count > 0)
        {
            gear = SmartGearManager.Build(
                snapshot.Gear,
                new GearScoreProfile(request.JobKey, StatWeights: request.GearStatWeights),
                new GearManagerOptions(request.MinimumKeepItemLevel));
        }
        else
        {
            warnings.Add("Product Snapshot contains no gear rows.");
        }

        var currency = CurrencyCapManager.Build(
            snapshot.Currencies,
            request.CurrencySpendOptions ?? Array.Empty<CurrencySpendOption>());
        warnings.AddRange(currency.Warnings);

        var collectionTargets = CollectionCompletionPlanner.Rank(snapshot.Collections);

        ProcurementPlan? procurement = null;
        if (request.ProcurementTargetItemId is { } targetId && targetId != 0 && request.ProcurementTargetQuantity > 0)
        {
            procurement = ProcurementPlanner.Build(
                targetId,
                request.ProcurementTargetQuantity,
                snapshot.OwnedItems,
                snapshot.Recipes,
                snapshot.Sources);
            warnings.AddRange(procurement.Blockers);
        }

        var meldPlans = (request.MeldRequests ?? Array.Empty<GearMeldRequest>())
            .Select(meldRequest => MateriaPlanner.Build(meldRequest, snapshot.Materia))
            .ToArray();
        foreach (var plan in meldPlans.Where(plan => !plan.MeetsTargets))
        {
            warnings.Add($"Materia targets are not fully satisfied for {plan.Request.GearName}: " +
                         string.Join(", ", plan.UnmetTargets.Select(entry => $"{entry.Key} {entry.Value}")));
        }

        return new ProductAnalysisResult(gear, currency, collectionTargets, procurement, meldPlans, warnings.Distinct().ToArray());
    }
}

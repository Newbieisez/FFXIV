namespace EZBuddy.Core.Retainers;

public interface IRetainerBellLease : IAsyncDisposable
{
    string Owner { get; }
    DateTimeOffset AcquiredAt { get; }
}

public interface IRetainerBellCoordinator
{
    Task<IRetainerBellLease?> TryAcquireAsync(
        string owner,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record RetainerBellAccessResult(
    bool Success,
    bool UsedLocalBell,
    string Message);

public interface IRetainerBellAccess
{
    Task<RetainerBellAccessResult> EnsureBellOpenAsync(
        CancellationToken cancellationToken = default);
}

public interface IVentureCatalogue
{
    VentureDefinition? Get(uint ventureId);
    IReadOnlyList<VentureDefinition> GetForJob(string retainerJob);
}

public interface IVentureInventoryReader
{
    int GetPlayerQuantity(uint itemId);
    int GetRetainerQuantity(ulong retainerId, uint itemId);
}

public interface IVentureConditionProvider
{
    string Key { get; }
    bool IsAllowlisted { get; }
    Task<bool> EvaluateAsync(
        RetainerDescriptor retainer,
        VenturePlanEntry entry,
        CancellationToken cancellationToken = default);
}

public interface IRetainerJournal
{
    Task WriteAsync(VentureJournalEntry entry, CancellationToken cancellationToken = default);
}

public interface IVenturePlanStore
{
    IReadOnlyList<VenturePlan> GetAll();
    VenturePlan? Get(Guid id);
    Task SaveAsync(VenturePlan plan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<string> ExportAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VenturePlan> ImportAsync(string json, CancellationToken cancellationToken = default);
}

namespace EZBuddy.Core.Adapters;

public enum AdapterHealth
{
    Unknown,
    Missing,
    Ready,
    Busy,
    Degraded,
    Faulted
}

public sealed record AdapterStatus(
    string Key,
    string DisplayName,
    AdapterHealth Health,
    string Message,
    DateTimeOffset CheckedAt,
    Version? Version = null);

public enum DutyAutomationMode
{
    DutySupport,
    Trust
}

public sealed record DutyAutomationRequest(
    uint DutyId,
    DutyAutomationMode Mode,
    int? TrustId = null)
{
    // DutyId is retained for source compatibility. It specifically means the
    // RebornBuddy/Llama queue-registration ID, never the territory/map ID.
    public uint QueueDutyId => DutyId;
}

public sealed record DutyAutomationStatus(
    string State,
    bool IsQueued,
    bool CanCommence,
    bool IsJoining,
    bool IsInDungeon);

public interface IEZAdapter
{
    string Key { get; }
    string DisplayName { get; }
    Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public interface IMagitekAdapter : IEZAdapter
{
    bool IsRequiredForCombat { get; }
    Task<bool> IsCurrentRoutineAsync(CancellationToken cancellationToken = default);
}

public interface ILisbethAdapter : IEZAdapter
{
    Task<bool> ExecuteOrdersAsync(string ordersJson, CancellationToken cancellationToken = default);
    Task<bool> ExitCraftingAsync(CancellationToken cancellationToken = default);
    Task<bool> SelfRepairAsync(bool allowMenderFallback, CancellationToken cancellationToken = default);
    Task<bool> ExtractMateriaAsync(CancellationToken cancellationToken = default);
    Task<bool> TravelAsync(uint zoneId, float x, float y, float z, bool land = true, CancellationToken cancellationToken = default);
}

public interface IOrderBotAdapter : IEZAdapter
{
    Task<bool> LoadProfileAsync(string profilePathOrIdentifier, CancellationToken cancellationToken = default);
    Task<bool> IsProfileRunningAsync(CancellationToken cancellationToken = default);
}

public interface IDutySupportAdapter : IEZAdapter
{
    Task<DutyAutomationStatus> GetDutyStatusAsync(CancellationToken cancellationToken = default);

    // Registers or selects the requested Duty Support/Trust duty. QueueDutyId is the
    // RebornBuddy/Llama registration ID, not the territory/map ID used after zone-in.
    // This method must not wait for the full queue/zone-in lifecycle; subsequent entry
    // progress is advanced through AdvanceEntryAsync so the tick owner regains control.
    Task<bool> EnterAsync(DutyAutomationRequest request, CancellationToken cancellationToken = default);

    // Performs at most one entry-side action for the current tick (for example Commence).
    // Returns false only when the bridge cannot safely advance the current entry state.
    Task<bool> AdvanceEntryAsync(CancellationToken cancellationToken = default);
}

public interface IRetainerSweepAdapter : IEZAdapter
{
    Task<bool> SweepCompletedVenturesAsync(CancellationToken cancellationToken = default);
}

public interface IGrandCompanyAdapter : IEZAdapter
{
    Task<bool> EnsureVentureTokensAsync(
        uint ventureItemId,
        int currentQuantity,
        int targetQuantity,
        CancellationToken cancellationToken = default);

    Task<bool> RunExpertDeliveryAsync(
        IReadOnlyCollection<uint> approvedItemIds,
        CancellationToken cancellationToken = default);
}

public interface ICustomDeliveryAdapter : IEZAdapter
{
    Task<bool> RunSelectedAsync(
        IReadOnlyCollection<string> clientKeys,
        string craftingClassKey,
        CancellationToken cancellationToken = default);
}

public interface IPluginHookAdapter : IEZAdapter
{
    IReadOnlyCollection<string> SupportedOperations { get; }
    Task<object?> InvokeApprovedAsync(string operation, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default);
}

public interface IAdapterRegistry
{
    IReadOnlyCollection<IEZAdapter> All { get; }
    void Register(IEZAdapter adapter);
    bool TryGet<TAdapter>(string key, out TAdapter? adapter) where TAdapter : class, IEZAdapter;
    Task<IReadOnlyList<AdapterStatus>> GetStatusesAsync(CancellationToken cancellationToken = default);
}

public sealed class AdapterRegistry : IAdapterRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IEZAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IEZAdapter> All
    {
        get
        {
            lock (_sync)
            {
                return _adapters.Values.ToArray();
            }
        }
    }

    public void Register(IEZAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.Key);

        lock (_sync)
        {
            _adapters[adapter.Key] = adapter;
        }
    }

    public bool TryGet<TAdapter>(string key, out TAdapter? adapter) where TAdapter : class, IEZAdapter
    {
        lock (_sync)
        {
            if (_adapters.TryGetValue(key, out var found) && found is TAdapter typed)
            {
                adapter = typed;
                return true;
            }
        }

        adapter = null;
        return false;
    }

    public async Task<IReadOnlyList<AdapterStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var adapters = All;
        var tasks = adapters.Select(async adapter =>
        {
            try
            {
                return await adapter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return new AdapterStatus(adapter.Key, adapter.DisplayName, AdapterHealth.Faulted, exception.Message, DateTimeOffset.UtcNow);
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
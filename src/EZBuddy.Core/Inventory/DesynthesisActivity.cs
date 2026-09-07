using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Inventory;

public sealed record DesynthesisCandidate(
    string InstanceKey,
    InventoryItemSnapshot Item);

public sealed record DesynthesisActivityOptions(
    InventoryMaintenanceSettings Policy,
    int MaximumItemsPerRun = 100)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Policy);
        Policy.Validate();
        if (!Policy.AllowDesynthesis)
        {
            throw new ArgumentException("Desynthesis activity requires AllowDesynthesis=true.", nameof(Policy));
        }

        if (Policy.EffectiveDesynthesisAllowlist.Count == 0)
        {
            throw new ArgumentException("Desynthesis activity requires a non-empty explicit item allowlist.", nameof(Policy));
        }

        if (MaximumItemsPerRun is < 1 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumItemsPerRun));
        }
    }
}

public interface IDesynthesisHost
{
    Task<IReadOnlyList<DesynthesisCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default);

    Task<bool> DesynthesizeAsync(DesynthesisCandidate candidate, CancellationToken cancellationToken = default);

    Task<bool> IsBusyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes at most one explicitly allowlisted desynthesis operation per activity tick. The item
/// policy is re-evaluated immediately before every destructive action.
/// </summary>
public sealed class DesynthesisActivity : IEZActivity
{
    private readonly IDesynthesisHost _host;
    private readonly DesynthesisActivityOptions _options;
    private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
    private bool _complete;

    public DesynthesisActivity(IDesynthesisHost host, DesynthesisActivityOptions options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }
    public string Name => "Allowlisted Desynthesis";
    public ActivityCategory Category => ActivityCategory.Utility;
    public bool IsComplete => _complete;
    public int ProcessedCount => _processed.Count;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!_complete);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (_complete)
        {
            return ExecutionResult.Complete("Desynthesis pass is already complete.");
        }

        if (await _host.IsBusyAsync(cancellationToken))
        {
            return ExecutionResult.Yield("Desynthesis UI is busy; waiting for the current item operation.");
        }

        if (_processed.Count >= _options.MaximumItemsPerRun)
        {
            _complete = true;
            return ExecutionResult.Complete($"Desynthesis safety cap reached after {_processed.Count} item(s).");
        }

        var candidates = await _host.GetCandidatesAsync(cancellationToken);
        var eligible = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.InstanceKey))
            .Where(candidate => !_processed.Contains(candidate.InstanceKey))
            .Select(candidate => new
            {
                Candidate = candidate,
                Decision = InventoryMaintenancePolicy.Evaluate([candidate.Item], _options.Policy).Single()
            })
            .FirstOrDefault(entry => entry.Decision.Action == InventoryMaintenanceAction.Desynthesize);

        if (eligible is null)
        {
            _complete = true;
            return ExecutionResult.Complete($"Desynthesis pass complete; {_processed.Count} explicitly approved item(s) processed.");
        }

        // Re-check the exact candidate immediately before the destructive host call. Host adapters
        // should map InstanceKey to a stable bag/slot identity, not only ItemId.
        var current = (await _host.GetCandidatesAsync(cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.InstanceKey, eligible.Candidate.InstanceKey, StringComparison.Ordinal));
        if (current is null)
        {
            return ExecutionResult.Yield("Selected desynthesis candidate moved or disappeared; rescanning inventory.");
        }

        var finalDecision = InventoryMaintenancePolicy.Evaluate([current.Item], _options.Policy).Single();
        if (finalDecision.Action != InventoryMaintenanceAction.Desynthesize)
        {
            return ExecutionResult.Yield("Selected item no longer passes the desynthesis safety policy; rescanning inventory.");
        }

        var succeeded = await _host.DesynthesizeAsync(current, cancellationToken);
        if (!succeeded)
        {
            return ExecutionResult.Retry(
                $"Desynthesis did not complete for '{current.Item.Name}' ({current.InstanceKey}).",
                TimeSpan.FromSeconds(1));
        }

        _processed.Add(current.InstanceKey);
        return ExecutionResult.Continue(
            $"Desynthesized allowlisted item '{current.Item.Name}' ({_processed.Count}/{_options.MaximumItemsPerRun} safety cap).");
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

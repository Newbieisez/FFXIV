using EZBuddy.Core.Engine;

namespace EZBuddy.Core.GoldSaucer;

public sealed record MiniCactpotHostSnapshot(
    bool IsTicketOpen,
    int TicketsRemainingToday,
    IReadOnlyList<int?> Cells,
    bool PrizeReady = false,
    bool IsBusy = false);

public sealed record MiniCactpotActivityOptions(
    int MaximumTickets = 3,
    bool CloseWindowWhenComplete = true)
{
    public void Validate()
    {
        if (MaximumTickets is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumTickets), "Mini Cactpot supports 1-3 tickets per run.");
        }
    }
}

public interface IMiniCactpotHost
{
    Task<MiniCactpotHostSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    Task<bool> OpenNextTicketAsync(CancellationToken cancellationToken = default);

    Task<bool> RevealCellAsync(int cellIndex, CancellationToken cancellationToken = default);

    Task<bool> SelectLineAsync(IReadOnlyList<int> cellIndexes, CancellationToken cancellationToken = default);

    Task<bool> ClaimPrizeAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Tick-safe Mini Cactpot executor. Each ExecuteStepAsync call performs at most one UI-changing
/// operation so RebornBuddy remains the owner of the execution cadence.
/// </summary>
public sealed class MiniCactpotActivity : IEZActivity
{
    private readonly IMiniCactpotHost _host;
    private readonly MiniCactpotActivityOptions _options;
    private int _completedTickets;
    private bool _complete;

    public MiniCactpotActivity(IMiniCactpotHost host, MiniCactpotActivityOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new MiniCactpotActivityOptions();
        _options.Validate();
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public string Name => "Mini Cactpot";

    public ActivityCategory Category => ActivityCategory.GoldSaucer;

    public bool IsComplete => _complete;

    public int CompletedTickets => _completedTickets;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!_complete);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (_complete)
        {
            return ExecutionResult.Complete("Mini Cactpot run is already complete.");
        }

        var snapshot = await _host.ReadAsync(cancellationToken);
        ValidateSnapshot(snapshot);

        if (snapshot.IsBusy)
        {
            return ExecutionResult.Yield("Mini Cactpot window is busy; waiting for the current UI transition.");
        }

        if (!snapshot.IsTicketOpen)
        {
            if (_completedTickets >= _options.MaximumTickets || snapshot.TicketsRemainingToday <= 0)
            {
                await FinishAsync(cancellationToken);
                return ExecutionResult.Complete($"Mini Cactpot complete after {_completedTickets} ticket(s).");
            }

            var opened = await _host.OpenNextTicketAsync(cancellationToken);
            return opened
                ? ExecutionResult.Continue("Opened the next Mini Cactpot ticket.")
                : ExecutionResult.Yield("Mini Cactpot ticket is not ready to open yet.");
        }

        if (snapshot.PrizeReady)
        {
            var claimed = await _host.ClaimPrizeAsync(cancellationToken);
            if (!claimed)
            {
                return ExecutionResult.Yield("Mini Cactpot prize is ready but could not be claimed yet.");
            }

            _completedTickets++;
            return ExecutionResult.Continue($"Claimed Mini Cactpot ticket {_completedTickets} of {_options.MaximumTickets}.");
        }

        var revealedCount = snapshot.Cells.Count(cell => cell.HasValue);
        if (revealedCount < 4)
        {
            var recommendation = MiniCactpotSolver.RecommendNextReveal(snapshot.Cells);
            if (recommendation is null)
            {
                return ExecutionResult.Block("Mini Cactpot solver could not identify a valid reveal cell.");
            }

            var revealed = await _host.RevealCellAsync(recommendation.CellIndex, cancellationToken);
            return revealed
                ? ExecutionResult.Continue($"Revealed Mini Cactpot cell {recommendation.CellIndex}.")
                : ExecutionResult.Yield($"Mini Cactpot cell {recommendation.CellIndex} is not ready to reveal yet.");
        }

        var line = MiniCactpotSolver.RecommendLine(snapshot.Cells);
        var selected = await _host.SelectLineAsync(line.CellIndexes, cancellationToken);
        return selected
            ? ExecutionResult.Continue($"Selected Mini Cactpot line with expected payout {line.ExpectedPayout:0.##} MGP.")
            : ExecutionResult.Yield("Mini Cactpot line selection is not ready yet.");
    }

    public async Task OnHaltAsync(CancellationToken cancellationToken = default)
    {
        await _host.CloseAsync(cancellationToken);
    }

    private async Task FinishAsync(CancellationToken cancellationToken)
    {
        _complete = true;
        if (_options.CloseWindowWhenComplete)
        {
            await _host.CloseAsync(cancellationToken);
        }
    }

    private static void ValidateSnapshot(MiniCactpotHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Cells);

        if (snapshot.TicketsRemainingToday is < 0 or > 3)
        {
            throw new InvalidDataException("Mini Cactpot tickets remaining must be between 0 and 3.");
        }

        if (snapshot.Cells.Count != 9)
        {
            throw new InvalidDataException("Mini Cactpot host snapshots must contain exactly nine cells.");
        }

        var known = snapshot.Cells.Where(cell => cell.HasValue).Select(cell => cell!.Value).ToArray();
        if (known.Any(value => value is < 1 or > 9) || known.Distinct().Count() != known.Length)
        {
            throw new InvalidDataException("Mini Cactpot host snapshot contains invalid revealed digits.");
        }
    }
}

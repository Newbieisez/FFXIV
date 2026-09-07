using EZBuddy.Core.Engine;

namespace EZBuddy.Core.GoldSaucer;

public readonly record struct JumboCactpotNumber(int Digit1, int Digit2, int Digit3, int Digit4)
{
    public IReadOnlyList<int> Digits => [Digit1, Digit2, Digit3, Digit4];

    public void Validate()
    {
        if (Digits.Any(digit => digit is < 1 or > 9))
        {
            throw new ArgumentOutOfRangeException(nameof(JumboCactpotNumber), "Jumbo Cactpot digits must be between 1 and 9.");
        }
    }

    public override string ToString() => $"{Digit1}{Digit2}{Digit3}{Digit4}";
}

public sealed record JumboCactpotHostSnapshot(
    int TicketsOwnedForCurrentDrawing,
    int UnclaimedWinningTickets,
    bool CanPurchaseTickets,
    bool IsBusy = false);

public sealed record JumboCactpotActivityOptions(
    IReadOnlyList<JumboCactpotNumber> DesiredTickets,
    bool ClaimAvailablePrizes = true,
    bool CloseWindowWhenComplete = true)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(DesiredTickets);
        if (DesiredTickets.Count > JumboCactpotActivity.MaximumWeeklyTickets)
        {
            throw new ArgumentException($"At most {JumboCactpotActivity.MaximumWeeklyTickets} Jumbo Cactpot ticket numbers may be configured.", nameof(DesiredTickets));
        }

        foreach (var number in DesiredTickets)
        {
            number.Validate();
        }

        if (DesiredTickets.Distinct().Count() != DesiredTickets.Count)
        {
            throw new ArgumentException("Configured Jumbo Cactpot ticket numbers must be unique.", nameof(DesiredTickets));
        }
    }
}

public interface IJumboCactpotHost
{
    Task<JumboCactpotHostSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task<bool> ClaimNextPrizeAsync(CancellationToken cancellationToken = default);
    Task<bool> PurchaseTicketAsync(JumboCactpotNumber number, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Tick-safe Jumbo Cactpot executor. It performs at most one claim or ticket purchase per tick and
/// relies on the host for current drawing state rather than calculating data-center draw times.
/// </summary>
public sealed class JumboCactpotActivity : IEZActivity
{
    public const int MaximumWeeklyTickets = 3;

    private readonly IJumboCactpotHost _host;
    private readonly JumboCactpotActivityOptions _options;
    private bool _complete;

    public JumboCactpotActivity(IJumboCactpotHost host, JumboCactpotActivityOptions options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }
    public string Name => "Jumbo Cactpot";
    public ActivityCategory Category => ActivityCategory.GoldSaucer;
    public bool IsComplete => _complete;

    public Task<bool> CanExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!_complete);
    }

    public async Task<ExecutionResult> ExecuteStepAsync(CancellationToken cancellationToken = default)
    {
        if (_complete)
        {
            return ExecutionResult.Complete("Jumbo Cactpot routine is already complete.");
        }

        var snapshot = await _host.ReadAsync(cancellationToken);
        ValidateSnapshot(snapshot);

        if (snapshot.IsBusy)
        {
            return ExecutionResult.Yield("Jumbo Cactpot UI is busy; waiting for the current transition.");
        }

        if (_options.ClaimAvailablePrizes && snapshot.UnclaimedWinningTickets > 0)
        {
            var claimed = await _host.ClaimNextPrizeAsync(cancellationToken);
            return claimed
                ? ExecutionResult.Continue($"Claimed one Jumbo Cactpot ticket; {snapshot.UnclaimedWinningTickets - 1} prize ticket(s) remain to be processed.")
                : ExecutionResult.Yield("A Jumbo Cactpot prize is available but could not be claimed yet.");
        }

        if (snapshot.CanPurchaseTickets &&
            snapshot.TicketsOwnedForCurrentDrawing < MaximumWeeklyTickets &&
            snapshot.TicketsOwnedForCurrentDrawing < _options.DesiredTickets.Count)
        {
            var number = _options.DesiredTickets[snapshot.TicketsOwnedForCurrentDrawing];
            var purchased = await _host.PurchaseTicketAsync(number, cancellationToken);
            return purchased
                ? ExecutionResult.Continue($"Purchased Jumbo Cactpot ticket {snapshot.TicketsOwnedForCurrentDrawing + 1}: {number}.")
                : ExecutionResult.Yield($"Jumbo Cactpot ticket {snapshot.TicketsOwnedForCurrentDrawing + 1} could not be purchased yet.");
        }

        _complete = true;
        if (_options.CloseWindowWhenComplete)
        {
            await _host.CloseAsync(cancellationToken);
        }

        var message = snapshot.CanPurchaseTickets
            ? $"Jumbo Cactpot routine complete with {snapshot.TicketsOwnedForCurrentDrawing} ticket(s) registered for the current drawing."
            : "Jumbo Cactpot purchase window is not available and no configured prize claims remain.";
        return ExecutionResult.Complete(message);
    }

    public Task OnHaltAsync(CancellationToken cancellationToken = default)
        => _host.CloseAsync(cancellationToken);

    private static void ValidateSnapshot(JumboCactpotHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TicketsOwnedForCurrentDrawing is < 0 or > MaximumWeeklyTickets)
        {
            throw new InvalidDataException("Jumbo Cactpot host reported an invalid current ticket count.");
        }

        if (snapshot.UnclaimedWinningTickets is < 0 or > MaximumWeeklyTickets)
        {
            throw new InvalidDataException("Jumbo Cactpot host reported an invalid unclaimed ticket count.");
        }
    }
}

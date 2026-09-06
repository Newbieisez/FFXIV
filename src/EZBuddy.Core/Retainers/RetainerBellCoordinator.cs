namespace EZBuddy.Core.Retainers;

public sealed class RetainerBellCoordinator : IRetainerBellCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IRetainerBellLease?> TryAcquireAsync(
        string owner,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var acquired = await _gate.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return acquired ? new Lease(owner, _gate) : null;
    }

    private sealed class Lease : IRetainerBellLease
    {
        private SemaphoreSlim? _gate;

        public Lease(string owner, SemaphoreSlim gate)
        {
            Owner = owner;
            _gate = gate;
            AcquiredAt = DateTimeOffset.UtcNow;
        }

        public string Owner { get; }
        public DateTimeOffset AcquiredAt { get; }

        public ValueTask DisposeAsync()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
            return ValueTask.CompletedTask;
        }
    }
}

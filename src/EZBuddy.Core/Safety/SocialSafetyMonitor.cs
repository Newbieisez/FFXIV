using EZBuddy.Core.Engine;
using EZBuddy.Core.Notifications;

namespace EZBuddy.Core.Safety;

public enum SocialContactKind
{
    DirectTell,
    GmCommunication,
    TradeRequest
}

public sealed record SocialContactSignal(
    string Id,
    SocialContactKind Kind,
    string Sender,
    DateTimeOffset TimestampUtc,
    string? Message = null);

public sealed record SocialSafetyPolicy(
    bool PauseOnDirectTell = true,
    bool PauseOnGmCommunication = true,
    bool PauseOnRepeatedTradeRequests = true,
    int RepeatedTradeRequestThreshold = 2,
    TimeSpan? RepeatedTradeWindow = null)
{
    public TimeSpan TradeWindow => RepeatedTradeWindow ?? TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (RepeatedTradeRequestThreshold is < 2 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(RepeatedTradeRequestThreshold));
        }

        if (TradeWindow < TimeSpan.FromSeconds(5) || TradeWindow > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(RepeatedTradeWindow));
        }
    }
}

public interface ISocialContactSource
{
    Task<IReadOnlyList<SocialContactSignal>> PollAsync(CancellationToken cancellationToken = default);
}

public sealed record SocialSafetyPollResult(
    int NewSignals,
    bool PauseRequested,
    IReadOnlyList<SocialContactSignal> ReviewSignals);

/// <summary>
/// Passive social-contact safety policy. It never logs off, moves, replies, or attempts to conceal
/// automation. High-risk contact is converted into a cooperative pause request and a user-review
/// notification.
/// </summary>
public sealed class SocialSafetyMonitor
{
    private const int MaximumRememberedSignals = 2_000;

    private readonly ISocialContactSource _source;
    private readonly IRunLoopController _runLoop;
    private readonly INotificationSink _notifications;
    private readonly SocialSafetyPolicy _policy;
    private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
    private readonly Queue<string> _processedOrder = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _tradeRequests = new(StringComparer.OrdinalIgnoreCase);

    public SocialSafetyMonitor(
        ISocialContactSource source,
        IRunLoopController runLoop,
        INotificationSink? notifications = null,
        SocialSafetyPolicy? policy = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _runLoop = runLoop ?? throw new ArgumentNullException(nameof(runLoop));
        _notifications = notifications ?? NullNotificationSink.Instance;
        _policy = policy ?? new SocialSafetyPolicy();
        _policy.Validate();
    }

    public async Task<SocialSafetyPollResult> PollAsync(CancellationToken cancellationToken = default)
    {
        var signals = await _source.PollAsync(cancellationToken).ConfigureAwait(false);
        var review = new List<SocialContactSignal>();
        var pauseRequested = false;
        var newSignals = 0;

        foreach (var signal in signals.OrderBy(signal => signal.TimestampUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSignal(signal);
            if (!Remember(signal.Id))
            {
                continue;
            }

            newSignals++;
            var requiresPause = EvaluatePause(signal);
            if (requiresPause)
            {
                review.Add(signal);
                pauseRequested = true;
            }

            await _notifications.SendAsync(
                BuildNotification(signal, requiresPause),
                cancellationToken).ConfigureAwait(false);
        }

        if (pauseRequested)
        {
            await _runLoop.PauseAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SocialSafetyPollResult(newSignals, pauseRequested, review);
    }

    private bool EvaluatePause(SocialContactSignal signal)
        => signal.Kind switch
        {
            SocialContactKind.DirectTell => _policy.PauseOnDirectTell,
            SocialContactKind.GmCommunication => _policy.PauseOnGmCommunication,
            SocialContactKind.TradeRequest => EvaluateTradeRequest(signal),
            _ => false
        };

    private bool EvaluateTradeRequest(SocialContactSignal signal)
    {
        if (!_tradeRequests.TryGetValue(signal.Sender, out var timestamps))
        {
            timestamps = new Queue<DateTimeOffset>();
            _tradeRequests[signal.Sender] = timestamps;
        }

        timestamps.Enqueue(signal.TimestampUtc);
        var cutoff = signal.TimestampUtc - _policy.TradeWindow;
        while (timestamps.Count > 0 && timestamps.Peek() < cutoff)
        {
            timestamps.Dequeue();
        }

        return _policy.PauseOnRepeatedTradeRequests &&
               timestamps.Count >= _policy.RepeatedTradeRequestThreshold;
    }

    private bool Remember(string id)
    {
        if (!_processed.Add(id))
        {
            return false;
        }

        _processedOrder.Enqueue(id);
        while (_processedOrder.Count > MaximumRememberedSignals)
        {
            _processed.Remove(_processedOrder.Dequeue());
        }

        return true;
    }

    private static EZNotification BuildNotification(SocialContactSignal signal, bool requiresPause)
    {
        var title = signal.Kind switch
        {
            SocialContactKind.DirectTell => "Direct tell received",
            SocialContactKind.GmCommunication => "GM communication detected",
            SocialContactKind.TradeRequest => "Trade request detected",
            _ => "Social contact detected"
        };

        var message = string.IsNullOrWhiteSpace(signal.Message)
            ? $"{signal.Kind} from {signal.Sender}."
            : $"{signal.Kind} from {signal.Sender}: {signal.Message}";

        return new EZNotification(
            NotificationEventType.SocialContact,
            title,
            message,
            signal.TimestampUtc,
            new Dictionary<string, string>
            {
                ["sender"] = signal.Sender,
                ["kind"] = signal.Kind.ToString()
            },
            RequiresUserReview: requiresPause);
    }

    private static void ValidateSignal(SocialContactSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.Sender);
    }
}

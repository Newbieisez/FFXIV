using EZBuddy.Core.Engine;
using EZBuddy.Core.Notifications;

namespace EZBuddy.Core.Safety;

public sealed record SessionSafetyExecutionResult(
    SessionSafetyDecision Decision,
    bool ActionApplied,
    string Message);

/// <summary>
/// Applies already-evaluated session safety decisions to the shared run loop. The coordinator
/// deliberately contains no timing/randomization logic; it only executes fixed user-configured
/// safety decisions and deduplicates identical consecutive actions.
/// </summary>
public sealed class SessionSafetyCoordinator
{
    private readonly IRunLoopController _runLoop;
    private readonly INotificationSink _notifications;
    private readonly object _sync = new();
    private string? _lastAppliedKey;

    public SessionSafetyCoordinator(
        IRunLoopController runLoop,
        INotificationSink? notifications = null)
    {
        _runLoop = runLoop ?? throw new ArgumentNullException(nameof(runLoop));
        _notifications = notifications ?? NullNotificationSink.Instance;
    }

    public async Task<SessionSafetyExecutionResult> ApplyAsync(
        SessionSafetyDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        if (decision.Action == SessionSafetyAction.None)
        {
            ClearDeduplication();
            return new SessionSafetyExecutionResult(decision, false, decision.Message);
        }

        var key = $"{decision.Action}|{decision.Message}";
        if (!TryMarkApplied(key))
        {
            return new SessionSafetyExecutionResult(
                decision,
                false,
                $"Session safety action '{decision.Action}' was already applied for the current condition.");
        }

        switch (decision.Action)
        {
            case SessionSafetyAction.RecommendBreak:
                await SendNotificationAsync(
                    "Break reminder",
                    decision.Message,
                    requiresReview: false,
                    cancellationToken).ConfigureAwait(false);
                break;

            case SessionSafetyAction.RequestGentleStop:
                await _runLoop.StopAsync(cancellationToken).ConfigureAwait(false);
                await SendNotificationAsync(
                    "Session limit reached",
                    decision.Message,
                    requiresReview: true,
                    cancellationToken).ConfigureAwait(false);
                break;

            case SessionSafetyAction.PauseForStuckReview:
                await _runLoop.PauseAsync(cancellationToken).ConfigureAwait(false);
                await SendNotificationAsync(
                    "EZBuddy paused for stuck review",
                    decision.Message,
                    requiresReview: true,
                    cancellationToken).ConfigureAwait(false);
                break;

            case SessionSafetyAction.QueueComplete:
                await SendNotificationAsync(
                    "EZBuddy queue complete",
                    decision.Message,
                    requiresReview: false,
                    cancellationToken,
                    NotificationEventType.QueueComplete).ConfigureAwait(false);
                break;

            default:
                ClearDeduplication();
                return new SessionSafetyExecutionResult(decision, false, "No session safety action was applied.");
        }

        return new SessionSafetyExecutionResult(decision, true, decision.Message);
    }

    private async Task SendNotificationAsync(
        string title,
        string message,
        bool requiresReview,
        CancellationToken cancellationToken,
        NotificationEventType eventType = NotificationEventType.Information)
    {
        await _notifications.SendAsync(
            new EZNotification(
                eventType,
                title,
                message,
                DateTimeOffset.UtcNow,
                RequiresUserReview: requiresReview),
            cancellationToken).ConfigureAwait(false);
    }

    private bool TryMarkApplied(string key)
    {
        lock (_sync)
        {
            if (string.Equals(_lastAppliedKey, key, StringComparison.Ordinal))
            {
                return false;
            }

            _lastAppliedKey = key;
            return true;
        }
    }

    private void ClearDeduplication()
    {
        lock (_sync)
        {
            _lastAppliedKey = null;
        }
    }
}

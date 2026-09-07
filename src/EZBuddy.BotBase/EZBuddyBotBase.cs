using Buddy.Coroutines;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;
using EZBuddy.Core.Safety;
using EZBuddy.Core.Settings;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Settings;
using ff14bot.Behavior;
using TreeSharp;

namespace EZBuddy.BotBase;

public sealed class EZBuddyBotBase : ff14bot.AClasses.BotBase
{
    private Composite? _root;
    private CancellationTokenSource? _runCancellation;
    private SessionSafetyConfiguration _sessionSafetyConfiguration = SessionSafetyConfiguration.Default;
    private SessionSafetyCoordinator? _sessionSafetyCoordinator;
    private DateTimeOffset _lastSafetyPulseUtc;
    private DateTimeOffset _lastMeaningfulProgressUtc;
    private TimeSpan _activeRuntime;
    private string? _lastProgressFingerprint;
    private bool _observedWorkThisSession;

    public override string Name => "EZBuddy";
    public override bool IsAutonomous => true;
    public override PulseFlags PulseFlags => PulseFlags.All;
    public override bool RequiresProfile => false;
    public override Composite Root => _root ??= new ActionRunCoroutine(_ => PulseQueueAsync());

    public override void Start()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _runCancellation, next);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        _ = RebornBuddySettingsSession.GetOrCreate();
        InitializeSessionSafety();
        _ = EZBuddyRuntime.RunLoop.StartAsync();
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase started with shared per-character settings, session/social safety hooks, and a new execution cancellation scope.");
    }

    public override void Stop()
    {
        if (HostLifecycleTransition.IsInternalTransition)
        {
            ff14bot.Helpers.Logging.Write("[EZBuddy] Internal botbase handoff detected; queue state and active activity were preserved.");
            return;
        }

        _ = EZBuddyRuntime.RunLoop.StopAsync();

        try
        {
            RebornBuddySettingsSession.FlushPendingSavesAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Settings] Stop flush failed: {exception.Message}");
        }

        var cancellation = Interlocked.Exchange(ref _runCancellation, null);
        cancellation?.Cancel();

        EZBuddyRuntime.RunLoop.NotifyHostStopRequested();
        EZBuddyRuntime.Queue.Pause();
        _ = StopQueueAsync(cancellation);
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase stop requested. Settings flushed, active activity cancellation propagated, and pending queue preserved.");
    }

    private async Task<bool> PulseQueueAsync()
    {
        var cancellation = Volatile.Read(ref _runCancellation);
        if (cancellation is null || cancellation.IsCancellationRequested)
        {
            await Coroutine.Yield();
            return false;
        }

        var token = cancellation.Token;
        var runLoop = EZBuddyRuntime.RunLoop;
        var queue = EZBuddyRuntime.Queue;

        try
        {
            await PollSocialSafetyAsync(runLoop, token).ConfigureAwait(true);

            var current = queue.CurrentActivity;
            if (current is not null && RequiresMagitek(current.Category) && !MagitekAdapter.IsActive(out var diagnostic))
            {
                ff14bot.Helpers.Logging.Write($"[EZBuddy Combat Guard] {diagnostic}");
                queue.Pause();
                runLoop.ObserveEngineState();
                await Coroutine.Yield();
                return false;
            }

            var result = await runLoop.TickAsync(token).ConfigureAwait(true);
            await ApplySessionSafetyAsync(queue, runLoop, token).ConfigureAwait(true);

            if (result.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            {
                await Task.Delay(retryAfter, token).ConfigureAwait(true);
            }
            else
            {
                await Coroutine.Yield();
            }

            return result.Disposition is not ExecutionDisposition.Fail and not ExecutionDisposition.Block;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task PollSocialSafetyAsync(
        IRunLoopController runLoop,
        CancellationToken cancellationToken)
    {
        var monitor = SocialSafetyRuntime.Monitor;
        if (monitor is null)
        {
            return;
        }

        try
        {
            var result = await monitor.PollAsync(cancellationToken).ConfigureAwait(true);
            if (result.PauseRequested)
            {
                ff14bot.Helpers.Logging.Write($"[EZBuddy Social Safety] Pausing for user review after {result.ReviewSignals.Count} inbound contact signal(s).");
                await runLoop.ApplyPendingSignalsAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Social Safety] Poll failed without affecting the activity engine: {exception.Message}");
        }
    }

    private void InitializeSessionSafety()
    {
        var now = DateTimeOffset.UtcNow;
        _lastSafetyPulseUtc = now;
        _lastMeaningfulProgressUtc = now;
        _activeRuntime = TimeSpan.Zero;
        _lastProgressFingerprint = null;
        _observedWorkThisSession = false;
        _sessionSafetyCoordinator = new SessionSafetyCoordinator(EZBuddyRuntime.RunLoop, EZBuddyRuntime.Notifications);
        _sessionSafetyConfiguration = SessionSafetyConfiguration.Default;

        try
        {
            var characterKey = SettingsPathSanitizer.Sanitize(ff14bot.Core.Player?.Name ?? "default");
            var path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Settings",
                "EZBuddy",
                "Safety",
                characterKey + ".session-safety.json");
            var store = new JsonSessionSafetyConfigurationStore(path);
            _sessionSafetyConfiguration = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(path))
            {
                store.SaveAsync(_sessionSafetyConfiguration, CancellationToken.None).GetAwaiter().GetResult();
            }

            ff14bot.Helpers.Logging.Write(
                _sessionSafetyConfiguration.Enabled
                    ? $"[EZBuddy Safety] Session safety enabled from {path}."
                    : $"[EZBuddy Safety] Session safety configuration created/loaded but remains opt-in disabled: {path}.");
        }
        catch (Exception exception)
        {
            _sessionSafetyConfiguration = SessionSafetyConfiguration.Default;
            ff14bot.Helpers.Logging.Write($"[EZBuddy Safety] Session safety configuration failed closed: {exception.Message}");
        }
    }

    private async Task ApplySessionSafetyAsync(
        ActivityQueueEngine queue,
        IRunLoopController runLoop,
        CancellationToken cancellationToken)
    {
        if (!_sessionSafetyConfiguration.Enabled || _sessionSafetyCoordinator is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsedSincePulse = now - _lastSafetyPulseUtc;
        _lastSafetyPulseUtc = now;

        if (runLoop.State != RunLoopState.Running)
        {
            _lastMeaningfulProgressUtc = now;
            _lastProgressFingerprint = BuildProgressFingerprint(queue.GetSnapshots());
            return;
        }

        if (elapsedSincePulse > TimeSpan.Zero && elapsedSincePulse < TimeSpan.FromMinutes(5))
        {
            _activeRuntime += elapsedSincePulse;
        }

        var snapshots = queue.GetSnapshots();
        var activeSnapshots = snapshots
            .Where(snapshot => snapshot.State is
                ActivityState.Pending or
                ActivityState.Waiting or
                ActivityState.Running or
                ActivityState.GentleStopping or
                ActivityState.Blocked)
            .ToArray();

        if (activeSnapshots.Length > 0 || snapshots.Any(snapshot => snapshot.CompletedAt >= now - elapsedSincePulse))
        {
            _observedWorkThisSession = true;
        }

        var fingerprint = BuildProgressFingerprint(snapshots);
        if (!string.Equals(_lastProgressFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _lastProgressFingerprint = fingerprint;
            _lastMeaningfulProgressUtc = now;
        }

        var currentId = queue.CurrentActivityId;
        var currentSnapshot = currentId.HasValue
            ? snapshots.FirstOrDefault(snapshot => snapshot.ActivityId == currentId.Value)
            : null;

        var decision = SessionSafetyEvaluator.Evaluate(
            _sessionSafetyConfiguration.ToPolicy(),
            new SessionSafetySnapshot(
                _activeRuntime,
                now,
                _lastMeaningfulProgressUtc,
                QueueStarted: _observedWorkThisSession,
                QueueHasPendingOrActiveWork: activeSnapshots.Length > 0,
                ActivityExpectedToProgress: currentSnapshot?.State == ActivityState.Running,
                CharacterMoving: false,
                CurrentActivityName: currentSnapshot?.Name));

        var applied = await _sessionSafetyCoordinator.ApplyAsync(decision, cancellationToken).ConfigureAwait(true);
        if (applied.ActionApplied)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Safety] {applied.Message}");
            await runLoop.ApplyPendingSignalsAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private static string BuildProgressFingerprint(IReadOnlyList<ActivityRuntimeSnapshot> snapshots)
        => string.Join(
            "|",
            snapshots
                .Where(snapshot => snapshot.State is
                    ActivityState.Pending or
                    ActivityState.Waiting or
                    ActivityState.Running or
                    ActivityState.GentleStopping or
                    ActivityState.Blocked ||
                    snapshot.CompletedAt.HasValue)
                .OrderBy(snapshot => snapshot.EnqueuedAt)
                .ThenBy(snapshot => snapshot.ActivityId)
                .Select(snapshot => $"{snapshot.ActivityId:N}:{snapshot.State}:{snapshot.Attempts}:{snapshot.LastMessage}"));

    private static async Task StopQueueAsync(CancellationTokenSource? cancellation)
    {
        try
        {
            await EZBuddyRuntime.Queue.StopAsync(
                preservePendingQueue: true,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy] Stop cleanup failed: {exception.Message}");
        }
        finally
        {
            EZBuddyRuntime.RunLoop.NotifyHostStopped();
            cancellation?.Dispose();
        }
    }

    private static bool RequiresMagitek(ActivityCategory category)
        => category is ActivityCategory.Duty or ActivityCategory.Questing;
}

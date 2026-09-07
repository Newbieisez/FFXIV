using Buddy.Coroutines;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Settings;
using ff14bot.Behavior;
using TreeSharp;

namespace EZBuddy.BotBase;

public sealed class EZBuddyBotBase : ff14bot.AClasses.BotBase
{
    private Composite? _root;
    private CancellationTokenSource? _runCancellation;

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
        _ = EZBuddyRuntime.RunLoop.StartAsync();
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase started with shared per-character settings and a new execution cancellation scope.");
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

        var current = queue.CurrentActivity;
        if (current is not null && RequiresMagitek(current.Category) && !MagitekAdapter.IsActive(out var diagnostic))
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Combat Guard] {diagnostic}");
            queue.Pause();
            runLoop.ObserveEngineState();
            await Coroutine.Yield();
            return false;
        }

        try
        {
            var result = await runLoop.TickAsync(token).ConfigureAwait(true);

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

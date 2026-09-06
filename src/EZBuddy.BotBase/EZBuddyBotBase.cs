using Buddy.Coroutines;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
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

        EZBuddyRuntime.Queue.Start();
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase started with a new execution cancellation scope.");
    }

    public override void Stop()
    {
        var cancellation = Interlocked.Exchange(ref _runCancellation, null);
        cancellation?.Cancel();

        EZBuddyRuntime.Queue.Pause();
        _ = StopQueueAsync(cancellation);
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase stop requested. Active activity cancellation propagated; pending queue preserved.");
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
        var queue = EZBuddyRuntime.Queue;
        if (!queue.IsRunning)
        {
            await Coroutine.Yield();
            return false;
        }

        var current = queue.CurrentActivity;
        if (current is not null && RequiresMagitek(current.Category) && !MagitekAdapter.IsActive(out var diagnostic))
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Combat Guard] {diagnostic}");
            queue.Pause();
            await Coroutine.Yield();
            return false;
        }

        try
        {
            var result = await queue.TickAsync(token).ConfigureAwait(true);
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
            ff14bot.Helpers.Logging.Write($"[EZBuddy] Queue stop cleanup failed: {exception.Message}");
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    private static bool RequiresMagitek(ActivityCategory category)
        => category is ActivityCategory.Duty or ActivityCategory.Questing;
}

using Buddy.Coroutines;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using ff14bot.AClasses;
using ff14bot.Behavior;
using TreeSharp;

namespace EZBuddy.BotBase;

public sealed class EZBuddyBotBase : BotBase
{
    private Composite? _root;

    public override string Name => "EZBuddy";
    public override bool IsAutonomous => true;
    public override PulseFlags PulseFlags => PulseFlags.All;
    public override bool RequiresProfile => false;
    public override Composite Root => _root ??= new ActionRunCoroutine(_ => PulseQueueAsync());

    public override void Start()
    {
        EZBuddyRuntime.Queue.Start();
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase started.");
    }

    public override void Stop()
    {
        EZBuddyRuntime.Queue.Pause();
        _ = EZBuddyRuntime.Queue.StopAsync(preservePendingQueue: true);
        ff14bot.Helpers.Logging.Write("[EZBuddy] BotBase stopped. Pending queue preserved.");
    }

    private static async Task<bool> PulseQueueAsync()
    {
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

        var result = await queue.TickAsync().ConfigureAwait(true);
        if (result.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            var delay = (int)Math.Min(retryAfter.TotalMilliseconds, int.MaxValue);
            await Coroutine.Sleep(delay);
        }
        else
        {
            await Coroutine.Yield();
        }

        return result.Disposition is not ExecutionDisposition.Fail and not ExecutionDisposition.Block;
    }

    private static bool RequiresMagitek(ActivityCategory category)
        => category is ActivityCategory.Duty or ActivityCategory.Questing;
}

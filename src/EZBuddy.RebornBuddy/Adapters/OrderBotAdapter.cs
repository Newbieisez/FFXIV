using EZBuddy.Core.Adapters;
using EZBuddy.Core.Runtime;
using ff14bot;
using ff14bot.AClasses;
using ff14bot.Managers;
using ff14bot.NeoProfiles;

namespace EZBuddy.RebornBuddy.Adapters;

public sealed class OrderBotAdapter : IOrderBotAdapter
{
    public const string AdapterKey = "orderbot";
    private readonly object _sync = new();
    private Task? _handoffTask;
    private Exception? _lastError;
    private string? _activeProfilePath;

    public string Key => AdapterKey;
    public string DisplayName => "Order Bot";

    public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var orderBot = FindOrderBot();
        if (orderBot is null)
        {
            return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Missing, "Order Bot botbase was not found.", DateTimeOffset.UtcNow));
        }

        lock (_sync)
        {
            if (_handoffTask is { IsCompleted: false })
            {
                return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Busy, $"Running generated profile '{Path.GetFileName(_activeProfilePath)}'.", DateTimeOffset.UtcNow));
            }

            if (_lastError is not null)
            {
                return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Degraded, _lastError.Message, DateTimeOffset.UtcNow));
            }
        }

        return Task.FromResult(new AdapterStatus(Key, DisplayName, AdapterHealth.Ready, "Order Bot is available for profile handoff.", DateTimeOffset.UtcNow));
    }

    public Task<bool> LoadProfileAsync(string profilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

        if (!File.Exists(profilePath) || FindOrderBot() is null)
        {
            return Task.FromResult(false);
        }

        lock (_sync)
        {
            if (_handoffTask is { IsCompleted: false })
            {
                return Task.FromResult(false);
            }

            _lastError = null;
            _activeProfilePath = profilePath;
            _handoffTask = Task.Run(() => RunProfileHandoffAsync(profilePath, cancellationToken), CancellationToken.None);
            return Task.FromResult(true);
        }
    }

    public Task<bool> IsProfileRunningAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(_handoffTask is { IsCompleted: false });
        }
    }

    private async Task RunProfileHandoffAsync(string profilePath, CancellationToken cancellationToken)
    {
        BotBase? previousBot = null;
        try
        {
            var orderBot = FindOrderBot() ?? throw new InvalidOperationException("Order Bot botbase is unavailable.");
            previousBot = BotManager.Current;

            if (TreeRoot.IsRunning)
            {
                using (HostLifecycleTransition.BeginInternalTransition())
                {
                    TreeRoot.Stop("EZBuddy generated profile handoff");
                    await WaitUntilAsync(() => !TreeRoot.IsRunning, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            BotManager.SetCurrent(orderBot);
            NeoProfileManager.Load(profilePath);
            NeoProfileManager.UpdateCurrentProfileBehavior();
            TreeRoot.Start();

            await WaitUntilAsync(() => TreeRoot.IsRunning, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            await WaitUntilAsync(() => !TreeRoot.IsRunning, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a normal external stop/shutdown path.
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _lastError = exception;
            }

            ff14bot.Helpers.Logging.Write($"[EZBuddy OrderBot] {exception.Message}");
        }
        finally
        {
            try
            {
                if (TreeRoot.IsRunning)
                {
                    TreeRoot.Stop("EZBuddy restoring previous botbase");
                    await WaitUntilAsync(() => !TreeRoot.IsRunning, TimeSpan.FromSeconds(20), CancellationToken.None).ConfigureAwait(false);
                }

                if (previousBot is not null)
                {
                    BotManager.SetCurrent(previousBot);
                    TreeRoot.Start();
                }
            }
            catch (Exception restoreException)
            {
                lock (_sync)
                {
                    _lastError = restoreException;
                }

                ff14bot.Helpers.Logging.Write($"[EZBuddy OrderBot] Failed to restore previous botbase: {restoreException.Message}");
            }
            finally
            {
                lock (_sync)
                {
                    _activeProfilePath = null;
                }
            }
        }
    }

    private static BotBase? FindOrderBot()
        => BotManager.Bots.FirstOrDefault(bot =>
            bot.EnglishName.Contains("Order Bot", StringComparison.OrdinalIgnoreCase) ||
            bot.Name.Contains("Order Bot", StringComparison.OrdinalIgnoreCase));

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout != Timeout.InfiniteTimeSpan && DateTimeOffset.UtcNow - started > timeout)
            {
                throw new TimeoutException("Timed out waiting for RebornBuddy botbase state transition.");
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}

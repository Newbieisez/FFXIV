using System.Windows;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.UI;
using ff14bot.AClasses;

namespace EZBuddy.Plugin;

public sealed class EZBuddyPlugin : BotPlugin
{
    private static readonly object WindowSync = new();
    private MainWindow? _window;

    public override string Author => "EZ";
    public override string Name => "EZBuddy Suite";
    public override Version Version => new(0, 1, 0);
    public override string Description => "Unified RebornBuddy automation, progression, integrations, safety guardrails, diagnostics, and dashboard.";
    public override bool WantButton => true;
    public override string ButtonText => "EZBuddy";

    public override void OnInitialize()
    {
        RegisterAdapters();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin initialized. Shared runtime and adapters registered.");
    }

    public override void OnEnabled()
    {
        RegisterAdapters();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin enabled.");
    }

    public override void OnDisabled()
    {
        CloseDashboard();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin disabled. BotBase execution is not forcibly stopped.");
    }

    public override void OnShutdown()
    {
        CloseDashboard();
        EZBuddyRuntime.Queue.Pause();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin shutdown; activity engine paused.");
    }

    public override void OnButtonPress()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ff14bot.Helpers.Logging.Write("[EZBuddy] Unable to open dashboard because the WPF dispatcher is unavailable.");
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            lock (WindowSync)
            {
                if (_window is { IsVisible: true })
                {
                    _window.Activate();
                    return;
                }

                _window = new MainWindow(new RebornBuddyTelemetryProvider());
                _window.Closed += (_, _) => _window = null;
                _window.Show();
                _window.Activate();
            }
        }));
    }

    private static void RegisterAdapters()
    {
        EZBuddyRuntime.Adapters.Register(new MagitekAdapter());
        EZBuddyRuntime.Adapters.Register(new OrderBotAdapter());
    }

    private void CloseDashboard()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _window = null;
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            lock (WindowSync)
            {
                if (_window is not null)
                {
                    _window.Close();
                    _window = null;
                }
            }
        }));
    }
}

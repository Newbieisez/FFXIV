using System.Windows;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Licensing;
using EZBuddy.UI;
using ff14bot.AClasses;

namespace EZBuddy.Plugin;

public sealed class EZBuddyPlugin : BotPlugin
{
    private static readonly object WindowSync = new();
    private MainWindow? _window;
    private LicenseManager? _licenseManager;

    public override string Author => "EZ";
    public override string Name => "EZBuddy Suite";
    public override Version Version => new(0, 1, 0);
    public override string Description => "Unified RebornBuddy automation, progression, integrations, safety guardrails, diagnostics, dashboard, and entitlement licensing.";
    public override bool WantButton => true;
    public override string ButtonText => "EZBuddy";

    public override void OnInitialize()
    {
        RegisterAdapters();
        InitializeLicensing();
        LicenseRuntime.LicenseRequired += OnLicenseRequired;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin initialized. Shared runtime, adapters, and licensing registered.");
    }

    public override void OnEnabled()
    {
        RegisterAdapters();
        if (_licenseManager is null)
        {
            InitializeLicensing();
        }
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin enabled.");
    }

    public override void OnDisabled()
    {
        CloseDashboard();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin disabled. BotBase execution is not forcibly stopped.");
    }

    public override void OnShutdown()
    {
        LicenseRuntime.LicenseRequired -= OnLicenseRequired;
        CloseDashboard();
        EZBuddyRuntime.Queue.Pause();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin shutdown; activity engine paused.");
    }

    public override void OnButtonPress() => OpenDashboard(navigateToLicense: false);

    private void InitializeLicensing()
    {
        try
        {
            var hardware = new WindowsHardwareIdentityProvider();
            var store = new FileLicenseStore();
            var validator = new RsaLicenseTokenValidator(LicenseSigningKeys.PublicKeys);
            _licenseManager = new LicenseManager(hardware, store, validator);
            LicenseRuntime.Configure(_licenseManager);
            var status = _licenseManager.InitializeAsync().GetAwaiter().GetResult();
            ff14bot.Helpers.Logging.Write($"[EZBuddy Licensing] {status.Message}");
        }
        catch (Exception ex)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Licensing] Initialization failed: {ex.Message}");
        }
    }

    private void OnLicenseRequired(object? sender, LicenseStatus? status)
    {
        ff14bot.Helpers.Logging.Write($"[EZBuddy Licensing] Execution blocked: {status?.Message ?? "License required."}");
        OpenDashboard(navigateToLicense: true);
    }

    private void OpenDashboard(bool navigateToLicense)
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
                if (_window is not { IsVisible: true })
                {
                    _window = new MainWindow(new RebornBuddyTelemetryProvider());
                    _window.Closed += (_, _) => _window = null;
                    _window.Show();
                }

                if (navigateToLicense)
                {
                    _window.ViewModel.NavigateToLicense();
                }

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

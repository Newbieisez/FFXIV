using System.Windows;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Notifications;
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
    private HttpOnlineLicenseClient? _onlineLicenseClient;
    private DiscordWebhookNotificationSink? _discordNotificationSink;
    private NotificationActivityTelemetrySink? _notificationTelemetrySink;

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
        InitializeNotifications();
        LicenseRuntime.LicenseRequired += OnLicenseRequired;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin initialized. Shared runtime, adapters, licensing, and optional notifications registered.");
    }

    public override void OnEnabled()
    {
        RegisterAdapters();
        if (_licenseManager is null)
        {
            InitializeLicensing();
        }

        if (_notificationTelemetrySink is null)
        {
            InitializeNotifications();
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

        if (_notificationTelemetrySink is not null)
        {
            EZBuddyRuntime.Telemetry.Unregister(_notificationTelemetrySink);
            _notificationTelemetrySink = null;
        }

        _discordNotificationSink?.Dispose();
        _discordNotificationSink = null;
        _onlineLicenseClient?.Dispose();
        _onlineLicenseClient = null;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin shutdown; activity engine paused and optional integrations released.");
    }

    public override void OnButtonPress() => OpenDashboard(navigateToLicense: false);

    private void InitializeLicensing()
    {
        try
        {
            var hardware = new WindowsHardwareIdentityProvider();
            var store = new FileLicenseStore();
            var validator = new RsaLicenseTokenValidator(LicenseSigningKeys.PublicKeys);

            IOnlineLicenseClient? onlineClient = null;
            var apiText = Environment.GetEnvironmentVariable("EZBUDDY_LICENSE_API");
            if (!string.IsNullOrWhiteSpace(apiText) &&
                Uri.TryCreate(apiText, UriKind.Absolute, out var apiUri) &&
                string.Equals(apiUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                _onlineLicenseClient?.Dispose();
                _onlineLicenseClient = new HttpOnlineLicenseClient(apiUri);
                onlineClient = _onlineLicenseClient;
            }

            _licenseManager = new LicenseManager(hardware, store, validator, onlineClient);
            LicenseRuntime.Configure(_licenseManager);
            var status = _licenseManager.InitializeAsync().GetAwaiter().GetResult();
            ff14bot.Helpers.Logging.Write($"[EZBuddy Licensing] {status.Message}");
        }
        catch (Exception ex)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Licensing] Initialization failed: {ex.Message}");
        }
    }

    private void InitializeNotifications()
    {
        var webhookText = Environment.GetEnvironmentVariable("EZBUDDY_DISCORD_WEBHOOK");
        if (string.IsNullOrWhiteSpace(webhookText))
        {
            return;
        }

        try
        {
            if (!Uri.TryCreate(webhookText, UriKind.Absolute, out var webhookUri))
            {
                ff14bot.Helpers.Logging.Write("[EZBuddy Notifications] Discord webhook configuration is invalid; notifications disabled.");
                return;
            }

            _discordNotificationSink?.Dispose();
            _discordNotificationSink = new DiscordWebhookNotificationSink(webhookUri);
            _notificationTelemetrySink = new NotificationActivityTelemetrySink(_discordNotificationSink);
            EZBuddyRuntime.Telemetry.Register(_notificationTelemetrySink);
            ff14bot.Helpers.Logging.Write("[EZBuddy Notifications] Discord queue notifications enabled.");
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Notifications] Initialization failed without exposing the webhook endpoint: {exception.Message}");
            _notificationTelemetrySink = null;
            _discordNotificationSink?.Dispose();
            _discordNotificationSink = null;
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
        EZBuddyRuntime.Adapters.Register(new LisbethAdapter());
        EZBuddyRuntime.Adapters.Register(new RebornBuddyDutySupportAdapter());
        EZBuddyRuntime.Adapters.Register(new LlamaRetainerSweepAdapter());
        EZBuddyRuntime.Adapters.Register(new LlamaGrandCompanyAdapter());
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

using System.Windows;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Notifications;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Bundles;
using EZBuddy.RebornBuddy.Duties;
using EZBuddy.RebornBuddy.Licensing;
using EZBuddy.RebornBuddy.Settings;
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
    private bool _firstPlayableLoopQueuedThisEnable;

    public override string Author => "EZ";
    public override string Name => "EZBuddy Suite";
    public override Version Version => new(0, 1, 0);
    public override string Description => "Unified RebornBuddy automation, progression, integrations, safety guardrails, diagnostics, dashboard, and entitlement licensing.";
    public override bool WantButton => true;
    public override string ButtonText => "EZBuddy";

    public override void OnInitialize()
    {
        RegisterAdapters();
        DutyRouteRecorderRuntime.Configure(new RebornBuddyDutyRouteRecorderController());
        InitializeLicensing();
        InitializeNotifications();
        _ = RebornBuddySettingsSession.GetOrCreate();
        LicenseRuntime.LicenseRequired += OnLicenseRequired;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin initialized. Shared runtime, per-character settings, adapters, licensing, route recorder, and optional notifications registered.");
    }

    public override void OnEnabled()
    {
        RegisterAdapters();
        DutyRouteRecorderRuntime.Configure(new RebornBuddyDutyRouteRecorderController());
        _ = RebornBuddySettingsSession.GetOrCreate();

        if (_licenseManager is null)
        {
            InitializeLicensing();
        }

        if (_notificationTelemetrySink is null)
        {
            InitializeNotifications();
        }

        _firstPlayableLoopQueuedThisEnable = false;
        if (ReadBooleanEnvironmentFlag("EZBUDDY_QUEUE_FIRST_LOOP_ON_ENABLE"))
        {
            QueueSavedFirstPlayableLoop();
        }

        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin enabled.");
    }

    public override void OnDisabled()
    {
        _firstPlayableLoopQueuedThisEnable = false;
        CloseDashboard();
        _ = RebornBuddySettingsSession.FlushPendingSavesAsync();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin disabled. BotBase execution is not forcibly stopped; pending settings flush requested.");
    }

    public override void OnShutdown()
    {
        LicenseRuntime.LicenseRequired -= OnLicenseRequired;
        DutyRouteRecorderRuntime.Configure(null);
        CloseDashboard();
        EZBuddyRuntime.Queue.Pause();

        try
        {
            RebornBuddySettingsSession.FlushPendingSavesAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Settings] Shutdown flush failed: {exception.Message}");
        }

        if (_notificationTelemetrySink is not null)
        {
            EZBuddyRuntime.Telemetry.Unregister(_notificationTelemetrySink);
            _notificationTelemetrySink = null;
        }

        _discordNotificationSink?.Dispose();
        _discordNotificationSink = null;
        _onlineLicenseClient?.Dispose();
        _onlineLicenseClient = null;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin shutdown; activity engine paused, settings flushed, route recorder released, and optional integrations released.");
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

    private void QueueSavedFirstPlayableLoop()
    {
        if (_firstPlayableLoopQueuedThisEnable)
        {
            return;
        }

        try
        {
            var settings = RebornBuddySettingsSession.GetOrCreate().Current;
            var controller = new RebornBuddyFirstPlayableLoopController();
            var result = controller.QueueAsync(settings.FirstPlayableLoop).GetAwaiter().GetResult();

            if (!result.Success)
            {
                ff14bot.Helpers.Logging.Write($"[EZBuddy Loop] Saved first-loop configuration was not queued: {result.Message}");
                if (LicenseRuntime.CurrentStatus is not { IsValid: true })
                {
                    OpenDashboard(navigateToLicense: true);
                }
                return;
            }

            _firstPlayableLoopQueuedThisEnable = true;
            ff14bot.Helpers.Logging.Write($"[EZBuddy Loop] {result.Message}");
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Loop] Saved configuration could not be queued: {exception.Message}");
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
                    var settingsManager = RebornBuddySettingsSession.GetOrCreate();
                    _window = new MainWindow(
                        new RebornBuddyTelemetryProvider(),
                        new RebornBuddyFirstPlayableLoopController(),
                        settingsManager,
                        EZBuddyRuntime.RunLoop);
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

    private static bool ReadBooleanEnvironmentFlag(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
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

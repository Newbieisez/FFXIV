using System.IO;
using System.Windows;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Notifications;
using EZBuddy.Core.Runtime;
using EZBuddy.Core.Safety;
using EZBuddy.Core.Settings;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Bundles;
using EZBuddy.RebornBuddy.Duties;
using EZBuddy.RebornBuddy.Licensing;
using EZBuddy.RebornBuddy.Safety;
using EZBuddy.RebornBuddy.Settings;
using EZBuddy.UI;
using EZBuddy.UI.ViewModels;
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
    private IRuntimeSessionPersistence? _runtimePersistence;
    private RebornBuddyGamelogContactSource? _socialContactSource;
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
        ProductIntelligenceRuntime.Provider = new RebornBuddyProductIntelligenceProvider();
        InitializeLicensing();
        InitializeNotifications();
        _ = RebornBuddySettingsSession.GetOrCreate();
        LicenseRuntime.LicenseRequired += OnLicenseRequired;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin initialized. Shared runtime, per-character settings, adapters, licensing, route recorder, Product Intelligence, and optional notifications registered.");
    }

    public override void OnEnabled()
    {
        RegisterAdapters();
        DutyRouteRecorderRuntime.Configure(new RebornBuddyDutyRouteRecorderController());
        ProductIntelligenceRuntime.Provider = new RebornBuddyProductIntelligenceProvider();
        _ = RebornBuddySettingsSession.GetOrCreate();
        InitializeRuntimePersistence();
        InitializeSocialSafety();

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
        CloseSocialSafety();
        CloseRuntimePersistence(markClean: true);
        _ = RebornBuddySettingsSession.FlushPendingSavesAsync();
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin disabled. Runtime checkpoint marked clean; social monitor released; pending settings flush requested.");
    }

    public override void OnShutdown()
    {
        LicenseRuntime.LicenseRequired -= OnLicenseRequired;
        DutyRouteRecorderRuntime.Configure(null);
        ProductIntelligenceRuntime.Provider = null;
        CloseDashboard();
        CloseSocialSafety();
        EZBuddyRuntime.Queue.Pause();
        CloseRuntimePersistence(markClean: true);

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

        if (_discordNotificationSink is not null)
        {
            EZBuddyRuntime.Notifications.Unregister(_discordNotificationSink);
            _discordNotificationSink.Dispose();
            _discordNotificationSink = null;
        }

        _onlineLicenseClient?.Dispose();
        _onlineLicenseClient = null;
        ff14bot.Helpers.Logging.Write("[EZBuddy] Plugin shutdown; queue paused, settings flushed, runtime checkpoint closed, social monitor/route recorder/Product Intelligence released, and optional integrations released.");
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

            if (_notificationTelemetrySink is not null)
            {
                EZBuddyRuntime.Telemetry.Unregister(_notificationTelemetrySink);
                _notificationTelemetrySink = null;
            }

            if (_discordNotificationSink is not null)
            {
                EZBuddyRuntime.Notifications.Unregister(_discordNotificationSink);
                _discordNotificationSink.Dispose();
            }

            _discordNotificationSink = new DiscordWebhookNotificationSink(webhookUri);
            EZBuddyRuntime.Notifications.Register(_discordNotificationSink);
            _notificationTelemetrySink = new NotificationActivityTelemetrySink(EZBuddyRuntime.Notifications);
            EZBuddyRuntime.Telemetry.Register(_notificationTelemetrySink);
            ff14bot.Helpers.Logging.Write("[EZBuddy Notifications] Shared Discord notification pipeline enabled.");
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Notifications] Initialization failed without exposing the webhook endpoint: {exception.Message}");
            if (_notificationTelemetrySink is not null)
            {
                EZBuddyRuntime.Telemetry.Unregister(_notificationTelemetrySink);
                _notificationTelemetrySink = null;
            }

            if (_discordNotificationSink is not null)
            {
                EZBuddyRuntime.Notifications.Unregister(_discordNotificationSink);
                _discordNotificationSink.Dispose();
                _discordNotificationSink = null;
            }
        }
    }

    private void InitializeSocialSafety()
    {
        CloseSocialSafety();
        try
        {
            _socialContactSource = new RebornBuddyGamelogContactSource();
            SocialSafetyRuntime.Monitor = new SocialSafetyMonitor(
                _socialContactSource,
                EZBuddyRuntime.RunLoop,
                EZBuddyRuntime.Notifications,
                new SocialSafetyPolicy(
                    PauseOnDirectTell: true,
                    PauseOnGmCommunication: true,
                    PauseOnRepeatedTradeRequests: false));
            ff14bot.Helpers.Logging.Write("[EZBuddy Social Safety] Passive incoming tell/GM monitoring enabled. Private message contents are not forwarded to notifications.");
        }
        catch (Exception exception)
        {
            SocialSafetyRuntime.Monitor = null;
            _socialContactSource?.Dispose();
            _socialContactSource = null;
            ff14bot.Helpers.Logging.Write($"[EZBuddy Social Safety] Monitor initialization failed closed: {exception.Message}");
        }
    }

    private void CloseSocialSafety()
    {
        SocialSafetyRuntime.Monitor = null;
        _socialContactSource?.Dispose();
        _socialContactSource = null;
    }

    private void InitializeRuntimePersistence()
    {
        CloseRuntimePersistence(markClean: false);
        try
        {
            var characterKey = SettingsPathSanitizer.Sanitize(ff14bot.Core.Player?.Name ?? "default");
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings", "EZBuddy", "Runtime");
            var checkpoint = new JsonResumeCheckpointStore(Path.Combine(root, characterKey + ".resume.json"));
            var replay = new JsonLinesDecisionReplayRecorder(Path.Combine(root, characterKey + ".decisions.jsonl"));
            _runtimePersistence = new RuntimePersistenceTelemetrySink(checkpoint, replay);
            EZBuddyRuntime.Telemetry.Register(_runtimePersistence);
            ff14bot.Helpers.Logging.Write($"[EZBuddy Runtime] Resume checkpoint and decision replay enabled for '{characterKey}'.");
        }
        catch (Exception exception)
        {
            _runtimePersistence = null;
            ff14bot.Helpers.Logging.Write($"[EZBuddy Runtime] Persistence initialization failed: {exception.Message}");
        }
    }

    private void CloseRuntimePersistence(bool markClean)
    {
        var persistence = _runtimePersistence;
        if (persistence is null)
        {
            return;
        }

        _runtimePersistence = null;
        try
        {
            if (markClean)
            {
                persistence.MarkCleanShutdownAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            ff14bot.Helpers.Logging.Write($"[EZBuddy Runtime] Final checkpoint failed: {exception.Message}");
        }
        finally
        {
            EZBuddyRuntime.Telemetry.Unregister(persistence);
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

using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Runtime;
using EZBuddy.Core.Settings;
using EZBuddy.UI.Infrastructure;
using EZBuddy.UI.Models;

namespace EZBuddy.UI.ViewModels;

public interface IHostTelemetryProvider
{
    Task<TelemetryUpdate> GetTelemetryAsync(CancellationToken cancellationToken = default);
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IHostTelemetryProvider? _hostTelemetryProvider;
    private readonly IRunLoopController _runLoopController;
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;
    private string _applicationStatus = "Idle";
    private string _currentActivity = "No active activity";
    private string _characterName = "Not connected";
    private string _serverName = "—";
    private string _routineName = "Unknown";
    private bool _routineActive;
    private long _gilEarned;
    private TimeSpan _sessionRuntime;
    private int _activeHooks;
    private int _warningCount;
    private string _selectedWorkspace = "Dashboard";
    private bool _isLicenseWorkspace;
    private bool _isFirstLoopWorkspace;
    private string _runLoopState = RunLoopState.Idle.ToString();
    private string _runLoopStatus = "Run loop is idle.";
    private string _licenseStatusMessage = "License status has not been evaluated.";
    private string _licenseTier = "None";
    private string _licenseExpiration = "—";
    private string _licenseHardwareId = "—";
    private string _licenseTokenText = string.Empty;
    private string _trialEmail = string.Empty;

    public MainWindowViewModel(
        IHostTelemetryProvider? hostTelemetryProvider = null,
        IFirstPlayableLoopController? firstLoopController = null,
        IObservableSettings? settings = null,
        IRunLoopController? runLoopController = null)
    {
        _hostTelemetryProvider = hostTelemetryProvider;
        _runLoopController = runLoopController ?? EZBuddyRuntime.RunLoop;
        FirstLoop = new FirstPlayableLoopViewModel(firstLoopController, settings);

        NavigationModules = new ObservableCollection<NavigationModule>(CreateNavigation());
        PipelineSteps = new ObservableCollection<PipelineStep>();
        ActivityQueue = new ObservableCollection<EZBuddy.UI.Models.ActivityQueueItem>();
        Integrations = new ObservableCollection<IntegrationHealthItem>();

        SelectModuleCommand = new RelayCommand(SelectModule);
        ToggleModuleCommand = new RelayCommand(ToggleModule);
        PauseCommand = new AsyncRelayCommand(() => _runLoopController.PauseAsync());
        GentleStopCommand = new AsyncRelayCommand(() => _runLoopController.StopAsync());
        ResumeCommand = new AsyncRelayCommand(() => _runLoopController.ResumeAsync());
        EmergencyStopCommand = new AsyncRelayCommand(() => EZBuddyRuntime.Queue.EmergencyStopAsync());
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        InstallLicenseCommand = new AsyncRelayCommand(InstallLicenseAsync);
        ActivateTrialCommand = new AsyncRelayCommand(ActivateTrialAsync);
        RefreshLicenseCommand = new AsyncRelayCommand(RefreshLicenseAsync);
        CopyHardwareIdCommand = new RelayCommand(CopyHardwareId);

        LicenseRuntime.StatusChanged += OnLicenseStatusChanged;
        LicenseRuntime.LicenseRequired += OnLicenseRequired;
        _runLoopController.StatusChanged += OnRunLoopStatusChanged;
        ApplyLicenseStatus(LicenseRuntime.CurrentStatus);
        ApplyRunLoopStatus(_runLoopController.Status);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTick;
    }

    public FirstPlayableLoopViewModel FirstLoop { get; }
    public ObservableCollection<NavigationModule> NavigationModules { get; }
    public ObservableCollection<PipelineStep> PipelineSteps { get; }
    public ObservableCollection<EZBuddy.UI.Models.ActivityQueueItem> ActivityQueue { get; }
    public ObservableCollection<IntegrationHealthItem> Integrations { get; }

    public ICommand SelectModuleCommand { get; }
    public ICommand ToggleModuleCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand GentleStopCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand EmergencyStopCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand InstallLicenseCommand { get; }
    public ICommand ActivateTrialCommand { get; }
    public ICommand RefreshLicenseCommand { get; }
    public ICommand CopyHardwareIdCommand { get; }

    public string ApplicationStatus
    {
        get => _applicationStatus;
        private set => SetProperty(ref _applicationStatus, value);
    }

    public string CurrentActivity
    {
        get => _currentActivity;
        private set => SetProperty(ref _currentActivity, value);
    }

    public string CharacterName
    {
        get => _characterName;
        private set => SetProperty(ref _characterName, value);
    }

    public string ServerName
    {
        get => _serverName;
        private set => SetProperty(ref _serverName, value);
    }

    public string RoutineName
    {
        get => _routineName;
        private set => SetProperty(ref _routineName, value);
    }

    public bool RoutineActive
    {
        get => _routineActive;
        private set => SetProperty(ref _routineActive, value);
    }

    public long GilEarned
    {
        get => _gilEarned;
        private set => SetProperty(ref _gilEarned, value);
    }

    public TimeSpan SessionRuntime
    {
        get => _sessionRuntime;
        private set
        {
            if (SetProperty(ref _sessionRuntime, value))
            {
                OnPropertyChanged(nameof(SessionRuntimeDisplay));
            }
        }
    }

    public string SessionRuntimeDisplay => SessionRuntime.ToString(@"hh\:mm\:ss");

    public int ActiveHooks
    {
        get => _activeHooks;
        private set => SetProperty(ref _activeHooks, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    public string SelectedWorkspace
    {
        get => _selectedWorkspace;
        private set => SetProperty(ref _selectedWorkspace, value);
    }

    public bool IsLicenseWorkspace
    {
        get => _isLicenseWorkspace;
        private set => SetProperty(ref _isLicenseWorkspace, value);
    }

    public bool IsFirstLoopWorkspace
    {
        get => _isFirstLoopWorkspace;
        private set => SetProperty(ref _isFirstLoopWorkspace, value);
    }

    public string RunLoopState
    {
        get => _runLoopState;
        private set => SetProperty(ref _runLoopState, value);
    }

    public string RunLoopStatus
    {
        get => _runLoopStatus;
        private set => SetProperty(ref _runLoopStatus, value);
    }

    public string LicenseStatusMessage
    {
        get => _licenseStatusMessage;
        private set => SetProperty(ref _licenseStatusMessage, value);
    }

    public string LicenseTier
    {
        get => _licenseTier;
        private set => SetProperty(ref _licenseTier, value);
    }

    public string LicenseExpiration
    {
        get => _licenseExpiration;
        private set => SetProperty(ref _licenseExpiration, value);
    }

    public string LicenseHardwareId
    {
        get => _licenseHardwareId;
        private set => SetProperty(ref _licenseHardwareId, value);
    }

    public string LicenseTokenText
    {
        get => _licenseTokenText;
        set => SetProperty(ref _licenseTokenText, value);
    }

    public string TrialEmail
    {
        get => _trialEmail;
        set => SetProperty(ref _trialEmail, value);
    }

    public void NavigateToLicense()
    {
        foreach (var module in NavigationModules)
        {
            module.IsSelected = string.Equals(module.Key, "License", StringComparison.OrdinalIgnoreCase);
        }

        SelectedWorkspace = "License & Trial Activation";
        IsLicenseWorkspace = true;
        IsFirstLoopWorkspace = false;
        ApplyLicenseStatus(LicenseRuntime.CurrentStatus);
    }

    public void StartAutoRefresh()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        _ = FirstLoop.LoadAsync();
        _ = RefreshAsync();
    }

    public void StopAutoRefresh() => _refreshTimer.Stop();

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            await RefreshTelemetryAsync().ConfigureAwait(true);
            RefreshQueue();
            await RefreshIntegrationsAsync().ConfigureAwait(true);
            ApplyLicenseStatus(LicenseRuntime.CurrentStatus);
            ApplyRunLoopStatus(_runLoopController.Status);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        LicenseRuntime.StatusChanged -= OnLicenseStatusChanged;
        LicenseRuntime.LicenseRequired -= OnLicenseRequired;
        _runLoopController.StatusChanged -= OnRunLoopStatusChanged;
        FirstLoop.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private async void OnRefreshTick(object? sender, EventArgs e)
    {
        try
        {
            await RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
            // The dashboard must never crash the bot because one telemetry refresh failed.
        }
    }

    private async Task RefreshTelemetryAsync()
    {
        if (_hostTelemetryProvider is not null)
        {
            var update = await _hostTelemetryProvider.GetTelemetryAsync().ConfigureAwait(true);
            ApplicationStatus = update.ApplicationStatus;
            CurrentActivity = update.CurrentActivity;
            CharacterName = update.CharacterName;
            ServerName = update.ServerName;
            RoutineName = update.RoutineName;
            RoutineActive = update.RoutineActive;
            GilEarned = update.GilEarned;
            SessionRuntime = update.SessionRuntime;
            ActiveHooks = update.ActiveHooks;
            WarningCount = update.WarningCount;
            return;
        }

        var queue = EZBuddyRuntime.Queue;
        ApplicationStatus = queue.IsEmergencyStopped ? "Emergency Stopped" : queue.IsPaused ? "Paused" : queue.IsRunning ? "Running" : "Idle";
        CurrentActivity = queue.CurrentActivity?.Name ?? "No active activity";
    }

    private void RefreshQueue()
    {
        var snapshots = EZBuddyRuntime.Queue.GetSnapshots();
        ActivityQueue.Clear();
        PipelineSteps.Clear();

        var activeAndPending = snapshots
            .Where(snapshot => snapshot.State is ActivityState.Pending or ActivityState.Waiting or ActivityState.Running or ActivityState.GentleStopping or ActivityState.Blocked)
            .OrderBy(snapshot => snapshot.EnqueuedAt)
            .ToArray();

        var order = 1;
        foreach (var snapshot in activeAndPending)
        {
            ActivityQueue.Add(new EZBuddy.UI.Models.ActivityQueueItem
            {
                Id = snapshot.ActivityId,
                Title = snapshot.Name,
                Category = snapshot.Category.ToString(),
                StopCondition = snapshot.State.ToString(),
                Detail = snapshot.LastMessage,
                State = snapshot.State.ToString()
            });

            PipelineSteps.Add(new PipelineStep
            {
                Order = order++,
                Title = snapshot.Name,
                Detail = snapshot.LastMessage,
                Status = snapshot.State.ToString()
            });
        }
    }

    private async Task RefreshIntegrationsAsync()
    {
        var statuses = await EZBuddyRuntime.Adapters.GetStatusesAsync().ConfigureAwait(true);
        Integrations.Clear();
        foreach (var status in statuses)
        {
            Integrations.Add(new IntegrationHealthItem
            {
                Name = status.DisplayName,
                Type = status.Key,
                State = status.Health.ToString(),
                Detail = status.Message
            });
        }
    }

    private async Task InstallLicenseAsync()
    {
        var manager = LicenseRuntime.Manager;
        if (manager is null)
        {
            LicenseStatusMessage = "Licensing is not initialized.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LicenseTokenText))
        {
            LicenseStatusMessage = "Paste the full .ezlic JSON entitlement before installing.";
            return;
        }

        try
        {
            var status = await manager.InstallOfflineTokenAsync(LicenseTokenText).ConfigureAwait(true);
            ApplyLicenseStatus(status);
            if (status.IsValid)
            {
                LicenseTokenText = string.Empty;
            }
        }
        catch (Exception ex)
        {
            LicenseStatusMessage = $"License install failed: {ex.Message}";
        }
    }

    private async Task ActivateTrialAsync()
    {
        var manager = LicenseRuntime.Manager;
        if (manager is null)
        {
            LicenseStatusMessage = "Licensing is not initialized.";
            return;
        }

        var status = await manager.ActivateOnlineTrialAsync(TrialEmail).ConfigureAwait(true);
        ApplyLicenseStatus(status);
    }

    private async Task RefreshLicenseAsync()
    {
        var manager = LicenseRuntime.Manager;
        if (manager is null)
        {
            LicenseStatusMessage = "Licensing is not initialized.";
            return;
        }

        var status = await manager.RefreshStatusAsync().ConfigureAwait(true);
        ApplyLicenseStatus(status);
    }

    private void CopyHardwareId()
    {
        if (string.IsNullOrWhiteSpace(LicenseHardwareId) || LicenseHardwareId == "—")
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(LicenseHardwareId);
            LicenseStatusMessage = "Anonymous hardware ID copied to clipboard.";
        }
        catch
        {
            LicenseStatusMessage = "Unable to access the Windows clipboard.";
        }
    }

    private void OnLicenseStatusChanged(object? sender, LicenseStatus status)
        => DispatchToUi(() => ApplyLicenseStatus(status));

    private void OnLicenseRequired(object? sender, LicenseStatus? status)
        => DispatchToUi(() =>
        {
            NavigateToLicense();
            ApplyLicenseStatus(status);
        });

    private void OnRunLoopStatusChanged(object? sender, RunLoopStatus status)
        => DispatchToUi(() => ApplyRunLoopStatus(status));

    private void ApplyRunLoopStatus(RunLoopStatus status)
    {
        RunLoopState = status.State.ToString();
        RunLoopStatus = status.Message;
    }

    private void ApplyLicenseStatus(LicenseStatus? status)
    {
        if (status is null)
        {
            LicenseTier = "None";
            LicenseExpiration = "—";
            LicenseHardwareId = "—";
            LicenseStatusMessage = "Licensing is not initialized.";
            return;
        }

        LicenseTier = status.Tier.ToString();
        LicenseExpiration = status.ExpiresUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "No expiration";
        LicenseHardwareId = status.HardwareId;
        LicenseStatusMessage = status.Message;
    }

    private static void DispatchToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private void SelectModule(object? parameter)
    {
        if (parameter is not NavigationModule selected)
        {
            return;
        }

        foreach (var module in NavigationModules)
        {
            module.IsSelected = ReferenceEquals(module, selected);
        }

        SelectedWorkspace = selected.Name;
        IsLicenseWorkspace = string.Equals(selected.Key, "License", StringComparison.OrdinalIgnoreCase);
        IsFirstLoopWorkspace = string.Equals(selected.Key, "FirstLoop", StringComparison.OrdinalIgnoreCase);

        if (IsLicenseWorkspace)
        {
            ApplyLicenseStatus(LicenseRuntime.CurrentStatus);
        }

        if (IsFirstLoopWorkspace)
        {
            _ = FirstLoop.LoadAsync();
        }
    }

    private static void ToggleModule(object? parameter)
    {
        if (parameter is NavigationModule module && module.CanToggle)
        {
            module.IsEnabled = !module.IsEnabled;
        }
    }

    private static IEnumerable<NavigationModule> CreateNavigation()
    {
        yield return Nav("Core", "Dashboard", "▦", "Dashboard", false, true);
        yield return Nav("Core", "First Playable Loop", "▶", "FirstLoop", false);
        yield return Nav("Core", "Activity Queue", "≡", "Queue", false);
        yield return Nav("Core", "License & Trial Activation", "◆", "License", false);
        yield return Nav("Core", "Progression Planner", "✓", "Progression", true);
        yield return Nav("Core", "Hooks & Integrations", "↔", "Hooks", false);
        yield return Nav("Core", "Diagnostics & Conflict Guard", "!", "Diagnostics", false);

        yield return Nav("Instances", "Duty & Dungeons", "◆", "Duty", true);
        yield return Nav("Instances", "Deep Dungeons", "⬡", "DeepDungeons", true);
        yield return Nav("Instances", "Treasure Hunts", "✧", "TreasureHunts", true);

        yield return Nav("World", "Field Operations", "◉", "FieldOperations", true);
        yield return Nav("World", "Shared FATE & Gemstones", "◇", "SharedFates", true);
        yield return Nav("World", "Relics & Events", "✦", "RelicsEvents", true);

        yield return Nav("Economy", "Gathering & Crafting", "⌁", "CraftGather", true);
        yield return Nav("Economy", "Retainers & Marketboard", "◎", "RetainersMarket", true);
        yield return Nav("Economy", "Airship & Submersibles", "▲", "Voyages", true);
        yield return Nav("Economy", "Materia Optimization", "◌", "Materia", true);
        yield return Nav("Economy", "Mass Desynthesis", "⌫", "Desynthesis", true);
        yield return Nav("Economy", "Levequest Auto-Burners", "▤", "Levequests", true);
        yield return Nav("Economy", "Doman Enclave", "◫", "DomanEnclave", true);

        yield return Nav("Collections", "Triple Triad", "▣", "TripleTriad", true);
        yield return Nav("Collections", "Wondrous Tails", "☆", "WondrousTails", true);

        yield return Nav("Lifestyle", "Island Sanctuary", "△", "Sanctuary", true);
        yield return Nav("Lifestyle", "Housing & Gardening", "⌂", "HousingGardening", true);
        yield return Nav("Lifestyle", "Dailies & Tribes", "◈", "Dailies", true);

        yield return Nav("Safety", "Social Safety Monitor", "⚑", "SocialSafety", true);
        yield return Nav("Safety", "Session Safety & Breaks", "◷", "SessionSafety", true);
        yield return Nav("Safety", "Webhook & Push Alerts", "●", "Notifications", true);
    }

    private static NavigationModule Nav(string group, string name, string icon, string key, bool canToggle, bool selected = false)
        => new()
        {
            Group = group,
            Name = name,
            Icon = icon,
            Key = key,
            CanToggle = canToggle,
            IsEnabled = !canToggle || key is "Progression" or "Duty" or "RetainersMarket",
            IsSelected = selected
        };
}

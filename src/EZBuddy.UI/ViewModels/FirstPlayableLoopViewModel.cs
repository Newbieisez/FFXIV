using System.Windows.Input;
using EZBuddy.Core.Adapters;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Settings;
using EZBuddy.UI.Infrastructure;

namespace EZBuddy.UI.ViewModels;

public sealed class FirstPlayableLoopViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IFirstPlayableLoopController? _controller;
    private readonly IDutyRouteRecorderController? _routeRecorderController;
    private readonly IObservableSettings _settings;
    private bool _applyingSettings;

    private string _queueDutyId = string.Empty;
    private string _dutyTerritoryId = string.Empty;
    private string _dutyProfilePath = string.Empty;
    private string _dutyMode = nameof(DutyAutomationMode.DutySupport);
    private string _trustId = string.Empty;
    private string _targetLevel = string.Empty;
    private string _maxRuns = "1";
    private string _dutyLootAction = nameof(EZBuddy.Core.Duties.DutyLootAction.Greed);
    private string _dutyLootPassAtOrBelowFreeSlots = "3";
    private string _minimumDutyFreeSlots = "6";
    private string _inventoryTargetFreeSlots = "12";
    private string _minimumRetainerFreeSlots = "8";
    private bool _autoRepairGear = true;
    private string _autoRepairThresholdPercent = "30";
    private bool _autoExtractMateria = true;
    private string _foodItemId = string.Empty;
    private string _approvedExpertDeliveryItemIds = string.Empty;
    private bool _requireWellFed;
    private bool _runMaintenance = true;
    private bool _runRetainers = true;
    private bool _runInventoryPressureRelief = true;
    private bool _runDailyProgression = true;
    private bool _runDutyLoop = true;
    private bool _returnToIdle = true;
    private bool _runGrandCompanyExpertDeliveryDaily;
    private bool _runVentureRefillDaily;
    private string _ventureMinimumQuantity = "10";
    private string _ventureTargetQuantity = "50";
    private bool _runCustomDeliveriesWeekly;
    private string _customDeliveryClientKeys = string.Empty;
    private string _customDeliveryCraftingClass = "Carpenter";
    private string _statusMessage = "Load or configure the first playable loop, then run it.";
    private string _routeRecorderName = "Recorded Duty";
    private string _routeRecorderMinimumLevel = "1";
    private string _routeRecorderMaximumLevel = "100";
    private string _routeRecorderStatus = "Recorder idle.";

    public FirstPlayableLoopViewModel(
        IFirstPlayableLoopController? controller,
        IObservableSettings? settings = null,
        IDutyRouteRecorderController? routeRecorderController = null)
    {
        _controller = controller;
        _routeRecorderController = routeRecorderController ?? DutyRouteRecorderRuntime.Controller;
        _settings = settings ?? new JsonEZBuddySettingsManager(new JsonEZBuddySettingsStore());
        _settings.SettingsChanged += OnSettingsChanged;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RunCommand = new AsyncRelayCommand(RunAsync);
        ReloadCommand = new AsyncRelayCommand(LoadAsync);
        StartRouteRecorderCommand = new RelayCommand(StartRouteRecorder);
        CaptureRouteTargetCommand = new RelayCommand(CaptureRouteTarget);
        StopRouteRecorderCommand = new RelayCommand(StopRouteRecorder);
    }

    public ICommand SaveCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand StartRouteRecorderCommand { get; }
    public ICommand CaptureRouteTargetCommand { get; }
    public ICommand StopRouteRecorderCommand { get; }

    public IReadOnlyList<string> DutyModes { get; } =
    [
        nameof(DutyAutomationMode.DutySupport),
        nameof(DutyAutomationMode.Trust)
    ];

    public IReadOnlyList<string> DutyLootActions { get; } =
    [
        nameof(EZBuddy.Core.Duties.DutyLootAction.Greed),
        nameof(EZBuddy.Core.Duties.DutyLootAction.Pass),
        nameof(EZBuddy.Core.Duties.DutyLootAction.LeaveUnchanged)
    ];

    public IReadOnlyList<string> CustomDeliveryCraftingClasses { get; } =
    [
        "Carpenter",
        "Blacksmith",
        "Armorer",
        "Goldsmith",
        "Leatherworker",
        "Weaver",
        "Alchemist",
        "Culinarian"
    ];

    public string SettingsPath => _settings.FilePath;

    public string QueueDutyId { get => _queueDutyId; set => SetAndSchedule(ref _queueDutyId, value); }
    public string DutyTerritoryId { get => _dutyTerritoryId; set => SetAndSchedule(ref _dutyTerritoryId, value); }
    public string DutyProfilePath { get => _dutyProfilePath; set => SetAndSchedule(ref _dutyProfilePath, value); }
    public string DutyMode { get => _dutyMode; set => SetAndSchedule(ref _dutyMode, value); }
    public string TrustId { get => _trustId; set => SetAndSchedule(ref _trustId, value); }
    public string TargetLevel { get => _targetLevel; set => SetAndSchedule(ref _targetLevel, value); }
    public string MaxRuns { get => _maxRuns; set => SetAndSchedule(ref _maxRuns, value); }
    public string DutyLootAction { get => _dutyLootAction; set => SetAndSchedule(ref _dutyLootAction, value); }
    public string DutyLootPassAtOrBelowFreeSlots { get => _dutyLootPassAtOrBelowFreeSlots; set => SetAndSchedule(ref _dutyLootPassAtOrBelowFreeSlots, value); }
    public string MinimumDutyFreeSlots { get => _minimumDutyFreeSlots; set => SetAndSchedule(ref _minimumDutyFreeSlots, value); }
    public string InventoryTargetFreeSlots { get => _inventoryTargetFreeSlots; set => SetAndSchedule(ref _inventoryTargetFreeSlots, value); }
    public string MinimumRetainerFreeSlots { get => _minimumRetainerFreeSlots; set => SetAndSchedule(ref _minimumRetainerFreeSlots, value); }
    public bool AutoRepairGear { get => _autoRepairGear; set => SetAndSchedule(ref _autoRepairGear, value); }
    public string AutoRepairThresholdPercent { get => _autoRepairThresholdPercent; set => SetAndSchedule(ref _autoRepairThresholdPercent, value); }
    public bool AutoExtractMateria { get => _autoExtractMateria; set => SetAndSchedule(ref _autoExtractMateria, value); }
    public string FoodItemId { get => _foodItemId; set => SetAndSchedule(ref _foodItemId, value); }
    public string ApprovedExpertDeliveryItemIds { get => _approvedExpertDeliveryItemIds; set => SetAndSchedule(ref _approvedExpertDeliveryItemIds, value); }
    public bool RequireWellFed { get => _requireWellFed; set => SetAndSchedule(ref _requireWellFed, value); }
    public bool RunMaintenance { get => _runMaintenance; set => SetAndSchedule(ref _runMaintenance, value); }
    public bool RunRetainers { get => _runRetainers; set => SetAndSchedule(ref _runRetainers, value); }
    public bool RunInventoryPressureRelief { get => _runInventoryPressureRelief; set => SetAndSchedule(ref _runInventoryPressureRelief, value); }
    public bool RunDailyProgression { get => _runDailyProgression; set => SetAndSchedule(ref _runDailyProgression, value); }
    public bool RunDutyLoop { get => _runDutyLoop; set => SetAndSchedule(ref _runDutyLoop, value); }
    public bool ReturnToIdle { get => _returnToIdle; set => SetAndSchedule(ref _returnToIdle, value); }
    public bool RunGrandCompanyExpertDeliveryDaily { get => _runGrandCompanyExpertDeliveryDaily; set => SetAndSchedule(ref _runGrandCompanyExpertDeliveryDaily, value); }
    public bool RunVentureRefillDaily { get => _runVentureRefillDaily; set => SetAndSchedule(ref _runVentureRefillDaily, value); }
    public string VentureMinimumQuantity { get => _ventureMinimumQuantity; set => SetAndSchedule(ref _ventureMinimumQuantity, value); }
    public string VentureTargetQuantity { get => _ventureTargetQuantity; set => SetAndSchedule(ref _ventureTargetQuantity, value); }
    public bool RunCustomDeliveriesWeekly { get => _runCustomDeliveriesWeekly; set => SetAndSchedule(ref _runCustomDeliveriesWeekly, value); }
    public string CustomDeliveryClientKeys { get => _customDeliveryClientKeys; set => SetAndSchedule(ref _customDeliveryClientKeys, value); }
    public string CustomDeliveryCraftingClass { get => _customDeliveryCraftingClass; set => SetAndSchedule(ref _customDeliveryCraftingClass, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string RouteRecorderName { get => _routeRecorderName; set => SetProperty(ref _routeRecorderName, value); }
    public string RouteRecorderMinimumLevel { get => _routeRecorderMinimumLevel; set => SetProperty(ref _routeRecorderMinimumLevel, value); }
    public string RouteRecorderMaximumLevel { get => _routeRecorderMaximumLevel; set => SetProperty(ref _routeRecorderMaximumLevel, value); }
    public string RouteRecorderStatus { get => _routeRecorderStatus; private set => SetProperty(ref _routeRecorderStatus, value); }

    public async Task LoadAsync()
    {
        try
        {
            await _settings.LoadAsync().ConfigureAwait(true);
            Apply(_settings.Current.FirstPlayableLoop);
            OnPropertyChanged(nameof(SettingsPath));
            StatusMessage = $"Settings loaded from {SettingsPath}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load settings: {exception.Message}";
        }
    }

    public async Task FlushAsync()
    {
        try
        {
            SynchronizeSettingsFromEditor();
            await _settings.FlushAsync().ConfigureAwait(true);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        await _settings.FlushAsync().ConfigureAwait(false);
    }

    private async Task SaveAsync()
    {
        if (!TryBuildSettings(out var loopSettings, out var error))
        {
            StatusMessage = error;
            return;
        }

        var validation = loopSettings.Validate(requireDutyProfileExists: false);
        if (validation.Count > 0)
        {
            StatusMessage = string.Join(" ", validation);
            return;
        }

        try
        {
            _settings.Update(current => current with { FirstPlayableLoop = loopSettings });
            await _settings.FlushAsync().ConfigureAwait(true);
            StatusMessage = $"First-loop settings saved to {SettingsPath}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not save settings: {exception.Message}";
        }
    }

    private async Task RunAsync()
    {
        if (_controller is null)
        {
            StatusMessage = "The RebornBuddy loop controller is unavailable in this host.";
            return;
        }

        if (!TryBuildSettings(out var loopSettings, out var error))
        {
            StatusMessage = error;
            return;
        }

        var validation = loopSettings.Validate(requireDutyProfileExists: loopSettings.RunDutyLoop);
        if (validation.Count > 0)
        {
            StatusMessage = string.Join(" ", validation);
            return;
        }

        try
        {
            _settings.Update(current => current with { FirstPlayableLoop = loopSettings });
            await _settings.FlushAsync().ConfigureAwait(true);
            var result = await _controller.QueueAsync(loopSettings).ConfigureAwait(true);
            StatusMessage = result.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"First loop failed to start: {exception.Message}";
        }
    }

    private void StartRouteRecorder()
    {
        if (_routeRecorderController is null)
        {
            RouteRecorderStatus = "Route recorder is unavailable in this host.";
            return;
        }

        if (!TryUInt(QueueDutyId, "Queue/registration duty ID", false, out var queueDutyId, out var error) ||
            !TryUInt(DutyTerritoryId, "Duty territory/map ID", false, out var territoryId, out error) ||
            !TryInt(RouteRecorderMinimumLevel, "Recorder minimum level", out var minimumLevel, out error) ||
            !TryInt(RouteRecorderMaximumLevel, "Recorder maximum level", out var maximumLevel, out error))
        {
            RouteRecorderStatus = error;
            return;
        }

        if (string.IsNullOrWhiteSpace(RouteRecorderName))
        {
            RouteRecorderStatus = "Recorder route name is required.";
            return;
        }

        try
        {
            var result = _routeRecorderController.Start(new DutyRouteRecorderStartRequest(
                queueDutyId,
                territoryId,
                RouteRecorderName.Trim(),
                minimumLevel,
                maximumLevel));
            RouteRecorderStatus = result.Message;
        }
        catch (Exception exception)
        {
            RouteRecorderStatus = $"Recorder could not start: {exception.Message}";
        }
    }

    private void CaptureRouteTarget()
    {
        RouteRecorderStatus = _routeRecorderController?.CaptureCurrentTarget().Message
            ?? "Route recorder is unavailable in this host.";
    }

    private void StopRouteRecorder()
    {
        var result = _routeRecorderController?.StopAndSave();
        if (result is null)
        {
            RouteRecorderStatus = "Route recorder is unavailable in this host.";
            return;
        }

        RouteRecorderStatus = result.Message;
        if (result.Success && TryUInt(QueueDutyId, "Queue/registration duty ID", false, out var queueDutyId, out _))
        {
            var store = new JsonDutyNavigationProfileStore();
            DutyProfilePath = Path.Combine(store.DirectoryPath, $"duty-{queueDutyId}.json");
            RouteRecorderStatus += $" Native route selected: {DutyProfilePath}";
        }
    }

    private void SynchronizeSettingsFromEditor()
    {
        if (TryBuildSettings(out var loopSettings, out _))
        {
            _settings.Update(current => current with { FirstPlayableLoop = loopSettings });
        }
    }

    private void ScheduleSettingsUpdate()
    {
        if (!_applyingSettings)
        {
            SynchronizeSettingsFromEditor();
        }
    }

    private void OnSettingsChanged(object? sender, EZBuddySettings settings)
        => DispatchToUi(() =>
        {
            if (!_applyingSettings)
            {
                Apply(settings.FirstPlayableLoop);
            }
        });

    private bool TryBuildSettings(out FirstPlayableLoopSettings settings, out string error)
    {
        settings = new FirstPlayableLoopSettings();
        error = string.Empty;

        if (!TryUInt(QueueDutyId, "Queue/registration duty ID", allowEmpty: !RunDutyLoop, out var queueDutyId, out error) ||
            !TryUInt(DutyTerritoryId, "Duty territory/map ID", allowEmpty: true, out var territoryId, out error) ||
            !TryNullableInt(TrustId, "Trust ID", out var trustId, out error) ||
            !TryNullableInt(TargetLevel, "Target level", out var targetLevel, out error) ||
            !TryInt(MaxRuns, "Maximum runs", out var maxRuns, out error) ||
            !TryInt(DutyLootPassAtOrBelowFreeSlots, "Loot pass-at free-slot threshold", out var lootPassAt, out error) ||
            !TryInt(MinimumDutyFreeSlots, "Minimum duty free slots", out var minimumSlots, out error) ||
            !TryInt(InventoryTargetFreeSlots, "Inventory target free slots", out var targetSlots, out error) ||
            !TryInt(MinimumRetainerFreeSlots, "Minimum retainer free slots", out var minimumRetainerSlots, out error) ||
            !TryInt(AutoRepairThresholdPercent, "Auto-repair threshold", out var repairThreshold, out error) ||
            !TryInt(VentureMinimumQuantity, "Venture minimum quantity", out var ventureMinimum, out error) ||
            !TryInt(VentureTargetQuantity, "Venture target quantity", out var ventureTarget, out error) ||
            !TryUInt(FoodItemId, "Food item ID", allowEmpty: !RequireWellFed, out var foodItemId, out error) ||
            !TryUIntList(ApprovedExpertDeliveryItemIds, out var approvedItems, out error) ||
            !TryStringList(CustomDeliveryClientKeys, out var customDeliveryClients, out error))
        {
            return false;
        }

        var mode = string.Equals(DutyMode, nameof(DutyAutomationMode.Trust), StringComparison.OrdinalIgnoreCase)
            ? DutyAutomationMode.Trust
            : DutyAutomationMode.DutySupport;

        if (!Enum.TryParse<EZBuddy.Core.Duties.DutyLootAction>(DutyLootAction, true, out var lootAction))
        {
            error = "Duty loot action is invalid.";
            return false;
        }

        settings = new FirstPlayableLoopSettings(
            DutyId: queueDutyId,
            DutyProfilePath: DutyProfilePath.Trim(),
            DutyMode: mode,
            TrustId: trustId,
            TargetLevel: targetLevel,
            MaxRuns: maxRuns,
            MinimumDutyFreeSlots: minimumSlots,
            InventoryTargetFreeSlots: targetSlots,
            MinimumRetainerFreeSlots: minimumRetainerSlots,
            AutoRepairGear: AutoRepairGear,
            AutoRepairThresholdPercent: repairThreshold,
            AutoExtractMateria: AutoExtractMateria,
            FoodItemId: foodItemId,
            RequireWellFed: RequireWellFed,
            RunMaintenance: RunMaintenance,
            RunRetainers: RunRetainers,
            RunInventoryPressureRelief: RunInventoryPressureRelief,
            RunDailyProgression: RunDailyProgression,
            RunDutyLoop: RunDutyLoop,
            ReturnToIdle: ReturnToIdle,
            ApprovedExpertDeliveryItemIds: approvedItems,
            DutyTerritoryId: territoryId,
            DutyLootAction: lootAction,
            DutyLootPassAtOrBelowFreeSlots: lootPassAt,
            RunGrandCompanyExpertDeliveryDaily: RunGrandCompanyExpertDeliveryDaily,
            RunVentureRefillDaily: RunVentureRefillDaily,
            VentureMinimumQuantity: ventureMinimum,
            VentureTargetQuantity: ventureTarget,
            RunCustomDeliveriesWeekly: RunCustomDeliveriesWeekly,
            CustomDeliveryClientKeys: customDeliveryClients,
            CustomDeliveryCraftingClass: CustomDeliveryCraftingClass.Trim());
        return true;
    }

    private void Apply(FirstPlayableLoopSettings settings)
    {
        _applyingSettings = true;
        try
        {
            QueueDutyId = settings.QueueDutyId == 0 ? string.Empty : settings.QueueDutyId.ToString();
            DutyTerritoryId = settings.DutyTerritoryId == 0 ? string.Empty : settings.DutyTerritoryId.ToString();
            DutyProfilePath = settings.DutyProfilePath;
            DutyMode = settings.DutyMode.ToString();
            TrustId = settings.TrustId?.ToString() ?? string.Empty;
            TargetLevel = settings.TargetLevel?.ToString() ?? string.Empty;
            MaxRuns = settings.MaxRuns.ToString();
            DutyLootAction = settings.DutyLootAction.ToString();
            DutyLootPassAtOrBelowFreeSlots = settings.DutyLootPassAtOrBelowFreeSlots.ToString();
            MinimumDutyFreeSlots = settings.MinimumDutyFreeSlots.ToString();
            InventoryTargetFreeSlots = settings.InventoryTargetFreeSlots.ToString();
            MinimumRetainerFreeSlots = settings.MinimumRetainerFreeSlots.ToString();
            AutoRepairGear = settings.AutoRepairGear;
            AutoRepairThresholdPercent = settings.AutoRepairThresholdPercent.ToString();
            AutoExtractMateria = settings.AutoExtractMateria;
            FoodItemId = settings.FoodItemId == 0 ? string.Empty : settings.FoodItemId.ToString();
            ApprovedExpertDeliveryItemIds = string.Join(",", settings.EffectiveApprovedExpertDeliveryItemIds);
            RequireWellFed = settings.RequireWellFed;
            RunMaintenance = settings.RunMaintenance;
            RunRetainers = settings.RunRetainers;
            RunInventoryPressureRelief = settings.RunInventoryPressureRelief;
            RunDailyProgression = settings.RunDailyProgression;
            RunDutyLoop = settings.RunDutyLoop;
            ReturnToIdle = settings.ReturnToIdle;
            RunGrandCompanyExpertDeliveryDaily = settings.RunGrandCompanyExpertDeliveryDaily;
            RunVentureRefillDaily = settings.RunVentureRefillDaily;
            VentureMinimumQuantity = settings.VentureMinimumQuantity.ToString();
            VentureTargetQuantity = settings.VentureTargetQuantity.ToString();
            RunCustomDeliveriesWeekly = settings.RunCustomDeliveriesWeekly;
            CustomDeliveryClientKeys = string.Join(",", settings.EffectiveCustomDeliveryClientKeys);
            CustomDeliveryCraftingClass = settings.CustomDeliveryCraftingClass;
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private bool SetAndSchedule<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        ScheduleSettingsUpdate();
        return true;
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

    private static bool TryInt(string text, string label, out int value, out string error)
    {
        if (int.TryParse(text?.Trim(), out value))
        {
            error = string.Empty;
            return true;
        }

        error = $"{label} must be an integer.";
        return false;
    }

    private static bool TryNullableInt(string text, string label, out int? value, out string error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            error = string.Empty;
            return true;
        }

        if (int.TryParse(text.Trim(), out var parsed))
        {
            value = parsed;
            error = string.Empty;
            return true;
        }

        value = null;
        error = $"{label} must be an integer when configured.";
        return false;
    }

    private static bool TryUInt(string text, string label, bool allowEmpty, out uint value, out string error)
    {
        if (allowEmpty && string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            error = string.Empty;
            return true;
        }

        if (uint.TryParse(text?.Trim(), out value) && value > 0)
        {
            error = string.Empty;
            return true;
        }

        error = $"{label} must be a positive unsigned integer.";
        return false;
    }

    private static bool TryUIntList(string text, out IReadOnlyList<uint> values, out string error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            values = Array.Empty<uint>();
            error = string.Empty;
            return true;
        }

        var parsed = new HashSet<uint>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(part, out var itemId) || itemId == 0)
            {
                values = Array.Empty<uint>();
                error = $"Expert Delivery item ID '{part}' is invalid.";
                return false;
            }

            parsed.Add(itemId);
        }

        values = parsed.OrderBy(value => value).ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryStringList(string text, out IReadOnlyList<string> values, out string error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            values = Array.Empty<string>();
            error = string.Empty;
            return true;
        }

        var parsed = text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parsed.Any(value => value.Length > 80))
        {
            values = Array.Empty<string>();
            error = "Custom Delivery client keys must each be 80 characters or fewer.";
            return false;
        }

        values = parsed;
        error = string.Empty;
        return true;
    }
}
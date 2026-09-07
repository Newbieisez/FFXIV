using System.Windows.Input;
using EZBuddy.Core.Adapters;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Settings;
using EZBuddy.UI.Infrastructure;

namespace EZBuddy.UI.ViewModels;

public sealed class FirstPlayableLoopViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IFirstPlayableLoopController? _controller;
    private readonly IObservableSettings _settings;
    private bool _applyingSettings;

    private string _dutyId = string.Empty;
    private string _dutyProfilePath = string.Empty;
    private string _dutyMode = nameof(DutyAutomationMode.DutySupport);
    private string _trustId = string.Empty;
    private string _targetLevel = string.Empty;
    private string _maxRuns = "1";
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
    private string _statusMessage = "Load or configure the first playable loop, then run it.";

    public FirstPlayableLoopViewModel(
        IFirstPlayableLoopController? controller,
        IObservableSettings? settings = null)
    {
        _controller = controller;
        _settings = settings ?? new JsonEZBuddySettingsManager(new JsonEZBuddySettingsStore());
        _settings.SettingsChanged += OnSettingsChanged;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RunCommand = new AsyncRelayCommand(RunAsync);
        ReloadCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ICommand SaveCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand ReloadCommand { get; }

    public IReadOnlyList<string> DutyModes { get; } =
    [
        nameof(DutyAutomationMode.DutySupport),
        nameof(DutyAutomationMode.Trust)
    ];

    public string SettingsPath => _settings.FilePath;

    public string DutyId { get => _dutyId; set => SetAndSchedule(ref _dutyId, value); }
    public string DutyProfilePath { get => _dutyProfilePath; set => SetAndSchedule(ref _dutyProfilePath, value); }
    public string DutyMode { get => _dutyMode; set => SetAndSchedule(ref _dutyMode, value); }
    public string TrustId { get => _trustId; set => SetAndSchedule(ref _trustId, value); }
    public string TargetLevel { get => _targetLevel; set => SetAndSchedule(ref _targetLevel, value); }
    public string MaxRuns { get => _maxRuns; set => SetAndSchedule(ref _maxRuns, value); }
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
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

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
            // Window close/shutdown should not throw because a settings flush failed.
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

    private void SynchronizeSettingsFromEditor()
    {
        if (TryBuildSettings(out var loopSettings, out _))
        {
            _settings.Update(current => current with { FirstPlayableLoop = loopSettings });
        }
    }

    private void ScheduleSettingsUpdate()
    {
        if (_applyingSettings)
        {
            return;
        }

        SynchronizeSettingsFromEditor();
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

        if (!TryUInt(DutyId, "Duty ID", allowEmpty: !RunDutyLoop, out var dutyId, out error) ||
            !TryNullableInt(TrustId, "Trust ID", out var trustId, out error) ||
            !TryNullableInt(TargetLevel, "Target level", out var targetLevel, out error) ||
            !TryInt(MaxRuns, "Maximum runs", out var maxRuns, out error) ||
            !TryInt(MinimumDutyFreeSlots, "Minimum duty free slots", out var minimumSlots, out error) ||
            !TryInt(InventoryTargetFreeSlots, "Inventory target free slots", out var targetSlots, out error) ||
            !TryInt(MinimumRetainerFreeSlots, "Minimum retainer free slots", out var minimumRetainerSlots, out error) ||
            !TryInt(AutoRepairThresholdPercent, "Auto-repair threshold", out var repairThreshold, out error) ||
            !TryUInt(FoodItemId, "Food item ID", allowEmpty: !RequireWellFed, out var foodItemId, out error) ||
            !TryUIntList(ApprovedExpertDeliveryItemIds, out var approvedItems, out error))
        {
            return false;
        }

        var mode = string.Equals(DutyMode, nameof(DutyAutomationMode.Trust), StringComparison.OrdinalIgnoreCase)
            ? DutyAutomationMode.Trust
            : DutyAutomationMode.DutySupport;

        settings = new FirstPlayableLoopSettings(
            DutyId: dutyId,
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
            ApprovedExpertDeliveryItemIds: approvedItems);

        return true;
    }

    private void Apply(FirstPlayableLoopSettings settings)
    {
        _applyingSettings = true;
        try
        {
            DutyId = settings.DutyId == 0 ? string.Empty : settings.DutyId.ToString();
            DutyProfilePath = settings.DutyProfilePath;
            DutyMode = settings.DutyMode.ToString();
            TrustId = settings.TrustId?.ToString() ?? string.Empty;
            TargetLevel = settings.TargetLevel?.ToString() ?? string.Empty;
            MaxRuns = settings.MaxRuns.ToString();
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

        if (uint.TryParse(text?.Trim(), out value))
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
}

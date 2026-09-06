using EZBuddy.UI.Infrastructure;

namespace EZBuddy.UI.Models;

public sealed class NavigationModule : ObservableObject
{
    private bool _isEnabled;
    private bool _isSelected;

    public required string Group { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string Key { get; init; }
    public bool CanToggle { get; init; } = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class PipelineStep : ObservableObject
{
    private string _status = "Pending";

    public required int Order { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

public sealed class ActivityQueueItem : ObservableObject
{
    private string _state = "Queued";

    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required string StopCondition { get; init; }
    public string? Detail { get; init; }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
}

public sealed class IntegrationHealthItem : ObservableObject
{
    private string _state = "Unknown";
    private string _detail = string.Empty;

    public required string Name { get; init; }
    public required string Type { get; init; }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}

public sealed record TelemetryUpdate(
    string ApplicationStatus,
    string CurrentActivity,
    string CharacterName,
    string ServerName,
    string RoutineName,
    bool RoutineActive,
    long GilEarned,
    TimeSpan SessionRuntime,
    int ActiveHooks,
    int WarningCount);

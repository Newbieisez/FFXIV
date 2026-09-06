using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Licensing;

public static class LicenseRuntime
{
    private static readonly object Sync = new();
    private static LicenseManager? _manager;

    public static LicenseManager? Manager
    {
        get
        {
            lock (Sync)
            {
                return _manager;
            }
        }
    }

    public static LicenseStatus? CurrentStatus => Manager?.CurrentStatus;

    public static event EventHandler<LicenseStatus>? StatusChanged;
    public static event EventHandler<LicenseStatus?>? LicenseRequired;

    public static void Configure(LicenseManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        lock (Sync)
        {
            if (_manager is not null)
            {
                _manager.StatusChanged -= OnManagerStatusChanged;
            }

            _manager = manager;
            _manager.StatusChanged += OnManagerStatusChanged;
        }

        StatusChanged?.Invoke(null, manager.CurrentStatus);
    }

    public static void NotifyLicenseRequired()
        => LicenseRequired?.Invoke(null, CurrentStatus);

    private static void OnManagerStatusChanged(object? sender, LicenseStatus status)
        => StatusChanged?.Invoke(null, status);
}

public sealed class LicenseExecutionGuard : IExecutionGate
{
    public static LicenseExecutionGuard Instance { get; } = new();

    private LicenseExecutionGuard() { }

    public bool CanExecute(ActivityCategory category, out string message)
    {
        var manager = LicenseRuntime.Manager;
        if (manager is null)
        {
            message = "EZBuddy licensing has not been initialized.";
            LicenseRuntime.NotifyLicenseRequired();
            return false;
        }

        var status = manager.CurrentStatus;
        if (!status.IsValid)
        {
            message = status.Message;
            LicenseRuntime.NotifyLicenseRequired();
            return false;
        }

        if (status.ExpiresUtc is { } expiresUtc && expiresUtc <= DateTimeOffset.UtcNow)
        {
            message = $"License expired on {expiresUtc:yyyy-MM-dd HH:mm} UTC.";
            LicenseRuntime.NotifyLicenseRequired();
            return false;
        }

        var featureKey = LicenseFeatureMap.ForCategory(category);
        if (!status.HasFeature(featureKey))
        {
            message = $"Your {status.Tier} license does not include the '{featureKey}' feature.";
            LicenseRuntime.NotifyLicenseRequired();
            return false;
        }

        message = "License permits execution.";
        return true;
    }
}

public static class LicenseFeatureMap
{
    public static string ForCategory(ActivityCategory category)
        => category switch
        {
            ActivityCategory.Core => "core",
            ActivityCategory.Duty => "duty",
            ActivityCategory.Questing => "progression",
            ActivityCategory.Gathering => "gathering",
            ActivityCategory.Crafting => "crafting",
            ActivityCategory.Retainers => "retainers",
            ActivityCategory.Marketboard => "marketboard",
            ActivityCategory.Relics => "relics",
            ActivityCategory.Events => "events",
            ActivityCategory.Sanctuary => "sanctuary",
            ActivityCategory.GoldSaucer => "gold-saucer",
            ActivityCategory.TripleTriad => "triple-triad",
            ActivityCategory.DailyWeekly => "daily-weekly",
            ActivityCategory.Utility => "utility",
            _ => "core"
        };
}

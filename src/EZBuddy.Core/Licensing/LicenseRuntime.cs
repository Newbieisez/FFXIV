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

public sealed class RuntimeLicenseStatusProvider : ILicenseStatusProvider
{
    public static RuntimeLicenseStatusProvider Instance { get; } = new();

    private RuntimeLicenseStatusProvider() { }

    public LicenseStatus? CurrentStatus => LicenseRuntime.CurrentStatus;

    public void NotifyLicenseRequired() => LicenseRuntime.NotifyLicenseRequired();
}

public sealed class LicenseExecutionGuard : IExecutionGate
{
    private readonly ILicenseStatusProvider _statusProvider;
    private readonly Func<DateTimeOffset> _utcNow;

    public static LicenseExecutionGuard Instance { get; } = new(RuntimeLicenseStatusProvider.Instance);

    public LicenseExecutionGuard(
        ILicenseStatusProvider statusProvider,
        Func<DateTimeOffset>? utcNow = null)
    {
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public bool CanExecute(ActivityCategory category, out string message)
    {
        var status = _statusProvider.CurrentStatus;
        if (status is null)
        {
            message = "EZBuddy licensing has not been initialized.";
            _statusProvider.NotifyLicenseRequired();
            return false;
        }

        if (!status.IsValid)
        {
            message = status.Message;
            _statusProvider.NotifyLicenseRequired();
            return false;
        }

        if (status.ExpiresUtc is { } expiresUtc && expiresUtc <= _utcNow())
        {
            message = $"License expired on {expiresUtc:yyyy-MM-dd HH:mm} UTC.";
            _statusProvider.NotifyLicenseRequired();
            return false;
        }

        var featureKey = LicenseFeatureMap.ForCategory(category);
        if (!status.HasFeature(featureKey))
        {
            message = $"Your {status.Tier} license does not include the '{featureKey}' feature.";
            _statusProvider.NotifyLicenseRequired();
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

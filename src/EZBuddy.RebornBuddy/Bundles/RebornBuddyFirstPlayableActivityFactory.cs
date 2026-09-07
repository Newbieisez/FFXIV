using EZBuddy.Core.Adapters;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Activities;
using EZBuddy.RebornBuddy.Duties;
using EZBuddy.RebornBuddy.Retainers;

namespace EZBuddy.RebornBuddy.Bundles;

public sealed record FirstPlayableLoopConfiguration(
    DutySupportLevelingOptions Duty,
    MaintenanceOptions Maintenance,
    RetainerSweepOptions Retainers,
    InventoryPressureReliefOptions InventoryRelief,
    DutyNavigationProfile? NativeDutyRoute = null)
{
    public static FirstPlayableLoopConfiguration FromEnvironment()
    {
        var dutyId = ReadRequiredUInt("EZBUDDY_DUTY_ID");
        var profilePath = ReadRequired("EZBUDDY_DUTY_PROFILE");
        profilePath = Path.GetFullPath(profilePath);
        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException(
                "Configured EZBUDDY_DUTY_PROFILE does not exist.",
                profilePath);
        }

        var modeText = Environment.GetEnvironmentVariable("EZBUDDY_DUTY_MODE")?.Trim();
        var mode = string.Equals(modeText, "Trust", StringComparison.OrdinalIgnoreCase)
            ? DutyAutomationMode.Trust
            : DutyAutomationMode.DutySupport;

        var trustId = ReadOptionalInt("EZBUDDY_TRUST_ID");
        var targetLevel = ReadOptionalInt("EZBUDDY_DUTY_TARGET_LEVEL");
        var maxRuns = ReadOptionalInt("EZBUDDY_DUTY_MAX_RUNS") ?? 1;
        var minimumDutySlots = ReadOptionalInt("EZBUDDY_DUTY_MIN_FREE_SLOTS") ?? 6;
        var foodItemId = ReadOptionalUInt("EZBUDDY_FOOD_ITEM_ID") ?? 0;
        var requireWellFed = ReadOptionalBool("EZBUDDY_REQUIRE_WELL_FED");
        var approvedItems = ReadUIntList("EZBUDDY_EXPERT_DELIVERY_ITEM_IDS");
        var inventoryTarget = ReadOptionalInt("EZBUDDY_INVENTORY_TARGET_FREE_SLOTS") ?? 12;

        var duty = new DutySupportLevelingOptions(
            DutyId: dutyId,
            ProfilePath: profilePath,
            Mode: mode,
            TrustId: trustId,
            TargetLevel: targetLevel,
            MaxRuns: maxRuns,
            MinimumFreeInventorySlots: minimumDutySlots);
        duty.Validate();

        var maintenance = new MaintenanceOptions(
            MinimumFreeInventorySlots: Math.Min(minimumDutySlots, inventoryTarget),
            RepairBelowPercent: 30,
            FoodItemId: foodItemId,
            RequireWellFed: requireWellFed,
            AllowMenderFallback: true);
        maintenance.Validate();

        var retainers = new RetainerSweepOptions(
            MinimumFreeInventorySlots: Math.Max(8, minimumDutySlots),
            MinimumVentureTokens: 10,
            TargetVentureTokens: 50,
            VentureItemId: 21072,
            ApprovedExpertDeliveryItemIds: approvedItems);
        retainers.Validate();

        var inventoryRelief = new InventoryPressureReliefOptions(
            TargetFreeInventorySlots: inventoryTarget,
            ExtractMateriaBeforeTurnIn: true,
            ApprovedExpertDeliveryItemIds: approvedItems);
        inventoryRelief.Validate();

        return new FirstPlayableLoopConfiguration(
            duty,
            maintenance,
            retainers,
            inventoryRelief);
    }

    private static string ReadRequired(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} must be configured before queuing the first playable loop.");
        }

        return value;
    }

    private static uint ReadRequiredUInt(string key)
    {
        var value = ReadRequired(key);
        return uint.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"{key} must be a positive unsigned integer.");
    }

    private static int? ReadOptionalInt(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{key} must be an integer when configured.");
    }

    private static uint? ReadOptionalUInt(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return uint.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{key} must be an unsigned integer when configured.");
    }

    private static bool ReadOptionalBool(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : value == "1"
                ? true
                : value == "0"
                    ? false
                    : throw new InvalidOperationException($"{key} must be true, false, 1, or 0 when configured.");
    }

    private static IReadOnlyCollection<uint> ReadUIntList(string key)
    {
        var value = Environment.GetEnvironmentVariable(key)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<uint>();
        }

        var values = new HashSet<uint>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(part, out var parsed) || parsed == 0)
            {
                throw new InvalidOperationException($"{key} contains invalid item ID '{part}'.");
            }

            values.Add(parsed);
        }

        return values.ToArray();
    }
}

public sealed class RebornBuddyFirstPlayableActivityFactory : IFirstPlayableActivityFactory
{
    private readonly FirstPlayableLoopConfiguration _configuration;
    private readonly ILisbethAdapter _lisbeth;
    private readonly IRetainerSweepAdapter _retainers;
    private readonly IGrandCompanyAdapter _grandCompany;
    private readonly IDutySupportAdapter _dutySupport;
    private readonly IOrderBotAdapter _orderBot;
    private readonly IMagitekAdapter _magitek;

    public RebornBuddyFirstPlayableActivityFactory(FirstPlayableLoopConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _lisbeth = RequiredAdapter<ILisbethAdapter>();
        _retainers = RequiredAdapter<IRetainerSweepAdapter>();
        _grandCompany = RequiredAdapter<IGrandCompanyAdapter>();
        _dutySupport = RequiredAdapter<IDutySupportAdapter>();
        _orderBot = RequiredAdapter<IOrderBotAdapter>();
        _magitek = RequiredAdapter<IMagitekAdapter>();
    }

    public IEZActivity CreateMaintenanceActivity()
        => new MaintenanceActivity(_lisbeth, _configuration.Maintenance);

    public IEZActivity CreateRetainerSweepActivity()
        => new RetainerSweepActivity(
            _retainers,
            EZBuddyRuntime.RetainerBellCoordinator,
            _configuration.Retainers,
            new RebornBuddyRetainerBellAccess(),
            _grandCompany);

    public IEZActivity CreateInventoryPressureReliefActivity()
        => new InventoryPressureReliefActivity(
            _lisbeth,
            _grandCompany,
            _configuration.InventoryRelief);

    public IEZActivity CreateDailyProgressionActivity()
        => new DailyProgressionPlanningActivity();

    public IEZActivity CreateDutyLoopActivity()
    {
        IDutyInInstanceRunner? nativeRunner = null;
        if (_configuration.NativeDutyRoute is { } route)
        {
            route.Validate();
            if (route.QueueDutyId != _configuration.Duty.QueueDutyId ||
                route.TerritoryId != _configuration.Duty.TerritoryId)
            {
                throw new InvalidOperationException(
                    "Configured native duty route does not match the duty queue ID and territory ID selected for this loop.");
            }

            nativeRunner = new DutyObjectiveRouteRunner(
                route,
                new DutyObjectiveNodeExecutor(
                    new RebornBuddyDutyObjectiveNodeHost(),
                    new DutyObjectiveExecutorOptions(
                        MinimumDutyFreeSlots: _configuration.Duty.MinimumFreeInventorySlots)));
        }

        return new DutySupportLevelingActivity(
            _dutySupport,
            _orderBot,
            _magitek,
            new RebornBuddyDutyLevelingProgressProvider(),
            _configuration.Duty,
            new RebornBuddyDutyPostRunAdapter(),
            nativeRunner);
    }

    private static TAdapter RequiredAdapter<TAdapter>()
        where TAdapter : class, IEZAdapter
        => EZBuddyRuntime.Adapters.All.OfType<TAdapter>().FirstOrDefault()
           ?? throw new InvalidOperationException(
               $"Required EZBuddy adapter '{typeof(TAdapter).Name}' is not registered.");
}

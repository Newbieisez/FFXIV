using EZBuddy.Core.Adapters;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Diagnostics;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Routines;
using EZBuddy.Core.Runtime;
using EZBuddy.Core.Settings;
using EZBuddy.RebornBuddy.Activities;
using EZBuddy.RebornBuddy.Diagnostics;
using EZBuddy.RebornBuddy.Routines;

namespace EZBuddy.RebornBuddy.Bundles;

public sealed class RebornBuddyFirstPlayableLoopController : IFirstPlayableLoopController
{
    public async Task<FirstPlayableLoopStartResult> QueueAsync(
        FirstPlayableLoopSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var license = LicenseRuntime.CurrentStatus;
        if (license is null || !license.IsValid)
        {
            return FirstPlayableLoopStartResult.Rejected(
                $"An active EZBuddy license is required: {license?.Message ?? "Licensing is not initialized."}");
        }

        var validationErrors = settings.Validate(requireDutyProfileExists: settings.RunDutyLoop);
        if (validationErrors.Count > 0)
        {
            return FirstPlayableLoopStartResult.Rejected(string.Join(" ", validationErrors));
        }

        var conflicts = await new RebornBuddyConflictPreflight()
            .ScanAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        var criticalConflicts = conflicts
            .Where(finding => finding.Severity == ConflictSeverity.Critical && finding.ShouldPause)
            .ToArray();
        if (criticalConflicts.Length > 0)
        {
            return FirstPlayableLoopStartResult.Rejected(
                $"EZBuddy conflict pre-flight blocked this loop: {RebornBuddyConflictPreflight.Summarize(criticalConflicts)}");
        }

        var queue = EZBuddyRuntime.Queue;
        var active = queue.GetSnapshots()
            .Any(snapshot => snapshot.State is
                ActivityState.Pending or
                ActivityState.Waiting or
                ActivityState.Running or
                ActivityState.GentleStopping);

        if (active || queue.CurrentActivity is not null)
        {
            return FirstPlayableLoopStartResult.Rejected(
                "EZBuddy already has active or pending work. Finish, remove, or stop the existing queue before starting another first loop.");
        }

        try
        {
            var configuration = BuildConfiguration(settings);
            var factory = new RebornBuddyFirstPlayableActivityFactory(configuration);

            // Validate reset history and build opt-in weekly work before mutating the activity queue.
            // A corrupt completion ledger therefore fails closed without leaving a partially queued loop.
            var beforeProgressionActivities = await BuildBeforeProgressionActivitiesAsync(cancellationToken)
                .ConfigureAwait(false);

            var planner = new FirstPlayableBundlePlanner(queue, factory);
            var plan = planner.Enqueue(new FirstPlayableBundleOptions(
                RunMaintenance: settings.RunMaintenance,
                RunRetainerSweep: settings.RunRetainers,
                RunInventoryPressureRelief: settings.RunInventoryPressureRelief,
                RunDailyProgression: settings.RunDailyProgression,
                RunDutyLoop: settings.RunDutyLoop,
                ReturnToIdle: settings.ReturnToIdle,
                BasePriority: 10_000,
                MaxRetriesPerStage: 2,
                BeforeProgressionActivities: beforeProgressionActivities));

            if (plan.StageNames.Count == 0)
            {
                return FirstPlayableLoopStartResult.Rejected("No first-loop stages are enabled.");
            }

            await EZBuddyRuntime.RunLoop.StartAsync(cancellationToken).ConfigureAwait(false);

            var advisory = conflicts.Count == 0
                ? string.Empty
                : $" Advisory conflicts: {RebornBuddyConflictPreflight.Summarize(conflicts)}";
            return FirstPlayableLoopStartResult.Started(
                $"Queued {plan.StageNames.Count} stage(s): {string.Join(" -> ", plan.StageNames)}. The EZBuddy BotBase will consume the start signal on its next pulse.{advisory}",
                plan.StageNames);
        }
        catch (Exception exception)
        {
            return FirstPlayableLoopStartResult.Rejected(
                $"First loop could not be queued: {exception.Message}");
        }
    }

    private static async Task<IReadOnlyList<IEZActivity>> BuildBeforeProgressionActivitiesAsync(
        CancellationToken cancellationToken)
    {
        var selectedClients = RebornBuddyCustomDeliveryRoutineFactory.ReadClientKeys();
        if (selectedClients.Count == 0)
        {
            return Array.Empty<IEZActivity>();
        }

        var routine = EZRoutineCatalog.Find("custom-deliveries")
            ?? throw new InvalidOperationException("Custom Deliveries routine definition is missing.");

        var characterKey = SettingsPathSanitizer.Sanitize(ff14bot.Core.Player?.Name ?? "default");
        var completionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Settings",
            "EZBuddy",
            "Routines",
            characterKey + ".json");
        var completionStore = new JsonRoutineCompletionStore(completionPath);
        var resetPlanner = new ResetAwareRoutinePlanner(completionStore);
        var nowUtc = DateTimeOffset.UtcNow;

        var dueState = (await resetPlanner.GetDueAsync([routine], nowUtc, cancellationToken)
            .ConfigureAwait(false)).Single();
        if (!dueState.IsDue)
        {
            return Array.Empty<IEZActivity>();
        }

        var activityFactory = new RebornBuddyCustomDeliveryRoutineFactory();
        var activity = await activityFactory.CreateAsync(routine, cancellationToken).ConfigureAwait(false);
        if (activity is null)
        {
            return Array.Empty<IEZActivity>();
        }

        return [new RoutineCompletionActivity(routine.Key, activity, completionStore)];
    }

    private static FirstPlayableLoopConfiguration BuildConfiguration(FirstPlayableLoopSettings settings)
    {
        var profilePath = string.IsNullOrWhiteSpace(settings.DutyProfilePath)
            ? string.Empty
            : Path.GetFullPath(settings.DutyProfilePath);

        DutyNavigationProfile? nativeRoute = null;
        var useNativeRoute = settings.RunDutyLoop &&
                             string.Equals(Path.GetExtension(profilePath), ".json", StringComparison.OrdinalIgnoreCase);
        if (useNativeRoute)
        {
            var directory = Path.GetDirectoryName(profilePath)
                ?? throw new InvalidOperationException("Native duty route profile path has no parent directory.");
            var store = new JsonDutyNavigationProfileStore(directory);
            nativeRoute = store.Load(settings.QueueDutyId)
                ?? throw new InvalidDataException(
                    $"No valid EZBuddy duty route for queue ID {settings.QueueDutyId} was found in {directory}.");
            nativeRoute.Validate();

            if (!nativeRoute.IsRunnable)
            {
                throw new InvalidDataException("The selected EZBuddy duty route is not runnable.");
            }

            if (settings.DutyTerritoryId == 0 || nativeRoute.TerritoryId != settings.DutyTerritoryId)
            {
                throw new InvalidDataException(
                    $"Native route territory {nativeRoute.TerritoryId} does not match configured territory {settings.DutyTerritoryId}.");
            }
        }

        var approvedItems = settings.EffectiveApprovedExpertDeliveryItemIds
            .Where(itemId => itemId > 0)
            .Distinct()
            .ToArray();

        var duty = new DutySupportLevelingOptions(
            DutyId: settings.QueueDutyId,
            ProfilePath: profilePath,
            Mode: settings.DutyMode,
            TrustId: settings.TrustId,
            TargetLevel: settings.TargetLevel,
            MaxRuns: settings.MaxRuns,
            MinimumFreeInventorySlots: settings.MinimumDutyFreeSlots,
            LootPolicy: settings.EffectiveDutyLootPolicy,
            TerritoryId: settings.DutyTerritoryId,
            UseNativeRoute: useNativeRoute);

        if (settings.RunDutyLoop)
        {
            duty.Validate();
        }

        var maintenance = new MaintenanceOptions(
            MinimumFreeInventorySlots: Math.Min(settings.MinimumDutyFreeSlots, settings.InventoryTargetFreeSlots),
            AutoRepairGear: settings.AutoRepairGear,
            RepairBelowPercent: settings.AutoRepairThresholdPercent,
            AutoExtractMateria: settings.AutoExtractMateria,
            FoodItemId: settings.FoodItemId,
            RequireWellFed: settings.RequireWellFed,
            AllowMenderFallback: true);
        maintenance.Validate();

        var retainers = new RetainerSweepOptions(
            MinimumFreeInventorySlots: settings.MinimumRetainerFreeSlots,
            MinimumVentureTokens: 10,
            TargetVentureTokens: 50,
            VentureItemId: 21072,
            ApprovedExpertDeliveryItemIds: approvedItems);
        retainers.Validate();

        var inventoryRelief = new InventoryPressureReliefOptions(
            TargetFreeInventorySlots: settings.InventoryTargetFreeSlots,
            ExtractMateriaBeforeTurnIn: settings.AutoExtractMateria,
            ApprovedExpertDeliveryItemIds: approvedItems);
        inventoryRelief.Validate();

        return new FirstPlayableLoopConfiguration(
            duty,
            maintenance,
            retainers,
            inventoryRelief,
            nativeRoute);
    }
}
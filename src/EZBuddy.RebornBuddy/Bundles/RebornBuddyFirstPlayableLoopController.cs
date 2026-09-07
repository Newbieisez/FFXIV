using EZBuddy.Core.Adapters;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Duties;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Runtime;
using EZBuddy.Core.Settings;
using EZBuddy.RebornBuddy.Activities;

namespace EZBuddy.RebornBuddy.Bundles;

public sealed class RebornBuddyFirstPlayableLoopController : IFirstPlayableLoopController
{
    public Task<FirstPlayableLoopStartResult> QueueAsync(
        FirstPlayableLoopSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var license = LicenseRuntime.CurrentStatus;
        if (license is null || !license.IsValid)
        {
            return Task.FromResult(FirstPlayableLoopStartResult.Rejected(
                $"An active EZBuddy license is required: {license?.Message ?? "Licensing is not initialized."}"));
        }

        var validationErrors = settings.Validate(requireDutyProfileExists: settings.RunDutyLoop);
        if (validationErrors.Count > 0)
        {
            return Task.FromResult(FirstPlayableLoopStartResult.Rejected(string.Join(" ", validationErrors)));
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
            return Task.FromResult(FirstPlayableLoopStartResult.Rejected(
                "EZBuddy already has active or pending work. Finish, remove, or stop the existing queue before starting another first loop."));
        }

        try
        {
            var configuration = BuildConfiguration(settings);
            var factory = new RebornBuddyFirstPlayableActivityFactory(configuration);
            var planner = new FirstPlayableBundlePlanner(queue, factory);
            var plan = planner.Enqueue(new FirstPlayableBundleOptions(
                RunMaintenance: settings.RunMaintenance,
                RunRetainerSweep: settings.RunRetainers,
                RunInventoryPressureRelief: settings.RunInventoryPressureRelief,
                RunDailyProgression: settings.RunDailyProgression,
                RunDutyLoop: settings.RunDutyLoop,
                ReturnToIdle: settings.ReturnToIdle,
                BasePriority: 10_000,
                MaxRetriesPerStage: 2));

            if (plan.StageNames.Count == 0)
            {
                return Task.FromResult(FirstPlayableLoopStartResult.Rejected(
                    "No first-loop stages are enabled."));
            }

            queue.Start();

            return Task.FromResult(FirstPlayableLoopStartResult.Started(
                $"Queued {plan.StageNames.Count} stage(s): {string.Join(" -> ", plan.StageNames)}",
                plan.StageNames));
        }
        catch (Exception exception)
        {
            return Task.FromResult(FirstPlayableLoopStartResult.Rejected(
                $"First loop could not be queued: {exception.Message}"));
        }
    }

    private static FirstPlayableLoopConfiguration BuildConfiguration(FirstPlayableLoopSettings settings)
    {
        var profilePath = string.IsNullOrWhiteSpace(settings.DutyProfilePath)
            ? string.Empty
            : Path.GetFullPath(settings.DutyProfilePath);

        var approvedItems = settings.EffectiveApprovedExpertDeliveryItemIds
            .Where(itemId => itemId > 0)
            .Distinct()
            .ToArray();

        var duty = new DutySupportLevelingOptions(
            DutyId: settings.DutyId,
            ProfilePath: profilePath,
            Mode: settings.DutyMode,
            TrustId: settings.TrustId,
            TargetLevel: settings.TargetLevel,
            MaxRuns: settings.MaxRuns,
            MinimumFreeInventorySlots: settings.MinimumDutyFreeSlots);

        if (settings.RunDutyLoop)
        {
            duty.Validate();
        }

        var maintenance = new MaintenanceOptions(
            MinimumFreeInventorySlots: Math.Min(settings.MinimumDutyFreeSlots, settings.InventoryTargetFreeSlots),
            RepairBelowPercent: 30,
            FoodItemId: settings.FoodItemId,
            RequireWellFed: settings.RequireWellFed,
            AllowMenderFallback: true);
        maintenance.Validate();

        var retainers = new RetainerSweepOptions(
            MinimumFreeInventorySlots: Math.Max(8, settings.MinimumDutyFreeSlots),
            MinimumVentureTokens: 10,
            TargetVentureTokens: 50,
            VentureItemId: 21072,
            ApprovedExpertDeliveryItemIds: approvedItems);
        retainers.Validate();

        var inventoryRelief = new InventoryPressureReliefOptions(
            TargetFreeInventorySlots: settings.InventoryTargetFreeSlots,
            ExtractMateriaBeforeTurnIn: true,
            ApprovedExpertDeliveryItemIds: approvedItems);
        inventoryRelief.Validate();

        return new FirstPlayableLoopConfiguration(
            duty,
            maintenance,
            retainers,
            inventoryRelief);
    }
}

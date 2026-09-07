using EZBuddy.Core.Adapters;
using EZBuddy.Core.Goals;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Product;
using EZBuddy.Core.Runtime;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Settings;
using EZBuddy.UI.ViewModels;
using ff14bot.Managers;

namespace EZBuddy.Plugin;

public sealed class RebornBuddyProductIntelligenceProvider : IProductIntelligenceProvider
{
    public async Task<ProductIntelligenceUpdate> EvaluateAsync(
        GoalType goalType,
        string subject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = RebornBuddySettingsSession.GetOrCreate().Current.FirstPlayableLoop;
        var statuses = await EZBuddyRuntime.Adapters.GetStatusesAsync(cancellationToken).ConfigureAwait(true);

        var characterAvailable = ff14bot.Core.Player is not null && !ff14bot.Behavior.CommonBehaviors.IsLoading;
        var freeSlots = characterAvailable ? checked((int)InventoryManager.FreeSlots) : 0;
        var licenseReady = LicenseRuntime.CurrentStatus is { IsValid: true };
        var magitekReady = MagitekAdapter.IsActive(out var magitekDiagnostic);
        var routeVerified = !settings.RunDutyLoop || IsExistingProfile(settings.DutyProfilePath);

        var adapterSnapshots = statuses
            .Select(status => new AdapterRuntimeSnapshot(
                status.Key,
                status.Health is AdapterHealth.Ready or AdapterHealth.Busy or AdapterHealth.Degraded,
                status.Version?.ToString(),
                Message: status.Message))
            .ToArray();

        // Compatibility stamps are intentionally empty until a build/plugin combination has
        // been explicitly live-validated. Preflight will surface this as Needs Verification.
        var preflight = PreflightEvaluator.Evaluate(new PreflightSnapshot(
            characterAvailable,
            licenseReady,
            MagitekRequired: settings.RunDutyLoop,
            MagitekReady: !settings.RunDutyLoop || magitekReady,
            FreeInventorySlots: freeSlots,
            MinimumFreeInventorySlots: settings.RunDutyLoop ? settings.MinimumDutyFreeSlots : 0,
            RouteRequired: settings.RunDutyLoop,
            RouteVerified: routeVerified,
            Adapters: adapterSnapshots,
            CompatibilityStamps: Array.Empty<AdapterCompatibilityStamp>()));

        var commonBlockers = new List<string>();
        if (!characterAvailable) commonBlockers.Add("Character is not available.");
        if (!licenseReady) commonBlockers.Add("License does not currently permit execution.");

        bool AdapterReady(string key)
            => statuses.Any(status =>
                string.Equals(status.Key, key, StringComparison.OrdinalIgnoreCase) &&
                status.Health is AdapterHealth.Ready or AdapterHealth.Busy or AdapterHealth.Degraded);

        var dryRun = DryRunPlanner.Build([
            new DryRunCandidate(
                "maintenance", "Maintenance Check", "Utility", 100,
                settings.RunMaintenance, characterAvailable && licenseReady,
                Warnings: AdapterReady("lisbeth") ? Array.Empty<string>() : ["Lisbeth is unavailable; repair/materia operations may block if required."],
                Blockers: commonBlockers),
            new DryRunCandidate(
                "retainers", "Retainer Venture Sweep", "Retainers", 90,
                settings.RunRetainers, characterAvailable && licenseReady && AdapterReady("llama-retainers"),
                Warnings: AdapterReady("llama-retainers") ? Array.Empty<string>() : ["No ready retainer sweep bridge is detected."],
                Blockers: commonBlockers),
            new DryRunCandidate(
                "inventory-relief", "Inventory Pressure Relief", "Utility", 80,
                settings.RunInventoryPressureRelief, characterAvailable && licenseReady,
                IsDestructive: settings.EffectiveApprovedExpertDeliveryItemIds.Count > 0,
                DestructiveOptIn: settings.EffectiveApprovedExpertDeliveryItemIds.Count > 0,
                Warnings: settings.EffectiveApprovedExpertDeliveryItemIds.Count == 0 ? ["No GC Expert Delivery allowlist is configured; destructive relief will not occur."] : Array.Empty<string>(),
                Blockers: commonBlockers),
            new DryRunCandidate(
                "progression", "Daily Progression Scan", "Questing", 70,
                settings.RunDailyProgression, characterAvailable && licenseReady,
                Warnings: AdapterReady("orderbot") ? Array.Empty<string>() : ["Order Bot is unavailable; generated progression work may not execute."],
                Blockers: commonBlockers),
            new DryRunCandidate(
                "duty", "Duty Support / Trust Leveling", "Duty", 60,
                settings.RunDutyLoop,
                characterAvailable && licenseReady && magitekReady && routeVerified && AdapterReady("rebornbuddy-duty-support"),
                Warnings: magitekReady ? Array.Empty<string>() : [magitekDiagnostic],
                Blockers: BuildDutyBlockers(settings.RunDutyLoop, commonBlockers, routeVerified, magitekReady, AdapterReady("rebornbuddy-duty-support"))),
            new DryRunCandidate(
                "idle", "Return to Idle", "Utility", -100,
                settings.ReturnToIdle, characterAvailable && licenseReady,
                Blockers: commonBlockers)
        ]);

        var availableCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "maintenance", "inventory-maintenance", "duty-support-leveling", "retainers",
            "craft-gather", "dailies", "wondrous-tails", "custom-deliveries", "gold-saucer",
            "smart-gear", "procurement", "collections", "desynthesis", "duties"
        };
        var goal = GoalPlanner.Build(new GoalRequest(goalType, string.IsNullOrWhiteSpace(subject) ? "My character" : subject.Trim()), availableCapabilities);

        var recommendations = preflight.Checks
            .Where(check => check.Severity != PreflightSeverity.Ready)
            .Select(check => $"{check.Severity}: {check.Title} — {check.Message}")
            .Concat(dryRun.Actions
                .Where(action => action.Disposition is DryRunDisposition.Blocked or DryRunDisposition.RequiresOptIn)
                .Select(action => $"Dry Run: {action.Title} — {action.Explanation}"))
            .Concat(goal.Steps
                .Where(step => !step.Available)
                .Select(step => $"Goal: {step.Title} is not available yet."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProductIntelligenceUpdate(preflight, dryRun, goal, recommendations);
    }

    private static IReadOnlyList<string> BuildDutyBlockers(
        bool enabled,
        IReadOnlyList<string> common,
        bool routeVerified,
        bool magitekReady,
        bool dutyAdapterReady)
    {
        if (!enabled)
        {
            return common;
        }

        var blockers = new List<string>(common);
        if (!routeVerified) blockers.Add("Configured duty profile/route does not exist.");
        if (!magitekReady) blockers.Add("Magitek is required for duty combat.");
        if (!dutyAdapterReady) blockers.Add("Duty Support / Trust adapter is unavailable.");
        return blockers;
    }

    private static bool IsExistingProfile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(Path.GetFullPath(path));
        }
        catch
        {
            return false;
        }
    }
}

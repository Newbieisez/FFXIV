using System.IO;
using EZBuddy.Core.Adapters;
using EZBuddy.Core.Gear;
using EZBuddy.Core.Goals;
using EZBuddy.Core.Licensing;
using EZBuddy.Core.Product;
using EZBuddy.Core.Runtime;
using EZBuddy.Core.Settings;
using EZBuddy.RebornBuddy.Adapters;
using EZBuddy.RebornBuddy.Product;
using EZBuddy.RebornBuddy.Settings;
using EZBuddy.UI.ViewModels;
using ff14bot.Managers;

namespace EZBuddy.Plugin;

public sealed class RebornBuddyProductIntelligenceProvider : IProductIntelligenceProvider
{
    private const int DefaultSmartGearKeepItemLevel = 730;

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
            "smart-gear", "currency-guard", "procurement", "collections", "materia", "desynthesis", "duties"
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
            .Concat(await BuildProductSnapshotRecommendationsAsync(characterAvailable, cancellationToken).ConfigureAwait(true))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProductIntelligenceUpdate(preflight, dryRun, goal, recommendations);
    }

    private static async Task<IReadOnlyList<string>> BuildProductSnapshotRecommendationsAsync(
        bool characterAvailable,
        CancellationToken cancellationToken)
    {
        var characterName = ff14bot.Core.Player?.Name ?? "default";
        var paths = new EZBuddyStoragePaths();
        var store = new JsonProductSnapshotStore(paths.GetProductSnapshotPath(characterName));

        string? captureWarning = null;
        var previousSnapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(true);
        ProductSnapshotBundle? snapshot = previousSnapshot;
        if (characterAvailable)
        {
            try
            {
                var collector = ProductSnapshotRuntime.Collector ?? new RebornBuddyProductSnapshotCollector();
                var liveSnapshot = await collector.CaptureAsync(cancellationToken).ConfigureAwait(true);
                snapshot = ProductSnapshotMerger.MergeLive(liveSnapshot, previousSnapshot);
                await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                snapshot = previousSnapshot;
                captureWarning = $"Product Snapshot: live capture failed; using the last good saved snapshot if available — {exception.Message}";
            }
        }

        if (snapshot is null)
        {
            return captureWarning is null
                ? ["Product Snapshot: no saved snapshot is available yet; Smart Gear, currency-cap, collections, procurement, and materia analysis will activate after a snapshot is captured."]
                : [captureWarning, "Product Snapshot: no last-good snapshot was available for fallback analysis."];
        }

        var jobKey = characterAvailable
            ? ff14bot.Core.Me.CurrentJob.ToString()
            : "Adventurer";

        ProductAnalysisResult analysis;
        try
        {
            analysis = ProductSnapshotAnalyzer.Analyze(
                snapshot,
                new ProductAnalysisRequest(
                    jobKey,
                    DefaultSmartGearKeepItemLevel));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return [$"Product Snapshot: saved data could not be analyzed safely — {exception.Message}"];
        }

        var output = new List<string>();
        if (captureWarning is not null)
        {
            output.Add(captureWarning);
        }

        var age = DateTimeOffset.UtcNow - snapshot.CapturedAtUtc;
        output.Add($"Product Snapshot: {snapshot.Gear.Count} gear row(s), {snapshot.Currencies.Count} currency row(s), {snapshot.Collections.Count} collection row(s); captured {FormatAge(age)} ago for {snapshot.CharacterKey}.");

        if (analysis.Gear is not null)
        {
            output.AddRange(analysis.Gear.Recommendations
                .Where(recommendation => recommendation.Disposition == GearDisposition.EquipBest)
                .OrderByDescending(recommendation => recommendation.Item.ItemLevel)
                .Take(8)
                .Select(recommendation => $"Smart Gear: {recommendation.Item.Name} (iLvl {recommendation.Item.ItemLevel}) is the best owned {recommendation.Item.Slot} candidate for {jobKey}."));

            var belowFloorCandidates = analysis.Gear.Recommendations.Count(recommendation =>
                recommendation.Item.ItemLevel < DefaultSmartGearKeepItemLevel &&
                recommendation.Disposition is GearDisposition.ExpertDeliveryCandidate or GearDisposition.DesynthesisCandidate or GearDisposition.SellCandidate or GearDisposition.Retainer);
            if (belowFloorCandidates > 0)
            {
                output.Add($"Smart Gear: {belowFloorCandidates} non-equipped item(s) are below the advisory iLvl {DefaultSmartGearKeepItemLevel} keep floor. They remain non-destructive candidates until an approved disposal rule exists.");
            }
        }

        output.AddRange(analysis.Currency.Warnings.Select(warning => $"Currency Guard: {warning}"));
        output.AddRange(analysis.Currency.SpendItems.Select(item =>
            $"Currency Guard: approved plan would spend {item.TotalCost:N0} {item.CurrencyKey} on {item.Quantity} × {item.DisplayName}."));

        output.AddRange(analysis.Collections
            .Take(5)
            .Select(target => $"Collection: {target.Item.Name} ({target.Item.CollectionType}) — {target.Recommendation}"));

        if (analysis.Procurement is not null)
        {
            output.AddRange(analysis.Procurement.Blockers.Select(blocker => $"Procurement: {blocker}"));
        }

        output.AddRange(analysis.Materia
            .Where(plan => !plan.MeetsTargets)
            .Select(plan => $"Materia: {plan.Request.GearName} still has unmet meld targets after planning."));
        output.AddRange(analysis.Warnings.Select(warning => $"Product Analysis: {warning}"));

        return output;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            return "0 minutes";
        }

        if (age.TotalDays >= 1)
        {
            return $"{Math.Floor(age.TotalDays):N0} day(s)";
        }

        if (age.TotalHours >= 1)
        {
            return $"{Math.Floor(age.TotalHours):N0} hour(s)";
        }

        return $"{Math.Max(0, Math.Floor(age.TotalMinutes)):N0} minute(s)";
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

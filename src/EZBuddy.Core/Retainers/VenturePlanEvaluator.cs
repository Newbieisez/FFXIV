namespace EZBuddy.Core.Retainers;

public sealed class VenturePlanEvaluator
{
    private readonly IVentureInventoryReader _inventory;
    private readonly IReadOnlyDictionary<string, IVentureConditionProvider> _providers;

    public VenturePlanEvaluator(
        IVentureInventoryReader inventory,
        IEnumerable<IVentureConditionProvider>? providers = null)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _providers = (providers ?? Array.Empty<IVentureConditionProvider>())
            .Where(p => p.IsAllowlisted)
            .ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<VenturePlanEvaluation> EvaluateNextAsync(
        VenturePlan plan,
        RetainerDescriptor retainer,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(retainer);

        if (!string.Equals(plan.RetainerJob, retainer.Job, StringComparison.OrdinalIgnoreCase))
        {
            return new VenturePlanEvaluation(null, false, $"Plan requires {plan.RetainerJob}; retainer is {retainer.Job}.");
        }

        if (plan.Entries.Count == 0)
        {
            return new VenturePlanEvaluation(null, true, "Plan contains no entries.");
        }

        var normalizedStart = Math.Clamp(startIndex, 0, plan.Entries.Count - 1);
        for (var offset = 0; offset < plan.Entries.Count; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = (normalizedStart + offset) % plan.Entries.Count;
            var entry = plan.Entries[index];
            if (!entry.Enabled)
            {
                continue;
            }

            if (await IsEligibleAsync(retainer, entry, cancellationToken).ConfigureAwait(false))
            {
                return new VenturePlanEvaluation(entry, false, $"Selected entry {index + 1} of {plan.Entries.Count}.");
            }
        }

        return plan.CompletionBehavior switch
        {
            VenturePlanCompletionBehavior.Loop => new VenturePlanEvaluation(null, true, "No eligible entry; loop has nothing eligible to run."),
            VenturePlanCompletionBehavior.QuickExploration => new VenturePlanEvaluation(null, true, "No eligible entry; switch to Quick Exploration."),
            VenturePlanCompletionBehavior.CollectOnly => new VenturePlanEvaluation(null, true, "No eligible entry; collect only."),
            VenturePlanCompletionBehavior.WaitForEligibleEntry => new VenturePlanEvaluation(null, false, "No eligible entry; waiting for a condition to become true."),
            _ => new VenturePlanEvaluation(null, true, "No eligible venture entry.")
        };
    }

    public async Task<VenturePlanEvaluation> EvaluateNextAsync(
        VenturePlan plan,
        RetainerDescriptor retainer,
        VentureExecutionContext executionContext,
        RetainerSafetySettings safetySettings,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(retainer);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(safetySettings);

        if (executionContext.Retainer.RetainerId != retainer.RetainerId)
        {
            return new VenturePlanEvaluation(null, false, "Safety context belongs to a different retainer.");
        }

        if (!PassesSafetyGates(executionContext, safetySettings, out var safetyReason))
        {
            return new VenturePlanEvaluation(null, false, $"Venture dispatch blocked by safety gate: {safetyReason}");
        }

        return await EvaluateNextAsync(plan, retainer, startIndex, cancellationToken).ConfigureAwait(false);
    }

    public static bool PassesSafetyGates(VentureExecutionContext context, RetainerSafetySettings settings, out string reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.MinimumFreeInventorySlots < 0 || settings.MinimumFreeInventorySlotsForQuickExploration < 0)
        {
            reason = "Inventory safety thresholds cannot be negative.";
            return false;
        }

        if (settings.MinimumVentureTokens < 0)
        {
            reason = "Venture-token safety threshold cannot be negative.";
            return false;
        }

        if (context.EmergencyStopRequested)
        {
            reason = "Emergency stop requested.";
            return false;
        }

        if (context.GentleStopRequested)
        {
            reason = "Gentle stop requested.";
            return false;
        }

        if (context.FreeInventorySlots < settings.MinimumFreeInventorySlots)
        {
            reason = $"Only {context.FreeInventorySlots} free inventory slots; minimum is {settings.MinimumFreeInventorySlots}.";
            return false;
        }

        if (context.VentureTokens < settings.MinimumVentureTokens)
        {
            reason = $"Only {context.VentureTokens} ventures remain; minimum is {settings.MinimumVentureTokens}.";
            return false;
        }

        if (settings.RequireSafeClientState && !context.IsClientStateSafe)
        {
            reason = "Client state is not safe for retainer interaction.";
            return false;
        }

        if (settings.StopOnCriticalConflict && context.HasCriticalConflict)
        {
            reason = "Conflict Guard reported a critical retainer/bell conflict.";
            return false;
        }

        reason = "Safety gates passed.";
        return true;
    }

    public static bool CanDispatchQuickExploration(
        VentureExecutionContext context,
        RetainerSafetySettings settings,
        out string reason)
    {
        if (!PassesSafetyGates(context, settings, out reason))
        {
            return false;
        }

        var quickExplorationFloor = Math.Max(
            settings.MinimumFreeInventorySlots,
            settings.MinimumFreeInventorySlotsForQuickExploration);

        if (context.FreeInventorySlots < quickExplorationFloor)
        {
            reason = $"Quick Exploration requires at least {quickExplorationFloor} free inventory slots; only {context.FreeInventorySlots} are available.";
            return false;
        }

        reason = "Quick Exploration inventory and venture-token safety gates passed.";
        return true;
    }

    private async Task<bool> IsEligibleAsync(
        RetainerDescriptor retainer,
        VenturePlanEntry entry,
        CancellationToken cancellationToken)
    {
        var itemId = entry.ConditionItemId;
        var threshold = entry.ConditionThreshold;

        return entry.ConditionType switch
        {
            VentureConditionType.Always => true,
            VentureConditionType.PlayerHasLessThan =>
                itemId.HasValue && threshold.HasValue && _inventory.GetPlayerQuantity(itemId.Value) < threshold.Value,
            VentureConditionType.RetainerHasLessThan =>
                itemId.HasValue && threshold.HasValue && _inventory.GetRetainerQuantity(retainer.RetainerId, itemId.Value) < threshold.Value,
            VentureConditionType.CombinedHasLessThan =>
                itemId.HasValue && threshold.HasValue &&
                _inventory.GetPlayerQuantity(itemId.Value) + _inventory.GetRetainerQuantity(retainer.RetainerId, itemId.Value) < threshold.Value,
            VentureConditionType.AllowlistedProviderCondition =>
                await EvaluateProviderConditionAsync(retainer, entry, cancellationToken).ConfigureAwait(false),
            _ => false
        };
    }

    private async Task<bool> EvaluateProviderConditionAsync(
        RetainerDescriptor retainer,
        VenturePlanEntry entry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.Label))
        {
            return false;
        }

        if (!_providers.TryGetValue(entry.Label, out var provider))
        {
            return false;
        }

        return await provider.EvaluateAsync(retainer, entry, cancellationToken).ConfigureAwait(false);
    }
}

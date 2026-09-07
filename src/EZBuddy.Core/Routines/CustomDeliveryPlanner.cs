namespace EZBuddy.Core.Routines;

public enum CustomDeliveryWorkKind
{
    Craft,
    Gather,
    Fish
}

public enum CustomDeliveryExecutionProvider
{
    LisbethCraft,
    LisbethGather,
    ManualFishing,
    HandInOnly
}

public sealed record CustomDeliveryRequest(
    uint ItemId,
    string ItemName,
    CustomDeliveryWorkKind WorkKind,
    int ItemsPerDelivery = 1)
{
    public void Validate()
    {
        if (ItemId == 0)
        {
            throw new InvalidDataException("Custom Delivery requests require a non-zero item ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ItemName);
        if (ItemsPerDelivery is < 1 or > 99)
        {
            throw new InvalidDataException("Custom Delivery items per delivery must be between 1 and 99.");
        }
    }
}

public sealed record CustomDeliveryClientSnapshot(
    string ClientKey,
    string DisplayName,
    bool IsUnlocked,
    int DeliveriesRemaining,
    CustomDeliveryRequest? CurrentRequest,
    int Priority = 0)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        if (DeliveriesRemaining is < 0 or > CustomDeliveryPlanner.MaximumDeliveriesPerClient)
        {
            throw new InvalidDataException(
                $"Custom Delivery client '{DisplayName}' has invalid remaining allowance {DeliveriesRemaining}.");
        }

        CurrentRequest?.Validate();
    }
}

public sealed record CustomDeliveryWeeklySnapshot(
    int WeeklyAllowancesRemaining,
    IReadOnlyList<CustomDeliveryClientSnapshot> Clients)
{
    public void Validate()
    {
        if (WeeklyAllowancesRemaining is < 0 or > CustomDeliveryPlanner.MaximumWeeklyDeliveries)
        {
            throw new InvalidDataException(
                $"Custom Delivery weekly allowance must be between 0 and {CustomDeliveryPlanner.MaximumWeeklyDeliveries}.");
        }

        ArgumentNullException.ThrowIfNull(Clients);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in Clients)
        {
            client.Validate();
            if (!keys.Add(client.ClientKey))
            {
                throw new InvalidDataException($"Duplicate Custom Delivery client key '{client.ClientKey}'.");
            }
        }
    }
}

public sealed record CustomDeliveryPlannerOptions(
    IReadOnlySet<string>? EnabledClientKeys = null,
    int MaximumDeliveriesThisRun = CustomDeliveryPlanner.MaximumWeeklyDeliveries,
    bool AllowCrafting = true,
    bool AllowGathering = true,
    bool AllowManualFishingSteps = false)
{
    public IReadOnlySet<string> EffectiveEnabledClientKeys =>
        EnabledClientKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (MaximumDeliveriesThisRun is < 1 or > CustomDeliveryPlanner.MaximumWeeklyDeliveries)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDeliveriesThisRun));
        }
    }
}

public sealed record CustomDeliveryPlanStep(
    string ClientKey,
    string ClientName,
    uint ItemId,
    string ItemName,
    CustomDeliveryWorkKind WorkKind,
    CustomDeliveryExecutionProvider Provider,
    int DeliveryCount,
    int RequiredItemQuantity);

public sealed record CustomDeliveryPlan(
    int WeeklyAllowancesAvailable,
    int PlannedDeliveries,
    IReadOnlyList<CustomDeliveryPlanStep> Steps,
    IReadOnlyList<string> SkippedClients,
    string Summary)
{
    public int RemainingUnplannedAllowances => Math.Max(0, WeeklyAllowancesAvailable - PlannedDeliveries);
}

/// <summary>
/// Plans Custom Deliveries from host-provided client/request state. It does not guess requested
/// items, collectability thresholds, or provider JSON. Current game limits are enforced centrally.
/// </summary>
public static class CustomDeliveryPlanner
{
    public const int MaximumWeeklyDeliveries = 12;
    public const int MaximumDeliveriesPerClient = 6;

    public static CustomDeliveryPlan Build(
        CustomDeliveryWeeklySnapshot snapshot,
        CustomDeliveryPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        options ??= new CustomDeliveryPlannerOptions();
        options.Validate();

        var remaining = Math.Min(snapshot.WeeklyAllowancesRemaining, options.MaximumDeliveriesThisRun);
        var steps = new List<CustomDeliveryPlanStep>();
        var skipped = new List<string>();

        if (remaining == 0)
        {
            return new CustomDeliveryPlan(
                snapshot.WeeklyAllowancesRemaining,
                0,
                [],
                [],
                "No Custom Delivery allowances remain for the current weekly period.");
        }

        foreach (var client in snapshot.Clients
                     .OrderByDescending(client => client.Priority)
                     .ThenBy(client => client.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (remaining == 0)
            {
                break;
            }

            if (!client.IsUnlocked)
            {
                skipped.Add($"{client.DisplayName}: not unlocked.");
                continue;
            }

            if (options.EffectiveEnabledClientKeys.Count > 0 &&
                !options.EffectiveEnabledClientKeys.Contains(client.ClientKey))
            {
                skipped.Add($"{client.DisplayName}: disabled by configuration.");
                continue;
            }

            if (client.DeliveriesRemaining <= 0)
            {
                skipped.Add($"{client.DisplayName}: client allowance exhausted.");
                continue;
            }

            if (client.CurrentRequest is null)
            {
                skipped.Add($"{client.DisplayName}: current requested item is unknown; refusing to guess.");
                continue;
            }

            var provider = SelectProvider(client.CurrentRequest.WorkKind, options);
            if (provider is null)
            {
                skipped.Add($"{client.DisplayName}: {client.CurrentRequest.WorkKind} work is disabled or unsupported for this run.");
                continue;
            }

            var deliveries = Math.Min(remaining, Math.Min(client.DeliveriesRemaining, MaximumDeliveriesPerClient));
            if (deliveries <= 0)
            {
                continue;
            }

            steps.Add(new CustomDeliveryPlanStep(
                client.ClientKey,
                client.DisplayName,
                client.CurrentRequest.ItemId,
                client.CurrentRequest.ItemName,
                client.CurrentRequest.WorkKind,
                provider.Value,
                deliveries,
                checked(deliveries * client.CurrentRequest.ItemsPerDelivery)));
            remaining -= deliveries;
        }

        var planned = steps.Sum(step => step.DeliveryCount);
        var summary = planned == 0
            ? "Custom Delivery allowances remain, but no configured/unlocked client has a supported verified request."
            : $"Planned {planned} of {snapshot.WeeklyAllowancesRemaining} remaining weekly Custom Delivery allowance(s) across {steps.Count} client(s).";

        return new CustomDeliveryPlan(
            snapshot.WeeklyAllowancesRemaining,
            planned,
            steps,
            skipped,
            summary);
    }

    private static CustomDeliveryExecutionProvider? SelectProvider(
        CustomDeliveryWorkKind workKind,
        CustomDeliveryPlannerOptions options)
        => workKind switch
        {
            CustomDeliveryWorkKind.Craft when options.AllowCrafting => CustomDeliveryExecutionProvider.LisbethCraft,
            CustomDeliveryWorkKind.Gather when options.AllowGathering => CustomDeliveryExecutionProvider.LisbethGather,
            CustomDeliveryWorkKind.Fish when options.AllowManualFishingSteps => CustomDeliveryExecutionProvider.ManualFishing,
            _ => null
        };
}

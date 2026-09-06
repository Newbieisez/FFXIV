namespace EZBuddy.Core.Retainers;

public enum VentureReturnBehavior
{
    RepeatPrevious,
    QuickExploration,
    VenturePlan,
    CollectOnly
}

public enum VenturePlanCompletionBehavior
{
    Loop,
    QuickExploration,
    CollectOnly,
    WaitForEligibleEntry
}

public enum VentureConditionType
{
    Always,
    PlayerHasLessThan,
    RetainerHasLessThan,
    CombinedHasLessThan,
    AllowlistedProviderCondition
}

public sealed record RetainerDescriptor(
    ulong RetainerId,
    string Name,
    string Job,
    int Level,
    int? Gathering,
    int? Perception,
    int? ItemLevel);

public sealed record VentureDefinition(
    uint VentureId,
    string Name,
    string RetainerJob,
    int MinimumLevel,
    int VentureCost,
    TimeSpan Duration,
    uint? RewardItemId,
    int? RequiredGathering,
    int? RequiredPerception,
    int? RequiredItemLevel,
    int? MinimumExpectedQuantity,
    int? MaximumExpectedQuantity);

public sealed record VenturePlanEntry(
    Guid Id,
    uint VentureId,
    bool Enabled,
    VentureConditionType ConditionType,
    uint? ConditionItemId,
    int? ConditionThreshold,
    int? Iterations,
    string? Label = null);

public sealed record VenturePlan(
    Guid Id,
    string Name,
    string RetainerJob,
    IReadOnlyList<VenturePlanEntry> Entries,
    VenturePlanCompletionBehavior CompletionBehavior,
    int SchemaVersion = 1);

public sealed record RetainerVenturePolicy(
    ulong RetainerId,
    VentureReturnBehavior ReturnBehavior,
    Guid? VenturePlanId,
    bool EntrustDuplicateStacks,
    bool CollectGil);

public sealed record RetainerSafetySettings(
    int MinimumFreeInventorySlots,
    int MinimumVentureTokens,
    bool RequireSafeClientState = true,
    bool StopOnCriticalConflict = true,
    int MinimumFreeInventorySlotsForQuickExploration = 8)
{
    public static RetainerSafetySettings Default { get; } = new(5, 10, MinimumFreeInventorySlotsForQuickExploration: 8);
}

public sealed record VentureExecutionContext(
    RetainerDescriptor Retainer,
    int FreeInventorySlots,
    int VentureTokens,
    bool IsClientStateSafe,
    bool HasCriticalConflict,
    bool GentleStopRequested,
    bool EmergencyStopRequested);

public sealed record VenturePlanEvaluation(
    VenturePlanEntry? SelectedEntry,
    bool IsPlanComplete,
    string Reason);

public sealed record VentureJournalEntry(
    DateTimeOffset Timestamp,
    string RetainerName,
    string Action,
    string Outcome,
    string? Detail = null,
    string? TriggerSource = null);

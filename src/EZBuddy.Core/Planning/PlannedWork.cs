namespace EZBuddy.Core.Planning;

public enum PlanRisk
{
    Low,
    Medium,
    High,
    ManualReview
}

public sealed record PlannedWork(
    string Key,
    string Title,
    int Priority,
    PlanRisk Risk,
    string Reason,
    IReadOnlyDictionary<string, string>? Metadata = null);

namespace EZBuddy.Core.Progression;

public enum ProgressionTargetType
{
    Quest,
    AetherCurrentOverworld,
    AetherCurrentQuest,
    JobUnlock,
    JobMilestone,
    RoleQuest,
    HuntingLog,
    ManualRequirement
}

public enum ProgressionCompletionState
{
    Unknown,
    Completed,
    Active,
    Missing,
    Blocked,
    ManualRequired
}

public enum ProgressionAutomationKind
{
    None,
    QuestPickup,
    AetherCurrentInteract,
    ExistingProfile,
    Manual
}

public sealed record ProgressionNode(
    string Id,
    string Name,
    ProgressionTargetType TargetType,
    uint SheetId,
    uint TerritoryId = 0,
    uint AetheryteId = 0,
    float X = 0,
    float Y = 0,
    float Z = 0,
    uint NpcId = 0,
    string Description = "",
    ProgressionAutomationKind AutomationKind = ProgressionAutomationKind.None,
    string? ExistingProfilePath = null,
    IReadOnlyList<string>? PrerequisiteNodeIds = null)
{
    public IReadOnlyList<string> Prerequisites { get; init; } = PrerequisiteNodeIds ?? Array.Empty<string>();
}

public sealed record ProgressionNodeInspection(
    ProgressionNode Node,
    ProgressionCompletionState State,
    string Reason,
    DateTimeOffset CheckedAt);

public sealed record CompletionReport(
    IReadOnlyList<ProgressionNodeInspection> Completed,
    IReadOnlyList<ProgressionNodeInspection> Active,
    IReadOnlyList<ProgressionNodeInspection> Missing,
    IReadOnlyList<ProgressionNodeInspection> Manual,
    DateTimeOffset GeneratedAt)
{
    public int Total => Completed.Count + Active.Count + Missing.Count + Manual.Count;
    public int CompletedCount => Completed.Count;
    public double PercentComplete => Total == 0 ? 100d : (double)CompletedCount / Total * 100d;
}

public sealed record ProgressionChecklist(
    IReadOnlyList<ProgressionNodeInspection> OrderedNodes,
    IReadOnlyList<ProgressionNodeInspection> AutomatableNodes,
    IReadOnlyList<ProgressionNodeInspection> BlockedNodes,
    IReadOnlyList<ProgressionNodeInspection> ManualNodes);

public interface IProgressionStateReader
{
    Task<ProgressionNodeInspection> InspectAsync(
        ProgressionNode node,
        CancellationToken cancellationToken = default);
}

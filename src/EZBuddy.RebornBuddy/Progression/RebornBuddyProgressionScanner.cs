using EZBuddy.Core.Progression;
using ff14bot;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Progression;

public interface IProgressionCompletionProbe
{
    string Key { get; }
    bool Supports(ProgressionNode node);
    Task<ProgressionNodeInspection?> InspectAsync(
        ProgressionNode node,
        CancellationToken cancellationToken = default);
}

public sealed class RebornBuddyProgressionScanner : IProgressionStateReader
{
    private readonly IReadOnlyList<IProgressionCompletionProbe> _probes;

    public RebornBuddyProgressionScanner(IEnumerable<IProgressionCompletionProbe>? probes = null)
    {
        _probes = (probes ?? Array.Empty<IProgressionCompletionProbe>()).ToArray();
    }

    public async Task<ProgressionNodeInspection> InspectAsync(
        ProgressionNode node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var probe in _probes.Where(p => p.Supports(node)))
        {
            var result = await probe.InspectAsync(node, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        try
        {
            return node.TargetType switch
            {
                ProgressionTargetType.Quest or
                ProgressionTargetType.AetherCurrentQuest or
                ProgressionTargetType.JobUnlock or
                ProgressionTargetType.RoleQuest
                    => InspectQuestBackedNode(node),

                ProgressionTargetType.AetherCurrentOverworld
                    => InspectOverworldAetherCurrent(node),

                ProgressionTargetType.JobMilestone or
                ProgressionTargetType.HuntingLog
                    => Unknown(node, "No native per-node RebornBuddy completion API is registered for this target; a completion probe is required."),

                ProgressionTargetType.ManualRequirement
                    => new ProgressionNodeInspection(node, ProgressionCompletionState.ManualRequired, node.Description.Length == 0 ? "Manual requirement." : node.Description, DateTimeOffset.UtcNow),

                _ => Unknown(node, "Unsupported progression target type.")
            };
        }
        catch (Exception exception)
        {
            return Unknown(node, $"Completion inspection failed: {exception.Message}");
        }
    }

    public async Task<CompletionReport> EvaluateAsync(
        IEnumerable<ProgressionNode> definitions,
        CancellationToken cancellationToken = default)
    {
        var inspections = new List<ProgressionNodeInspection>();
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inspections.Add(await InspectAsync(definition, cancellationToken).ConfigureAwait(false));
        }

        return new ProgressionChecklistEvaluator().BuildReport(inspections);
    }

    private static ProgressionNodeInspection InspectQuestBackedNode(ProgressionNode node)
    {
        if (node.SheetId == 0)
        {
            return Unknown(node, "Quest-backed node has no quest ID.");
        }

        if (QuestLogManager.IsQuestCompleted(node.SheetId))
        {
            return new ProgressionNodeInspection(node, ProgressionCompletionState.Completed, "Quest is completed.", DateTimeOffset.UtcNow);
        }

        if (QuestLogManager.HasQuest(node.SheetId))
        {
            return new ProgressionNodeInspection(node, ProgressionCompletionState.Active, "Quest is currently active.", DateTimeOffset.UtcNow);
        }

        return new ProgressionNodeInspection(node, ProgressionCompletionState.Missing, "Quest is not completed or active.", DateTimeOffset.UtcNow);
    }

    private static ProgressionNodeInspection InspectOverworldAetherCurrent(ProgressionNode node)
    {
        if (node.TerritoryId != 0 && WorldManager.ZoneId == node.TerritoryId && WorldManager.CanFly)
        {
            return new ProgressionNodeInspection(
                node,
                ProgressionCompletionState.Completed,
                "Flight is unlocked in this territory; the territory's required aether-current set is complete.",
                DateTimeOffset.UtcNow);
        }

        var zoneText = node.TerritoryId == 0
            ? "No territory ID is defined."
            : $"Current territory is {WorldManager.ZoneId}; target territory is {node.TerritoryId}.";

        return Unknown(
            node,
            $"Individual overworld-current completion cannot be proven from the native zone-level flight flag alone. {zoneText} Register an aether-current completion probe for exact per-current state.");
    }

    private static ProgressionNodeInspection Unknown(ProgressionNode node, string reason)
        => new(node, ProgressionCompletionState.Unknown, reason, DateTimeOffset.UtcNow);
}

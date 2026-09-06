namespace EZBuddy.Core.Progression;

public sealed class ProgressionChecklistEvaluator
{
    public CompletionReport BuildReport(IEnumerable<ProgressionNodeInspection> inspections)
    {
        var nodes = inspections?.ToArray() ?? throw new ArgumentNullException(nameof(inspections));

        return new CompletionReport(
            nodes.Where(x => x.State == ProgressionCompletionState.Completed).ToArray(),
            nodes.Where(x => x.State == ProgressionCompletionState.Active).ToArray(),
            nodes.Where(x => x.State is ProgressionCompletionState.Missing or ProgressionCompletionState.Blocked or ProgressionCompletionState.Unknown).ToArray(),
            nodes.Where(x => x.State == ProgressionCompletionState.ManualRequired).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public ProgressionChecklist BuildChecklist(IEnumerable<ProgressionNodeInspection> inspections)
    {
        var nodes = inspections?.ToArray() ?? throw new ArgumentNullException(nameof(inspections));
        var byId = nodes.ToDictionary(x => x.Node.Id, StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(
            nodes.Where(x => x.State == ProgressionCompletionState.Completed).Select(x => x.Node.Id),
            StringComparer.OrdinalIgnoreCase);

        var ordered = TopologicalOrder(nodes, byId);
        var automatable = new List<ProgressionNodeInspection>();
        var blocked = new List<ProgressionNodeInspection>();
        var manual = new List<ProgressionNodeInspection>();

        foreach (var inspection in ordered)
        {
            if (inspection.State == ProgressionCompletionState.Completed)
            {
                continue;
            }

            if (inspection.State == ProgressionCompletionState.ManualRequired ||
                inspection.Node.AutomationKind is ProgressionAutomationKind.Manual or ProgressionAutomationKind.None)
            {
                manual.Add(inspection);
                continue;
            }

            var missingPrerequisites = inspection.Node.Prerequisites
                .Where(id => !completed.Contains(id))
                .ToArray();

            if (missingPrerequisites.Length > 0)
            {
                blocked.Add(inspection with
                {
                    State = ProgressionCompletionState.Blocked,
                    Reason = $"Waiting on prerequisite nodes: {string.Join(", ", missingPrerequisites)}"
                });
                continue;
            }

            automatable.Add(inspection);
        }

        return new ProgressionChecklist(ordered, automatable, blocked, manual);
    }

    private static IReadOnlyList<ProgressionNodeInspection> TopologicalOrder(
        IReadOnlyList<ProgressionNodeInspection> nodes,
        IReadOnlyDictionary<string, ProgressionNodeInspection> byId)
    {
        var result = new List<ProgressionNodeInspection>(nodes.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            Visit(node, byId, visited, visiting, result);
        }

        return result;
    }

    private static void Visit(
        ProgressionNodeInspection node,
        IReadOnlyDictionary<string, ProgressionNodeInspection> byId,
        ISet<string> visited,
        ISet<string> visiting,
        ICollection<ProgressionNodeInspection> result)
    {
        if (visited.Contains(node.Node.Id))
        {
            return;
        }

        if (!visiting.Add(node.Node.Id))
        {
            throw new InvalidDataException($"Progression catalog contains a prerequisite cycle at '{node.Node.Id}'.");
        }

        foreach (var prerequisite in node.Node.Prerequisites)
        {
            if (byId.TryGetValue(prerequisite, out var prerequisiteNode))
            {
                Visit(prerequisiteNode, byId, visited, visiting, result);
            }
        }

        visiting.Remove(node.Node.Id);
        visited.Add(node.Node.Id);
        result.Add(node);
    }
}

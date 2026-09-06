namespace EZBuddy.Core.Progression;

public static class ProgressionCatalog
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ProgressionNode> Nodes = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ProgressionNode> All
    {
        get
        {
            lock (Sync)
            {
                return Nodes.Values
                    .OrderBy(node => node.TargetType)
                    .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public static IReadOnlyList<ProgressionNode> Quests => OfType(ProgressionTargetType.Quest);
    public static IReadOnlyList<ProgressionNode> AetherCurrents =>
        All.Where(node => node.TargetType is ProgressionTargetType.AetherCurrentOverworld or ProgressionTargetType.AetherCurrentQuest).ToArray();
    public static IReadOnlyList<ProgressionNode> JobMilestones =>
        All.Where(node => node.TargetType is ProgressionTargetType.JobUnlock or ProgressionTargetType.JobMilestone or ProgressionTargetType.RoleQuest).ToArray();
    public static IReadOnlyList<ProgressionNode> HuntingLogs => OfType(ProgressionTargetType.HuntingLog);

    public static void Register(IEnumerable<ProgressionNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        lock (Sync)
        {
            foreach (var node in nodes)
            {
                Validate(node);
                Nodes[node.Id] = node;
            }
        }
    }

    public static bool TryGet(string id, out ProgressionNode? node)
    {
        lock (Sync)
        {
            return Nodes.TryGetValue(id, out node);
        }
    }

    public static IReadOnlyList<ProgressionNode> ForTerritory(uint territoryId)
        => All.Where(node => node.TerritoryId == territoryId).ToArray();

    public static IReadOnlyList<ProgressionNode> ForTargetTypes(params ProgressionTargetType[] targetTypes)
    {
        var set = targetTypes.ToHashSet();
        return All.Where(node => set.Contains(node.TargetType)).ToArray();
    }

    private static IReadOnlyList<ProgressionNode> OfType(ProgressionTargetType type)
        => All.Where(node => node.TargetType == type).ToArray();

    private static void Validate(ProgressionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.Name);

        if (node.Id.Length > 160)
        {
            throw new InvalidDataException("Progression node ID exceeds 160 characters.");
        }

        if (node.Name.Length > 200)
        {
            throw new InvalidDataException("Progression node name exceeds 200 characters.");
        }

        if (node.AutomationKind == ProgressionAutomationKind.QuestPickup && (node.SheetId == 0 || node.NpcId == 0))
        {
            throw new InvalidDataException($"Quest pickup node '{node.Id}' requires quest and NPC IDs.");
        }
    }
}

namespace EZBuddy.Core.Goals;

public enum GoalType
{
    LevelJob,
    EarnGil,
    WeeklyChores,
    FarmCollectible,
    BuildGearSet,
    CleanInventory
}

public sealed record GoalRequest(
    GoalType Type,
    string Subject,
    long TargetValue = 0,
    IReadOnlySet<string>? EnabledCapabilities = null);

public sealed record GoalStep(
    string CapabilityKey,
    string Title,
    int Priority,
    bool Available,
    string Reason);

public sealed record GoalPlan(
    GoalRequest Goal,
    IReadOnlyList<GoalStep> Steps,
    IReadOnlyList<string> Blockers)
{
    public bool CanStart => Blockers.Count == 0 && Steps.Any(step => step.Available);
}

public static class GoalPlanner
{
    private static readonly IReadOnlyDictionary<GoalType, (string Key, string Title, int Priority)[]> Templates =
        new Dictionary<GoalType, (string, string, int)[]>
        {
            [GoalType.LevelJob] = [("maintenance", "Pre-run maintenance", 100), ("inventory-maintenance", "Inventory safety", 95), ("duty-support-leveling", "Duty Support / Trust leveling", 80)],
            [GoalType.EarnGil] = [("retainers", "Retainer ventures", 100), ("craft-gather", "Crafting and gathering opportunities", 80), ("marketboard", "Marketboard sales", 70)],
            [GoalType.WeeklyChores] = [("dailies", "Daily and weekly routine planner", 100), ("wondrous-tails", "Wondrous Tails", 85), ("custom-deliveries", "Custom Deliveries", 80), ("gold-saucer", "Gold Saucer routines", 50)],
            [GoalType.FarmCollectible] = [("collections", "Collection target planner", 100), ("duties", "Verified duty farming", 80), ("craft-gather", "Craft/gather acquisition", 60)],
            [GoalType.BuildGearSet] = [("smart-gear", "Owned gear analysis", 100), ("procurement", "Missing material procurement", 90), ("craft-gather", "Craft/gather missing items", 80), ("materia", "Materia optimization", 70)],
            [GoalType.CleanInventory] = [("smart-gear", "Protect useful gear", 100), ("inventory-maintenance", "Inventory maintenance", 90), ("gc-expert-delivery", "Approved GC turn-ins", 80), ("desynthesis", "Allowlisted desynthesis", 70), ("retainers", "Retainer storage", 50)]
        };

    public static GoalPlan Build(GoalRequest request, IReadOnlySet<string> availableCapabilities)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(availableCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);

        var enabled = request.EnabledCapabilities;
        var steps = Templates[request.Type]
            .Select(template =>
            {
                var allowed = enabled is null || enabled.Contains(template.Key);
                var available = allowed && availableCapabilities.Contains(template.Key);
                var reason = !allowed ? "Capability disabled for this goal." : available ? "Capability is available." : "Capability is not currently available.";
                return new GoalStep(template.Key, template.Title, template.Priority, available, reason);
            })
            .OrderByDescending(step => step.Priority)
            .ToArray();

        var blockers = new List<string>();
        if (!steps.Any(step => step.Available))
        {
            blockers.Add("No enabled capability can advance this goal in the current environment.");
        }

        return new GoalPlan(request, steps, blockers);
    }
}

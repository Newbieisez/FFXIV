namespace EZBuddy.Core.Goals;

public enum GoalType
{
    LevelJob,
    EarnGil,
    WeeklyChores,
    FarmCollectible,
    BuildGearSet,
    CleanInventory,
    DeepDungeonProgress,
    TreasureMaps,
    FieldOperationProgress,
    SharedFateRanks,
    IslandMaintenance,
    VoyageManagement,
    BurnLeveAllowances,
    DomanEnclaveDonations,
    HousingAndGardening
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
            [GoalType.EarnGil] = [("retainers", "Retainer ventures", 100), ("craft-gather", "Crafting and gathering opportunities", 80), ("marketboard", "Marketboard sales planning", 70)],
            [GoalType.WeeklyChores] = [("dailies", "Daily and weekly routine planner", 100), ("wondrous-tails", "Wondrous Tails", 85), ("custom-deliveries", "Custom Deliveries", 80), ("gold-saucer", "Gold Saucer routines", 50)],
            [GoalType.FarmCollectible] = [("collections", "Collection target planner", 100), ("duties", "Verified duty farming", 80), ("craft-gather", "Craft/gather acquisition", 60)],
            [GoalType.BuildGearSet] = [("smart-gear", "Owned gear analysis", 100), ("procurement", "Missing material procurement", 90), ("craft-gather", "Craft/gather missing items", 80), ("materia", "Materia optimization", 70)],
            [GoalType.CleanInventory] = [("smart-gear", "Protect useful gear", 100), ("inventory-maintenance", "Inventory maintenance", 90), ("gc-expert-delivery", "Approved GC turn-ins", 80), ("desynthesis", "Allowlisted desynthesis", 70), ("retainers", "Retainer storage", 50)],
            [GoalType.DeepDungeonProgress] = [("deep-dungeons", "Deep Dungeon floor-set planner", 100), ("smart-loot", "Deep Dungeon loot safety", 80), ("maintenance", "Pre-run maintenance", 70)],
            [GoalType.TreasureMaps] = [("treasure-hunts", "Treasure Hunt planner", 100), ("craft-gather", "Map acquisition", 70), ("duties", "Portal duty support", 60)],
            [GoalType.FieldOperationProgress] = [("field-operations", "Field Operation event planner", 100), ("maintenance", "Loadout readiness", 80), ("collections", "Field collection targets", 50)],
            [GoalType.SharedFateRanks] = [("shared-fates", "Shared FATE regional planner", 100), ("currency-cap", "Bicolor Gemstone cap protection", 90), ("collections", "Gemstone collection targets", 50)],
            [GoalType.IslandMaintenance] = [("island", "Island Sanctuary maintenance planner", 100), ("inventory-maintenance", "Island inventory safety", 80)],
            [GoalType.VoyageManagement] = [("voyages", "Airship/Submersible voyage planner", 100), ("procurement", "Fuel and repair-kit procurement", 80)],
            [GoalType.BurnLeveAllowances] = [("levequests", "Leve allowance planner", 100), ("craft-gather", "Craft/Gather leve execution", 80), ("procurement", "Leve material procurement", 60)],
            [GoalType.DomanEnclaveDonations] = [("doman-enclave", "Doman Enclave donation planner", 100), ("smart-gear", "Protect valuable inventory", 90)],
            [GoalType.HousingAndGardening] = [("housing-gardening", "Housing/Gardening planner", 100), ("procurement", "Seed/fertilizer procurement", 70)]
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
                var reason = !allowed ? "Capability disabled for this goal." : available ? "Planning capability is available." : "Capability is not currently available in this environment.";
                return new GoalStep(template.Key, template.Title, template.Priority, available, reason);
            })
            .OrderByDescending(step => step.Priority)
            .ToArray();

        var blockers = new List<string>();
        if (!steps.Any(step => step.Available))
            blockers.Add("No enabled planning capability can advance this goal in the current environment.");

        return new GoalPlan(request, steps, blockers);
    }
}

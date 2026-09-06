using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

public static class EZRoutineCatalog
{
    public static TimeOnly DailyResetUtc { get; } = new(15, 0);
    public static TimeOnly WeeklyResetUtc { get; } = new(8, 0);

    public static IReadOnlyList<RoutineDefinition> All { get; } =
    [
        new(
            "gc-expert-delivery",
            "Grand Company Expert Delivery",
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Daily, DailyResetUtc),
            RoutineExecutionKind.ExternalAdapter,
            Priority: 85,
            DestructiveActionsRequireExplicitOptIn: true,
            Description: "Turn in only explicitly allowlisted eligible gear, protect capped seals, then purchase configured GC supplies."),

        new(
            "retainer-venture-refill",
            "Venture Token Refill",
            ActivityCategory.Retainers,
            new RoutineResetRule(RoutineCadence.Daily, DailyResetUtc),
            RoutineExecutionKind.ExternalAdapter,
            Priority: 95,
            Description: "Maintain a configured Venture-token reserve using Grand Company seals and approved Expert Delivery items."),

        new(
            "mini-cactpot",
            "Mini Cactpot",
            ActivityCategory.GoldSaucer,
            new RoutineResetRule(RoutineCadence.Daily, DailyResetUtc),
            RoutineExecutionKind.Activity,
            Priority: 45,
            Description: "Complete available daily Mini Cactpot tickets using expected-value line selection."),

        new(
            "jumbo-cactpot",
            "Jumbo Cactpot",
            ActivityCategory.GoldSaucer,
            new RoutineResetRule(RoutineCadence.Weekly, WeeklyResetUtc, DayOfWeek.Tuesday),
            RoutineExecutionKind.Activity,
            Priority: 40,
            Description: "Track weekly ticket purchase and prize-claim state; do not repurchase completed entries."),

        new(
            "custom-deliveries",
            "Custom Deliveries",
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Weekly, WeeklyResetUtc, DayOfWeek.Tuesday),
            RoutineExecutionKind.LisbethOrders,
            Priority: 80,
            SupportsLisbeth: true,
            SupportsOrderBot: true,
            Description: "Evaluate remaining allowances, generate required collectable orders, then hand in supported deliveries."),

        new(
            "allied-society",
            "Allied Society Quests",
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Daily, DailyResetUtc),
            RoutineExecutionKind.OrderBotProfile,
            Priority: 65,
            RequiresMagitek: true,
            SupportsLisbeth: true,
            SupportsOrderBot: true,
            Description: "Select supported daily society quests and route crafting, gathering, or combat execution to the correct provider."),

        new(
            "wondrous-tails",
            "Wondrous Tails",
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Weekly, WeeklyResetUtc, DayOfWeek.Tuesday),
            RoutineExecutionKind.Activity,
            Priority: 75,
            RequiresMagitek: true,
            SupportsOrderBot: true,
            Description: "Track journal state, plan eligible duties and Second Chance usage, and coordinate pickup/turn-in."),

        new(
            "inventory-maintenance",
            "Inventory Maintenance",
            ActivityCategory.Utility,
            new RoutineResetRule(RoutineCadence.Daily, DailyResetUtc),
            RoutineExecutionKind.Activity,
            Priority: 90,
            DestructiveActionsRequireExplicitOptIn: true,
            Description: "Repair, materia extraction and opt-in desynthesis using explicit thresholds and protected-item rules."),

        new(
            "duty-support-leveling",
            "Duty Support Leveling",
            ActivityCategory.Duty,
            new RoutineResetRule(RoutineCadence.Once, TimeOnly.MinValue),
            RoutineExecutionKind.Activity,
            Priority: 60,
            RequiresMagitek: true,
            SupportsOrderBot: true,
            Description: "Loop an eligible level-appropriate Duty Support or Trust dungeon until configured level, currency or inventory goals are reached."),

        new(
            "island-sanctuary",
            "Island Sanctuary Maintenance",
            ActivityCategory.Sanctuary,
            new RoutineResetRule(RoutineCadence.Daily, DailyResetUtc),
            RoutineExecutionKind.Activity,
            Priority: 35,
            Description: "Track pasture, cropland, granary and workshop maintenance through patch-compatible providers."),

        new(
            "session-finish",
            "Queue Completion Shutdown",
            ActivityCategory.Utility,
            new RoutineResetRule(RoutineCadence.Once, TimeOnly.MinValue),
            RoutineExecutionKind.Activity,
            Priority: -100,
            Description: "When the user enables it, perform a safe game logout or configured local shutdown action after the queue is exhausted; never used for enforcement evasion.")
    ];

    public static RoutineDefinition? Find(string key)
        => All.FirstOrDefault(routine => string.Equals(routine.Key, key, StringComparison.OrdinalIgnoreCase));
}

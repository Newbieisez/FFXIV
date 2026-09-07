namespace EZBuddy.Core.Modules;

public enum EZModuleDomain
{
    Core,
    Progression,
    Instances,
    FieldOperations,
    Economy,
    CraftGather,
    Collection,
    Lifestyle,
    Housing,
    GoldSaucer,
    WeeklyDaily,
    Safety,
    Notifications
}

public enum ModuleMaturity
{
    Planned,
    Foundation,
    InDevelopment,
    Ready
}

public sealed record EZModuleDefinition(
    string Key,
    string DisplayName,
    EZModuleDomain Domain,
    string Description,
    ModuleMaturity Maturity,
    bool RequiresMagitek = false,
    bool SupportsLisbeth = false,
    bool SupportsOrderBot = false,
    bool DestructiveActionsRequireExplicitOptIn = false);

public static class ModuleCatalog
{
    public static IReadOnlyList<EZModuleDefinition> All { get; } =
    [
        new("dashboard", "Dashboard & Activity Queue", EZModuleDomain.Core, "Unified scheduling, queue orchestration, stop conditions, telemetry and diagnostics.", ModuleMaturity.InDevelopment),
        new("conflict-guard", "Diagnostics & Conflict Guard", EZModuleDomain.Safety, "Detect overlapping automation providers and pause unsafe ownership conflicts.", ModuleMaturity.InDevelopment),
        new("hooks", "Hooks & Integrations", EZModuleDomain.Core, "Allowlisted Magitek, Lisbeth, OrderBot and third-party adapter registry.", ModuleMaturity.InDevelopment),

        new("duties", "Duty & Dungeon Engine", EZModuleDomain.Instances, "Duty queueing, repeat loops, recovery, mechanics providers and farming stop conditions.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),
        new("duty-support", "Duty Support & Trust Leveling", EZModuleDomain.Instances, "Goal-driven Duty Support/Trust loops for leveling, currency and loot farming with Magitek combat ownership and queue stop conditions.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),
        new("deep-dungeons", "Deep Dungeons", EZModuleDomain.Instances, "Palace of the Dead, Heaven-on-High and Eureka Orthos floor-set planning, resource prioritization, trap/chest safety and party-mode fail-safes.", ModuleMaturity.Planned, RequiresMagitek: true),
        new("treasure-hunts", "Treasure Hunts", EZModuleDomain.Instances, "Map acquisition planning, decipher cooldown tracking, dig navigation, chest defense and portal-dungeon progression.", ModuleMaturity.Planned, RequiresMagitek: true, SupportsLisbeth: true),

        new("field-operations", "Field Operations", EZModuleDomain.FieldOperations, "Eureka, Bozja and Zadnor progression, event prioritization, loadout planning, mettle/elemental progression and collection goals.", ModuleMaturity.Planned, RequiresMagitek: true),
        new("shared-fates", "Shared FATE & Bicolor Gemstones", EZModuleDomain.FieldOperations, "Regional Shared FATE rank tracking, gemstone cap protection and configurable spend goals.", ModuleMaturity.Planned, RequiresMagitek: true),

        new("craft-gather", "Gathering & Crafting", EZModuleDomain.CraftGather, "Lisbeth-backed crafting, gathering, travel and material acquisition orchestration.", ModuleMaturity.Foundation, SupportsLisbeth: true),
        new("materia", "Materia Optimization", EZModuleDomain.CraftGather, "Melding and pentamelding plans with stat-cap priorities, importable stat targets and materia reserve floors.", ModuleMaturity.Planned, DestructiveActionsRequireExplicitOptIn: true),
        new("inventory-maintenance", "Inventory Maintenance", EZModuleDomain.Economy, "Guarded repair, materia extraction and explicitly allowlisted desynthesis using configurable thresholds and protected-item rules.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("desynthesis", "Mass Desynthesis", EZModuleDomain.Economy, "Rule-based desynthesis of explicitly approved overflow items with protected-item and value guardrails.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("levequests", "Levequest Auto-Burners", EZModuleDomain.WeeklyDaily, "Near-cap allowance management for configured DoH/DoL turn-in routines and repeatable crafting workflows.", ModuleMaturity.Planned, SupportsLisbeth: true, SupportsOrderBot: true),
        new("doman-enclave", "Doman Enclave Reconstruction", EZModuleDomain.WeeklyDaily, "Weekly donation-budget planning and turn-ins using explicitly approved item rules.", ModuleMaturity.Planned, DestructiveActionsRequireExplicitOptIn: true),

        new("retainers", "Retainers & Ventures", EZModuleDomain.Economy, "Venture collection/reassignment, venture plans, inventory/token safeguards, GC refill integration, local-bell preference, entrust, gil collection and shared bell coordination.", ModuleMaturity.InDevelopment, SupportsLisbeth: true, SupportsOrderBot: true, DestructiveActionsRequireExplicitOptIn: true),
        new("marketboard", "Marketboard", EZModuleDomain.Economy, "Rule-based listing orchestration with price floors, spend/sell protections and retainer coordination.", ModuleMaturity.Planned, DestructiveActionsRequireExplicitOptIn: true),
        new("voyages", "Airship & Submersible Voyages", EZModuleDomain.Economy, "FC workshop deployment routes, fuel/repair-kit inventory tracking and configurable voyage schedules.", ModuleMaturity.Planned, DestructiveActionsRequireExplicitOptIn: true),

        new("relics", "Relics & Long-Term Projects", EZModuleDomain.Progression, "Resume-aware relic and tool project orchestration across supported expansion chains.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsLisbeth: true, SupportsOrderBot: true),
        new("msq", "MSQ & Quest Progression", EZModuleDomain.Progression, "Resume-aware main-story, class/job, role and prerequisite quest orchestration.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),
        new("wondrous-tails", "Wondrous Tails", EZModuleDomain.WeeklyDaily, "Weekly book tracking, eligible duty planning, Second Chance budgeting and turn-in reminders/workflows.", ModuleMaturity.Planned, RequiresMagitek: true, SupportsOrderBot: true),

        new("triple-triad", "Triple Triad", EZModuleDomain.GoldSaucer, "Collection-aware NPC battles, rule solver, deck optimization, card routes, duty card farming and overlay telemetry.", ModuleMaturity.Foundation, RequiresMagitek: true),
        new("gold-saucer", "Gold Saucer & Arcade", EZModuleDomain.GoldSaucer, "MGP goals, exact expected-value Mini Cactpot solving, weekly Cactpot state planning, supported minigames, collection targets and spend caps.", ModuleMaturity.Foundation),

        new("island", "Island Sanctuary", EZModuleDomain.Lifestyle, "Gathering, workshop, pasture, crop and granary orchestration with inventory goals.", ModuleMaturity.Planned),
        new("housing-gardening", "Housing & Gardening", EZModuleDomain.Housing, "Lottery status/reminder workflows, refund retrieval, garden layouts, watering, fertilizing and crop lifecycle tracking.", ModuleMaturity.Planned, DestructiveActionsRequireExplicitOptIn: true),

        new("dailies", "Daily & Weekly Routines", EZModuleDomain.WeeklyDaily, "Reset-aware GC, tribe/allied society, custom-delivery, Wondrous Tails, Gold Saucer and other daily/weekly planning routed through activities, Lisbeth, OrderBot and adapters.", ModuleMaturity.InDevelopment, RequiresMagitek: true, SupportsLisbeth: true, SupportsOrderBot: true),

        new("social-safety", "Social Safety Monitor", EZModuleDomain.Safety, "Pause and alert on configured tells, invites, trades, unexpected relocation or other user-selected social events; records an audit trail and requires user review.", ModuleMaturity.Planned),
        new("session-safety", "Session Safety & Break Scheduler", EZModuleDomain.Safety, "User-configured session limits, scheduled breaks, stuck detection and safe stopping for health/reliability; not designed to evade game enforcement.", ModuleMaturity.Planned),
        new("notifications", "Webhook & Push Alerting", EZModuleDomain.Notifications, "Opt-in outbound notifications for configured drops, clears, stuck states, inventory halts, social alerts and engine failures with secret-safe Discord webhook support.", ModuleMaturity.Foundation)
    ];

    public static EZModuleDefinition? Find(string key)
        => All.FirstOrDefault(module => string.Equals(module.Key, key, StringComparison.OrdinalIgnoreCase));
}

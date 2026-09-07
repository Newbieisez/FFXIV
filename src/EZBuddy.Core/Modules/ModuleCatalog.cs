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
        new("product-intelligence", "Product Intelligence & Dry Run", EZModuleDomain.Core, "Live preflight, patch-compatibility warnings, dry-run simulation, product snapshots and goal planning without starting the queue.", ModuleMaturity.InDevelopment),
        new("runtime-resume", "Crash Resume Checkpoints", EZModuleDomain.Safety, "Per-character activity checkpoints that distinguish clean shutdown from interrupted sessions for recovery planning.", ModuleMaturity.InDevelopment),
        new("decision-replay", "Decision Replay / Black Box", EZModuleDomain.Safety, "Append-only decision and activity-state audit log for explaining what EZBuddy decided and why.", ModuleMaturity.InDevelopment),
        new("conflict-guard", "Diagnostics & Conflict Guard", EZModuleDomain.Safety, "Detect overlapping automation providers and pause unsafe ownership conflicts.", ModuleMaturity.InDevelopment),
        new("hooks", "Hooks & Integrations", EZModuleDomain.Core, "Allowlisted Magitek, Lisbeth, OrderBot and third-party adapter registry.", ModuleMaturity.InDevelopment),

        new("duties", "Duty & Dungeon Engine", EZModuleDomain.Instances, "Duty queueing, repeat loops, recovery, mechanics providers and farming stop conditions.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),
        new("duty-support", "Duty Support & Trust Leveling", EZModuleDomain.Instances, "Goal-driven Duty Support/Trust loops for leveling, currency and loot farming with Magitek combat ownership and queue stop conditions.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),
        new("smart-loot", "Smart Loot Brain", EZModuleDomain.Instances, "Inventory-aware loot recommendations that protect upgrades, missing collections and rare drops while passing low-value overflow safely.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("deep-dungeons", "Deep Dungeons", EZModuleDomain.Instances, "Floor-set planning with passage priority, Aetherpool targets, inventory-aware chest policy, revival and boss safety gates; live navigation remains provider-bound.", ModuleMaturity.Foundation, RequiresMagitek: true),
        new("treasure-hunts", "Treasure Hunts", EZModuleDomain.Instances, "Map-state planning for acquisition, Decipher cooldown, verified travel/dig targets, chest defense and guarded portal entry.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsLisbeth: true),

        new("field-operations", "Field Operations", EZModuleDomain.FieldOperations, "Eureka/Bozja/Zadnor event ranking with loadout validation, reward-per-minute scoring, party requirements and progress-loss protection.", ModuleMaturity.Foundation, RequiresMagitek: true),
        new("shared-fates", "Shared FATE & Bicolor Gemstones", EZModuleDomain.FieldOperations, "Regional Shared FATE target planning with reachable-event ranking and Bicolor Gemstone safe-cap protection.", ModuleMaturity.Foundation, RequiresMagitek: true),

        new("craft-gather", "Gathering & Crafting", EZModuleDomain.CraftGather, "Lisbeth-backed crafting, gathering, travel and material acquisition orchestration.", ModuleMaturity.Foundation, SupportsLisbeth: true),
        new("procurement", "Resource Procurement Planner", EZModuleDomain.CraftGather, "Recursive missing-material planning across owned inventory, recipes and approved craft/gather/vendor/currency sources.", ModuleMaturity.Foundation, SupportsLisbeth: true),
        new("materia", "Materia Optimization", EZModuleDomain.CraftGather, "Melding and pentamelding plans with stat-cap priorities, guaranteed/overmeld slot rules, inventory-aware reserves and importable stat targets.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("smart-gear", "Smart Gear Manager", EZModuleDomain.Economy, "Job-aware owned-gear scoring and safe recommendations across equip, protect, keep, retainer, GC, desynthesis and sell candidate states.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("currency-cap", "Currency Cap Manager", EZModuleDomain.Economy, "Projected currency-cap protection with explicitly approved spend rules, reserve floors and live GC Venture pressure relief.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("inventory-maintenance", "Inventory Maintenance", EZModuleDomain.Economy, "Guarded repair, materia extraction and explicitly allowlisted desynthesis using configurable thresholds and protected-item rules.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("desynthesis", "Mass Desynthesis", EZModuleDomain.Economy, "Rule-based desynthesis of explicitly approved overflow items with protected-item and value guardrails.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("levequests", "Levequest Auto-Burners", EZModuleDomain.WeeklyDaily, "Allowance-pressure planner with reserve floors, per-run caps and verified-executor gating for DoH/DoL/combat leve workflows.", ModuleMaturity.Foundation, SupportsLisbeth: true, SupportsOrderBot: true),
        new("doman-enclave", "Doman Enclave Reconstruction", EZModuleDomain.WeeklyDaily, "Weekly donation-budget planning using only explicitly approved items above configured reserve quantities.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),

        new("retainers", "Retainers & Ventures", EZModuleDomain.Economy, "Venture collection/reassignment, venture plans, inventory/token safeguards, GC refill integration, seal-pressure protection, local-bell preference, entrust, gil collection and shared bell coordination.", ModuleMaturity.InDevelopment, SupportsLisbeth: true, SupportsOrderBot: true, DestructiveActionsRequireExplicitOptIn: true),
        new("marketboard", "Marketboard", EZModuleDomain.Economy, "Explicit-sale-allowlist pricing and relist recommendations with price floors, fee-aware net floors, reserve quantities, undercut limits and maximum automatic price-drop guardrails.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),
        new("voyages", "Airship & Submersible Voyages", EZModuleDomain.Economy, "Vessel return-state, condition, repair-kit, fuel and verified-route deployment planning for FC workshop voyages.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),

        new("relics", "Relics & Long-Term Projects", EZModuleDomain.Progression, "Resume-aware relic and tool project orchestration across supported expansion chains.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsLisbeth: true, SupportsOrderBot: true),
        new("msq", "MSQ & Quest Progression", EZModuleDomain.Progression, "Resume-aware main-story, class/job, role and prerequisite quest orchestration.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),
        new("wondrous-tails", "Wondrous Tails", EZModuleDomain.WeeklyDaily, "Weekly book tracking, verified-duty planning, Second Chance budgeting and turn-in workflows.", ModuleMaturity.Foundation, RequiresMagitek: true, SupportsOrderBot: true),

        new("collections", "Collection Completion Engine", EZModuleDomain.Collection, "Rank missing mounts, minions, cards and other collectibles by priority, effort and availability of verified EZBuddy acquisition paths.", ModuleMaturity.Foundation),
        new("triple-triad", "Triple Triad", EZModuleDomain.GoldSaucer, "Collection-aware NPC battles, rule solver, deck optimization, card routes, duty card farming and overlay telemetry.", ModuleMaturity.Foundation, RequiresMagitek: true),
        new("gold-saucer", "Gold Saucer & Arcade", EZModuleDomain.GoldSaucer, "MGP goals, exact expected-value Mini Cactpot solving, weekly Cactpot state planning, supported minigames, collection targets and spend caps.", ModuleMaturity.Foundation),

        new("island", "Island Sanctuary", EZModuleDomain.Lifestyle, "Inventory-aware crop, pasture, granary and workshop maintenance planning with verified workshop-plan gating.", ModuleMaturity.Foundation),
        new("housing-gardening", "Housing & Gardening", EZModuleDomain.Housing, "Lottery review/refund planning plus watering, approved planting, fertilizing and explicitly approved harvest lifecycle management.", ModuleMaturity.Foundation, DestructiveActionsRequireExplicitOptIn: true),

        new("dailies", "Daily & Weekly Routines", EZModuleDomain.WeeklyDaily, "Reset-aware GC, tribe/allied society, custom-delivery, Wondrous Tails, Gold Saucer and other daily/weekly planning routed through activities, Lisbeth, OrderBot and adapters.", ModuleMaturity.InDevelopment, RequiresMagitek: true, SupportsLisbeth: true, SupportsOrderBot: true),

        new("social-safety", "Social Safety Monitor", EZModuleDomain.Safety, "Pause and alert on configured tells, invites, trades, unexpected relocation or other user-selected social events; records an audit trail and requires user review.", ModuleMaturity.InDevelopment),
        new("session-safety", "Session Safety & Break Scheduler", EZModuleDomain.Safety, "User-configured session limits, scheduled breaks, stuck detection and safe stopping for health/reliability; not designed to evade game enforcement.", ModuleMaturity.InDevelopment),
        new("notifications", "Webhook & Push Alerting", EZModuleDomain.Notifications, "Opt-in outbound notifications for configured drops, clears, stuck states, inventory halts, social alerts and engine failures with secret-safe Discord webhook support.", ModuleMaturity.InDevelopment)
    ];

    public static EZModuleDefinition? Find(string key)
        => All.FirstOrDefault(module => string.Equals(module.Key, key, StringComparison.OrdinalIgnoreCase));
}

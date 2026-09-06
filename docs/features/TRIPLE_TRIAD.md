# EZBuddy Triple Triad

EZBuddy Triple Triad is a native feature pack inside the EZBuddy activity engine. It is an original implementation designed to cover and extend the workflow currently spread across dedicated Triple Triad tools.

## Product goal

Turn Triple Triad collection completion into a goal-driven activity: select a target card, opponent, route, or collection goal and let EZBuddy plan the safe supported steps required to make progress.

## Feature scope

### Collection-aware planner
- Read the active character's learned Triple Triad collection.
- Identify missing cards.
- Search/filter by card name, card ID, source, route type, acquisition type, and automation support.
- Multi-select missing cards and generate a combined acquisition route.
- Mark unsupported acquisitions as guide-only rather than pretending they are automated.
- Track collection completion and session gains.

### Automated NPC battles
- Target a supported NPC and play once.
- Farm a selected opponent for a configurable match count.
- Farm until all catalogued drops from that opponent are learned.
- Sweep unbeaten supported NPCs.
- Sweep all missing supported NPC-drop cards.
- Travel to supported opponents through the normal EZBuddy travel provider / Lisbeth adapter.

### Smart rule-aware play
- Board solver evaluates legal card placements for the active Triple Triad rule set.
- Opponent-specific weighting can account for Plus, Same, Reverse, Fallen Ace, Ascension, Descension, Order, Chaos, Swap and other supported rules.
- Solver is deterministic when inputs are identical and can expose its selected move/reason in diagnostics.
- No input injection occurs outside the game interaction APIs used by the RebornBuddy host layer.

### Deck Lab
- Build the strongest general-purpose deck from cards actually owned by the active character.
- Optimize a deck for a specific opponent and active rule set.
- Store opponent-specific recommendations locally.
- Optional community recommendation provider can be added behind an explicit privacy opt-in.
- Never select cards the current character has not learned.

### Duty card farming
- Queue and run supported card-dropping duties.
- Use the EZBuddy Duty Engine for queue/repeat/recovery logic.
- Require Magitek for combat; EZBuddy does not ship an internal combat routine.
- Support stop-after-run-count, stop-on-card-obtained, stop-on-inventory/full, time-limit and manual-stop conditions.

### Unlock quests and routes
- Detect known prerequisite quests and unlock state.
- Add prerequisite quest/profile steps to an activity plan when supported.
- Clearly mark manual prerequisites.
- Resume the card route after prerequisite completion.

### Gold Saucer utilities
- Duplicate-card exchange workflow where supported.
- Calculate MGP required for selected missing cards/packs.
- Integrate with the Gold Saucer/Arcade provider to farm toward an MGP target.
- Purchase supported cards and packs only under an explicit user-selected goal and configured spend limit.

### Tournament / Battlehall assist
- Match-assist mode that activates when an eligible board opens.
- Weekly Battlehall practice workflow.
- Tournament-aware solver mode where supported.
- Tournament registration remains user-controlled unless a future provider can validate it safely.

### Card catalog updates
- Versioned local catalog.
- Catalog schema includes card ID, localized names, rarity, acquisition sources, NPC drops, duty sources, purchase cost, packs, prerequisite quests and automation support state.
- Update checks are signed/version-aware before replacing catalog data.
- Preserve a last-known-good catalog if an update fails validation.

### Overlay and telemetry
- Compact always-on-top overlay option.
- Current objective.
- Current opponent/duty.
- Current target card(s).
- Matches played / wins / losses / draws.
- Session win rate.
- Cards learned this session.
- Route progress.
- Gentle Stop button.

### Gentle stopping
Gentle Stop must stop only at a safe activity boundary:
- after the current NPC match,
- after the current duty,
- after the current purchase transaction,
- or before beginning the next route step.

Emergency Stop remains separate and immediately cancels the EZBuddy activity engine.

## Architecture

Triple Triad is split into clean services so the solver, catalog, routing and UI remain independently testable:

```text
EZBuddy.TripleTriad
├── Catalog
│   ├── ICardCatalogProvider
│   └── CardCatalogService
├── Collection
│   └── ICollectionReader
├── Solver
│   ├── ITripleTriadSolver
│   ├── IDeckOptimizer
│   └── RuleEngine
├── Planning
│   ├── ICardRoutePlanner
│   └── AcquisitionPlan
├── Activities
│   ├── NpcMatchActivity
│   ├── NpcFarmActivity
│   ├── DutyCardFarmActivity
│   ├── MgpPurchaseActivity
│   └── CollectionSweepActivity
└── Telemetry
    └── TripleTriadSession
```

The Core contracts contain no direct reference to optional third-party assemblies. Navigation/crafting/duty/combat dependencies are supplied through the EZBuddy adapter registry.

## Security / privacy requirements
- No account credentials or RebornBuddy license data stored by this module.
- No character/account identity uploaded by default.
- Any community match-result sharing is opt-in and must strip character/account identifiers.
- Remote catalog updates require HTTPS plus integrity/version validation before activation.
- Purchase automation has explicit MGP spend caps.
- Reflection is not used as arbitrary method execution; optional adapters expose allowlisted operations only.
- All external data is treated as untrusted input and validated before use.

## Parity target

The module targets the documented workflow of modern Triple Triad automation tools while improving integration through EZBuddy's shared Activity Queue, Conflict Guard, diagnostics, stop conditions, security model, unified UI, and Magitek/Duty Engine integration.

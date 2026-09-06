# EZBuddy — RebornBuddy All-in-One for FFXIV

EZBuddy is a modular RebornBuddy plugin/control center for Final Fantasy XIV. The goal is one place to manage the repetitive utility work that is currently spread across multiple RebornBuddy plugins and helper projects.

## Design goals

- One plugin, one settings experience, one log prefix.
- Modular: every capability can be enabled/disabled independently.
- Conservative automation: never discard/sell/move valuable items without an explicit rule.
- Patch-resilient: prefer RebornBuddy/LlamaLibrary public APIs over brittle hard-coded memory offsets.
- Conflict-aware: detect overlapping plugins and warn rather than allowing two automations to fight each other.
- Observable: each module exposes status, last action, errors, and cooldown state.
- Maintainable: current .NET 10 / RebornBuddy reference-assembly build pipeline.

## Initial module set

| Module | Purpose | Status |
|---|---|---|
| Core Orchestrator | Shared lifecycle, throttling, module isolation, diagnostics | Implemented |
| Food | Maintain Well Fed using a configured item ID | Implemented |
| Repair | Monitor durability and invoke supported self-repair logic | Implemented |
| Death Recovery | Detect death and coordinate safe recovery hooks | Implemented foundation |
| Inventory Guard | Capacity warnings + protected-item framework | Implemented |
| Companion | Chocobo/companion management surface | Foundation |
| Retainers | Venture-management surface | Foundation |
| Duty Assistant | Duty-session state and integration surface for encounter logic | Foundation |
| Conflict Guard | Detect overlapping RebornBuddy plugins | Implemented |
| Dashboard | WinForms control center for modules/settings/status | Implemented |

The architecture intentionally separates the core plugin from content-specific automation such as individual duty mechanics, relic stages, tribe quests, and long profile chains. Those are added as feature packs/modules instead of turning the main plugin class into an unmaintainable monolith.

## Requirements

- RebornBuddy x64 with an active license.
- .NET 10 SDK to build from source.
- Current `__LlamaLibrary` runtime installed under `RebornBuddy/QuestBehaviors/__LlamaLibrary`.

The project compiles against the official `RebornBuddy.ReferenceAssemblies` NuGet package and the LlamaLibrary developer package. RebornBuddy's reference-assembly package is compile-time only; the host supplies the runtime assemblies.

## Install from a build

1. Build `src/EZBuddy/EZBuddy.csproj` in `Release | x64`.
2. Create `RebornBuddy/Plugins/EZBuddy/`.
3. Copy the build output into that folder.
4. Start RebornBuddy.
5. Open **Plugins**, enable **EZBuddy**, select it, then press **Settings**.

If `RebornbuddyDir` is passed as an MSBuild property, the project can deploy the build output directly into the correct plugin folder.

## Build

```powershell
dotnet restore src/EZBuddy/EZBuddy.csproj
dotnet build src/EZBuddy/EZBuddy.csproj -c Release -p:Platform=x64
```

## Configuration

EZBuddy stores settings using RebornBuddy's settings system. Destructive inventory actions are disabled by design until a future rule explicitly identifies the item and operation.

Recommended first setup:

- Enable Food and set your preferred food item ID.
- Enable Repair and choose a durability threshold.
- Leave experimental modules disabled until their status panel reports `Ready` for your setup.
- Do not run separate plugins that duplicate an enabled EZBuddy module.

## Roadmap

The next feature packs are organized around the major gaps in the current ecosystem:

1. Companion summon/stance/feed automation.
2. Retainer venture collection/reassignment.
3. Vendor/self-repair routing.
4. Duty Support mechanic engine and reusable encounter definitions.
5. Daily/weekly task planner (tribes, hunts, GC, custom deliveries, roulettes).
6. Relic/project workflows that can resume from detected progress.
7. Profile launcher and activity queue.
8. Optional web/API status surface.

## Important

EZBuddy is an independent community project and is not affiliated with Square Enix, Final Fantasy XIV, or the RebornBuddy developers. Automated gameplay can violate game rules and can carry account risk. Use at your own discretion.

No paid or proprietary plugin code is included or copied. EZBuddy's implementations are original and use public RebornBuddy/LlamaLibrary interfaces and documented/open community patterns.

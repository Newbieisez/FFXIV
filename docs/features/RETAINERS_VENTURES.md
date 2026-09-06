# EZBuddy Retainers & Ventures

This feature pack is a native EZBuddy implementation for retainer venture automation, venture planning, entrust, gil collection, and shared summoning-bell coordination.

## Goals

- One Retainers workspace for ventures, entrust, gil collection, inventory safety and plan management.
- Safe execution from the EZBuddy BotBase, BotPlugin hooks, OrderBot/Profile hooks, and Lisbeth hooks.
- No competing bell trips between EZBuddy modules or compatible third-party adapters.
- Every automated action is written to the EZBuddy Journal with timestamps, retainer, action, outcome and stop reason.

## Per-retainer venture behavior

Each retainer has an independent configured return behavior:

1. **Repeat Previous Venture** — resend the venture that just completed.
2. **Quick Exploration** — send the retainer on Quick Exploration when eligible.
3. **Venture Plan** — execute the next eligible entry from the selected named plan.
4. **Collect Only** — collect the completed venture and stop without sending another.

At the summoning bell EZBuddy may also, when explicitly enabled:

- entrust duplicate stacks to that retainer,
- collect gil held by that retainer,
- record inventory deltas and venture rewards,
- stop before starting another retainer when a safety gate fails.

## Safety gates

Retainer automation will not start unless all configured gates pass:

- minimum free player inventory slots,
- minimum venture-token stock,
- no critical Conflict Guard finding for the bell/session,
- game/client state is safe for retainer interaction,
- no emergency/gentle stop is pending.

The task stops cleanly when venture stock falls below the configured minimum or inventory free space falls below the configured threshold.

## Shared Summoning Bell Session

All EZBuddy systems that use a summoning bell coordinate through one `IRetainerBellCoordinator` lease. A hook-triggered venture run can share a single bell session with Retainer Entrust, LlamaMarket-compatible adapters, inventory cleanup, or other approved retainer tasks.

Rules:

- only one bell owner at a time,
- compatible retainer tasks can enqueue work into the same active session,
- direct user-initiated runs may request an end-to-end dedicated session,
- timeouts and cancellation always release the lease,
- third-party adapters cannot bypass the coordinator.

## Venture Plans

A venture plan is a named ordered list of venture entries tied to a retainer job/class.

Each entry contains:

- venture ID,
- optional quantity/iteration target,
- optional condition type,
- optional item ID and threshold,
- enabled state,
- notes/label.

Supported conditions include:

- always eligible,
- player holds fewer than N of the venture item,
- retainer holds fewer than N of the venture item,
- combined player + retainer holdings are fewer than N,
- custom safe condition supplied by an allowlisted provider.

Plan completion behavior:

- loop from start,
- switch to Quick Exploration,
- collect only / stop,
- wait until an entry becomes eligible.

Plans are designed to be shared across characters but validated against the selected retainer's job, level and gear before execution.

## Venture Catalogue

The catalogue model includes:

- venture ID and localized name,
- retainer job/type,
- minimum level,
- venture-token cost,
- required gathering/combat stats where applicable,
- duration,
- expected item ID,
- expected quantity range.

The UI recalculates expected yield for the currently selected retainer's level and gear and estimates one complete plan-pass duration.

## Import / Export / Duplicate

Venture plans support:

- duplicate,
- export to versioned JSON,
- import from versioned JSON,
- schema validation,
- unknown-field tolerance for forward compatibility,
- safe rejection of malformed or unsupported plan data.

Import never executes code or arbitrary expressions.

## Entrust

Retainer Entrust is part of the Retainers module, not the generic Inventory page.

- each entrust row exposes an always-visible retainer selector,
- duplicate-stack entrust is supported,
- existing profile compatibility can be provided through a stable EZBuddy profile tag,
- adapter aliases may temporarily support older integration names with deprecation warnings,
- destructive movement rules are opt-in and validated.

## Execution surfaces

Retainer tasks may be initiated from:

- Retainers page,
- EZBuddy Activity Queue,
- EZBuddy BotBase,
- EZBuddy background plugin hook,
- OrderBot/profile tag,
- Lisbeth hook,
- approved adapter API.

All execution paths use the same Core engine and safety gates.

## Journal

Journal events include:

- bell session start/end,
- retainer opened,
- venture collected,
- reward summary,
- venture assigned,
- plan entry advanced/skipped,
- duplicate items entrusted,
- gil collected,
- safety gate prevented execution,
- venture-token floor reached,
- inventory floor reached,
- hook source,
- exception/recovery result.

## UI requirements inherited from observed usability issues

- Checkboxes inside settings grids must toggle on the **first click**; no row-selection click is required first.
- Retainer selectors remain visible as normal dropdowns.
- Nested list/grid mouse-wheel handling must bubble to the parent scroll viewer when the inner control reaches its scroll boundary.
- Stopping the bot or swapping the active profile/behavior is treated as a normal lifecycle event, not as a task failure.
- Session failure counters increment only for genuine execution failures, never expected stop/unhook events.

## Security and reliability

- No arbitrary reflection invocation.
- No credentials or account secrets stored in plan files.
- Plan import uses a strict data schema.
- Bell lease has timeout/cancellation protection.
- All retainer operations are serialized through the coordinator.
- Entrust and gil actions are explicit user settings.
- Inventory operations never discard items as part of venture handling.
- Every module exception is isolated and journaled.

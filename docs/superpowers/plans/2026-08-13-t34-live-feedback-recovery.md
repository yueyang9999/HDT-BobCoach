# T34 Live Feedback Recovery Plan

Date: 2026-08-13

Branch: `codex/season14-algorithm-dark-gift-capture`

## Objective

Fix the reproduced Faelin and demon-path feedback without changing the legacy decision contract beyond the visible bug fixes.

## Reproduced Inputs

- Replay:
  `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260813_143438_BG22_HERO_201_3d476ea3.jsonl`
- Hearthstone log directory:
  `D:\software\game\Hearthstone\Logs\Hearthstone_2026_08_13_14_32_59`
- Plugin log:
  `E:\claude_project_hdt\local-data\bob-coach\user-data\bob_coach.log`

## Root Causes

1. Faelin passive choices reached `PassiveChoiceActive` but never entered the authoritative discover batch/panel used by the renderer.
2. Mainline direction used ordinary tribe counts as a higher-priority signal than catalog core cards, so two Quilboar minions could override a Demon core.
3. A full board could still recommend leveling instead of a protected sell-to-buy transition when the established tribe had a matching shop minion.
4. Hand tavern spells inherited shop purchase cost and were marked frozen because they had no attack/health. `BG36_880` therefore was never enumerated as a zero-cost `UseSpell` action.
5. `BG36_880` originally used an unconditional early return. It could override core purchases, triples, leveling, or preserving a valuable shop.

## Implementation

1. Route Faelin passive batches into `_discoverBatchFromLog` and clear them by matching choice id.
2. Rename and retain separate `CoreCount` and `CardCount` tribe signals in `MainlineStrategyPlanner`.
3. Extend the matching-tribe helper to handle full boards only when a protected sell target and an affordable, stronger shop replacement exist.
4. Normalize hand spell cost to zero and remove the attack/health-based spell-freeze rule.
5. Enumerate `UseSpell` at zero cost and add an explicit friendly-demon target for `BG36_880`.
6. Move `BG36_880` from an unconditional early return to a safe end-of-turn fallback with shop-count, core/triple, affordable-buy, leveling, and freeze-preservation gates.

## Acceptance Gates

- T34 live feedback contract passes.
- Full `npm test` passes.
- Release build passes.
- Repository validation passes.
- `git diff --check` passes.
- All five local HDT plugin directories contain the same release SHA256.

## Deployment

- DLL bytes: `997888`
- SHA256: `67B155A30D1F459215820DC60E99E189A477DEC291017E1B233A5D877EA32871`
- Archive: `E:\HDT-BobCoach-evidence\dll-archive\20260813T074258417Z-e8ea5fa4`

This plan does not claim win-rate, placement, or strategy-quality improvement.

# Active Trinket Rule Expansion Phase 3 Implementation Plan

> **Execution requirement:** Execute this plan directly after writing it. Follow test-driven development and verification-before-completion. Push is authorized only after all required validation passes; do not create a PR or publish a Release.

**Goal:** Add the deterministic exact-ID `Bartend-o-Tron's Oilcan` equipped-trinket rule so upgrade legality, cost, simulation, and phase scoring use the same local effective rule while keeping quote recommendations hidden and independent.

**Architecture:** Resolve `BG30_MagicItem_705` through the existing source-independent `TrinketEffectRegistry` into immutable `ActiveTrinketContext`. Carry an upgrade-cost delta through `EffectiveGameRules`, and centralize cost resolution in `GameRuleEvaluator.GetUpgradeCost`. Preserve an observed HDT button cost as authoritative to avoid applying the same discount twice; apply the local fallback discount only when the live cost is unavailable. No Firestone endpoint, cache, retry, offer recommendation, UI visibility, or user-data path changes.

**Tech stack:** C# 7-compatible HDT plugin code, PowerShell Roslyn harnesses, Node.js repository tests, existing Release build and repository validation scripts.

**Global Constraints:**

- Keep all existing trinket recognition, local rules, recommendation services, and UI code.
- `TrinketRecommendationsVisible = false` controls rendering only; equipped effects remain active.
- Unknown and incorrectly-cased CardIds fail closed and remain diagnostic-only.
- Tests use synthetic game state and never Firestone or historical cache data.
- Do not delete files, read/migrate/delete user caches, alter `.env`/secrets/CI, run full VM acceptance, or use `-RemoveUserData`.
- Use `apply_patch` for edits and preserve unrelated worktree changes.

## Task 1: Add failing Oilcan rule tests

**Modify:** `tests/ActiveTrinketEffectsHarness.cs`

- Add `BartendOTronOilcanId` and expected ruleset version `hdt-1.53.5-hearthdb-2026-07-22-r3`.
- Assert exact, case-sensitive resolution, stable deduplication, and unknown-ID isolation for Oilcan.
- Add synthetic tests for fallback upgrade cost reduction by 3 with a floor of 0, authoritative observed HDT cost with no double-discount, `ActionEnumerator` upgrade legality at the discounted cost, and `Simulator` gold deduction/tier advancement.
- Add a phase-engine assertion that upgrade urgency reads the same effective cost path.
- Run `tests/test_active_trinket_effects.ps1` and capture the expected RED before implementation.

## Task 2: Implement Oilcan through the effective rules pipeline

**Modify:**

- `src/BobCoach/Core/TrinketEffectRegistry.cs`
- `src/BobCoach/Core/ActiveTrinketContext.cs`
- `src/BobCoach/Core/EffectiveGameRules.cs`
- `src/BobCoach/Core/GameRuleEvaluator.cs`
- `src/BobCoach/Core/TurnPhaseEngine.cs`

- Add the exact Oilcan CardId and immutable context flag.
- Expose an internal upgrade-cost delta from the context and carry it in `EffectiveGameRules` without changing unrelated rule construction behavior.
- Make `GameRuleEvaluator.GetUpgradeCost` honor an observed nonnegative HDT button cost as already effective; otherwise apply the local delta to the deterministic fallback and clamp at zero.
- Make `TurnPhaseEngine` use `GameRuleEvaluator.GetUpgradeCost` with the state context so urgency matches enumeration and simulation.
- Re-run the focused harness and require GREEN.

## Task 3: Update source and architecture documentation

**Modify:** `README.md`, `DATA_SOURCES.md`, `PRIVACY.md`, `NOTICE`, `docs/maintainer/ARCHITECTURE.md`, `docs/maintainer/DEPENDENCIES.md`

- Update the local ruleset version from `r2` to `r3` and record 16 exact CardIds, including Oilcan's repeated 3-coin tavern-upgrade discount.
- State that observed HDT upgrade cost is not discounted twice and unavailable cost uses a local fail-closed fallback.
- Preserve the hidden quote recommendation boundary, synthetic-test statement, unknown-effect diagnostics, and no historical-cache access contract.

## Task 4: Run release validation and produce a candidate package

- Run `npm test`, `tests/test_active_trinket_effects.ps1`, Release build, `tools/build/validate_repository.ps1`, and `git diff --check`.
- Audit package allowlist, Firestone/Zero to Heroes endpoint and production call graph, secrets, personal paths, replay data, large files, and `.github` diff.
- Build a new candidate package outside the repository and record its SHA-256 and size. Do not run full VM lifecycle validation or touch user data.

## Task 5: Review and push

- Inspect final status and diff, confirm only scoped source/tests/plan/documentation changes are present, and review residual exact-environment smoke risk.
- Commit the validated change and push `codex/add-bilingual-readme` without force. Do not create a PR or publish a Release.
- Report worktree state, exact validation results, candidate package checksum, and remaining Windows 11 24H2 + HDT 1.53.5 smoke gap.

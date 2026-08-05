# Season14 Mechanic Profile and Test Env Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align the BobCoach Season14 test environment with the real Season14 mechanic set by introducing an external mechanic profile. The profile activates `activate`, `lockbox`, `fishbait`, `darkGift`, and `trinket`, and deactivates `anomaly`. All mechanism gates must fail closed when the profile is absent, invalid, inconsistent, or when the queried mechanism is unknown.

**Architecture:** Add `SeasonMechanicProfile` under `src/BobCoach/Core`, load it through `SeasonDataSelector.ResolveConfiguredRoot()`, expose it through `Season14RulesEvaluator`, and gate `TrinketChoiceBatchLifecycle` plus `GameStateExtractor` state extraction by the profile. Evidence remains under `E:\HDT-BobCoach-evidence\season14\generated\`.

**Tech Stack:** C# 7.3/.NET Framework 4.7.2 harness compiled with Roslyn; PowerShell test runners; Node.js validation tools.

## Global Constraints

- Continue on `codex/season14-algorithm-iteration`; no sub-branch is needed for this round.
- Do not push, rebase, `reset --hard`, force push, delete files, or alter git history.
- Do not modify `.env`, keys, CI/CD, database schema, or install global dependencies.
- Do not read/write `%APPDATA%\bob-coach\data\`; do not add `BGDUO`.
- Do not mark `unresolvedOfficialRows=10` resolved.
- Do not claim win rate, placement, or strategy quality improvements; real effect evaluation stays `blocked-by-season-data`.
- All PASS claims require fresh command output in this session.

## Task 1: Plan Record

- [x] Create this plan and commit it as a docs-only commit.

## Task 2: Add SeasonMechanicProfile with RED/GREEN Harness Coverage

**Files:**
- Modify: `E:\HDT-BobCoach\tests\Season14RegistriesHarness.cs`
- Add: `E:\HDT-BobCoach\src\BobCoach\Core\SeasonMechanicProfile.cs`

- [x] Add harness tests for profile success and fail-closed cases before implementing the class.
- [x] Run `tests\test_season14_registries.ps1` and confirm RED because `SeasonMechanicProfile` is missing.
- [x] Implement the profile loader and make the harness pass.

Fail-closed profile rules:
- Missing configured root, missing file, invalid JSON, missing `_meta`, unsupported schema version, selector identity mismatch, unknown mechanism, unknown status, incomplete mechanism set, and `BGDUO` all leave `IsLoaded=false`.
- `IsMechanicEnabled(mechanic)` returns `false` when unloaded or when the mechanism is unknown/inactive.

## Task 3: Wire Profile Into Season14RulesEvaluator

**Files:**
- Modify: `E:\HDT-BobCoach\src\BobCoach\Core\Season14RulesEvaluator.cs`

- [x] Add `IsMechanicEnabled(string mechanic)` and require the profile in `IsLoaded`.
- [x] Keep the existing four-registry `LoadFromRegistries` API for harness compatibility.
- [x] Load the profile in `LoadFromConfiguredRoot`.
- [x] Extend harness real/empty evaluator checks and verify GREEN.

## Task 4: Create External Profile and Register It

**Files (outside git):**
- Create: `E:\HDT-BobCoach-evidence\season14\generated\registries\season14_mechanic_profile.json`
- Modify: `E:\HDT-BobCoach-evidence\season_selector.json`

- [x] Add the profile to `season14.files`.
- [x] Confirm the real configured-root harness still loads all Season14 registries and the evaluator reports the expected active/inactive set.

## Task 5: Gate Trinket Lifecycle

**Files:**
- Modify: `E:\HDT-BobCoach\src\BobCoach\Core\TrinketChoiceBatchLifecycle.cs`
- Modify: `E:\HDT-BobCoach\src\BobCoach\BobCoachPlugin.cs` only if needed to handle an inactive trinket safely

- [x] Keep legacy fallback when evaluator is null or not loaded.
- [x] When the loaded profile says trinket is inactive, do not create a trinket lifecycle binding.
- [x] Add harness regression and verify GREEN.

## Task 6: Gate Anomaly and Trinket State Extraction

**Files:**
- Modify: `E:\HDT-BobCoach\src\BobCoach\GameStateExtractor.cs`

- [x] When the loaded profile says anomaly is inactive, set `state.AnomalyContext = AnomalyContext.Empty` and do not activate anomaly rules.
- [x] When the loaded profile says trinket is inactive, skip active trinket extraction and trinket offer extraction without mutating persistent trinket state.

## Task 7: Update Compile and Release Contracts

**Files:**
- Modify: `E:\HDT-BobCoach\src\BobCoach\BobCoach.csproj`
- Modify: `E:\HDT-BobCoach\tests\test_season14_registries.ps1`
- Modify: `E:\HDT-BobCoach\tests\test_release_build_contract.js`

- [x] Add `Core\SeasonMechanicProfile.cs` to the project and harness source list.
- [x] Update compile item count and SHA-256 digest in the release build contract.
- [x] Run the affected contract tests and verify GREEN.

## Task 8: Full Verification

**Commands:**

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker'
node "E:\HDT-BobCoach-evidence\season14\tools\validate_season14_guides.js"
node "E:\HDT-BobCoach-evidence\season14\tools\validate_season14_registries.js"
node "E:\HDT-BobCoach-evidence\season14\tools\validate_season14_rule_catalog.js"
node tests\test_season14_guide_fetch.js
node tests\test_season14_guide_sync.js
node tests\test_season14_guide_weights.js
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\build\validate_repository.ps1
git diff --check
```

Because state extraction and decision paths are touched, also run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\model\run_policy_benchmark.ps1 -SelfTest -OutputDirectory E:\HDT-BobCoach-evidence\season14\generated\benchmark-self-test-post-t7-2026-08-06
node tests\test_algorithm_evaluation.js
node tools\model\evaluate_policy.js
```

## Task 9: State File and Handover

- [x] Update `E:\HDT-BobCoach-evidence\season14\generated\t_session_state.md` after each commit and at completion.
- [x] Create `G:\software\Codex\Handover document\BobCoach_Season14_单人模式_T7_机制Profile_2026-08-06.md`.
- [x] Commit repository docs/plan changes only.

## Self-Review

- The actual test environment profile says `anomaly=inactive` and `trinket=active`.
- Unknown or missing profile data fails closed instead of falling back to guessed Season14 behavior.
- The legacy unloaded-evaluator behavior remains unchanged.
- Policy benchmark baseline and post-T7 outputs are compared without claiming real effect metrics.

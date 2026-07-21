# Firestone Trinket Data Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the public Bob Coach runtime from requesting, caching, or using Firestone trinket statistics while preserving source-independent validation, keeping trinket quote recommendations default-hidden, and independently applying deterministic local effects from equipped trinkets to later decisions.

**Architecture:** Disconnect the production plugin from the external statistics coordinator and reduce that coordinator to an inert, source-authorization boundary with no I/O. Keep the generic models, verifier, HTTPS fetcher, and store available as isolated infrastructure, make verification source-agnostic, and exercise it only with synthetic data. Gate only the final quote-recommendation UI dispatch. Resolve `ActiveTrinkets` through a versioned local registry into an immutable context, merge hard effects into effective rules before action enumeration, and pass deterministic synergy/combat effects to scoring and simulation without sharing the quote UI switch.

**Tech Stack:** C# 7.3 / .NET Framework 4.7.2 HDT plugin, Node.js source-contract tests, PowerShell C# harnesses, MSBuild Release build.

## Global Constraints

- Preserve all trinket recognition, local rules, recommendation service, panel, and rendering source; hide trinket recommendation display by default for the first public version.
- Remove Firestone/Zero to Heroes trinket-stat runtime requests, caching, use, concrete endpoint, dedicated parser, and retry entry points.
- Do not read, migrate, modify, or delete existing user cache data.
- Keep source-independent validation using synthetic test data only; do not add another external source.
- Do not rerun or clean the full VM acceptance environment and never use `-RemoveUserData`.
- Do not commit, push, create a pull request, publish a release, or modify CI/CD without explicit authorization.
- Preserve the existing untracked approved design document and every pre-existing worktree change.
- Treat quote recommendation and equipped-trinket effects as independent services; `TrinketRecommendationsVisible` may affect rendering only.
- Match hard effects by exact `CardId`; unknown IDs are diagnostic-only and must not change legality, costs, or scoring.

---

### Task 1: Lock the runtime boundary and default-hidden UI in contracts

**Files:**
- Modify: `tests/test_offline_runtime_contract.js`
- Modify: `tests/test_ui_lifecycle.js`
- Modify: `src/BobCoach/BobCoachPlugin.cs`
- Modify: `src/BobCoach/Core/TrinketStatsFetcher.cs`
- Modify: `src/BobCoach/Core/TrinketStatsUpdater.cs`

**Interfaces:**
- Consumes: existing `EvaluateTrinkets(GameState)` and `RenderPlan.TrinketHints` local recommendation flow.
- Produces: `private const bool TrinketRecommendationsVisible = false`; inert `TrinketStatsUpdater` status boundary with no constructor arguments, I/O, timers, or request methods.

- [x] **Step 1: Write failing source contracts**

Update both JavaScript contract suites to require `TrinketRecommendationsVisible = false`, require the final UI branch to include `TrinketRecommendationsVisible && trinketShouldShow`, and reject updater construction/Build notification plus the concrete host, Firestone parser, request/retry methods, timer/task primitives, and cache-store construction in the production runtime path. Keep assertions that the trinket evaluator, UI renderer methods, and all five validation infrastructure files remain compiled.

- [x] **Step 2: Run the focused contracts and verify RED**

Run:

```powershell
node tests/test_offline_runtime_contract.js
node tests/test_ui_lifecycle.js
```

Expected: FAIL because the plugin still creates and drives `TrinketStatsUpdater`, the UI has no default-off gate, and the updater still contains the Firestone endpoint/parser/retry/cache code.

- [x] **Step 3: Implement the minimal runtime boundary**

In `BobCoachPlugin.cs`, add the single constant next to the other feature switches:

```csharp
private const bool TrinketRecommendationsVisible = false;
```

Remove `_trinketStatsUpdater`, its `OnLoad` construction, `OnUnload` disposal, and Build notification. Change only the final trinket UI condition to:

```csharp
if (TrinketRecommendationsVisible && trinketShouldShow)
```

Keep the existing `else` branch so stale hints are cleared, and do not gate candidate recognition, state machines, evaluation, or shadow capture.

Remove `static.zerotoheroes.com` from `TrinketStatsFetcher.AllowedHosts` and replace its Firestone-specific User-Agent with a generic validation-only identifier. Replace `TrinketStatsUpdater` with a sealed inert coordinator that sets `Status = SourceUnavailable` and `StatusReason = "no-authorized-external-source"`; it must not accept a cache path, instantiate fetch/store objects, expose request/retry/parse entry points, use timers/tasks, or touch the filesystem/network.

- [x] **Step 4: Run the focused contracts and verify GREEN**

Run:

```powershell
node tests/test_offline_runtime_contract.js
node tests/test_ui_lifecycle.js
```

Expected: both print PASS; static contracts still find trinket recognition/scoring/UI code but no production external-stat runtime path.

### Task 2: Prove source-independent validation with synthetic data

**Files:**
- Create: `tests/TrinketStatsVerifierHarness.cs`
- Create: `tests/test_trinket_stats_verifier.ps1`
- Modify: `tests/run_behavior_tests.ps1`
- Modify: `src/BobCoach/Core/TrinketStatsVerifier.cs`

**Interfaces:**
- Consumes: `TrinketStatsVerifier.Verify(TrinketStatsSnapshot, TrinketStatsVerificationContext)`.
- Produces: source-independent validation result for any non-empty `Source` and `TimePeriod`, with existing Build, time, ID uniqueness, known-ID, hash rollback, and numeric-range guards unchanged.

- [x] **Step 1: Add a synthetic compiled harness and suite entry**

Create a C# executable harness that builds a minimal candidate entirely in memory with source `synthetic-test-source`, period `synthetic-window`, Build `12345`, two known IDs, current timestamps, positive totals, and valid placement/pick-rate values. It must assert acceptance plus rejection for candidate Build mismatch, duplicate ID, unknown ID, invalid top-level value range, invalid MMR value range, time rollback/content change, and missing source metadata. Add a PowerShell wrapper following the existing C# harness pattern and register it in `run_behavior_tests.ps1`.

- [x] **Step 2: Run the harness and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_trinket_stats_verifier.ps1
```

Expected: FAIL with `source-not-firestone` because the current verifier hard-codes Firestone and `last-patch`.

- [x] **Step 3: Make the verifier source-independent**

Replace Firestone-specific equality checks with non-empty provenance checks:

```csharp
if (string.IsNullOrWhiteSpace(candidate.Source))
    return Reject(TrinketStatsStatus.Quarantined, "source-missing", candidate);
if (string.IsNullOrWhiteSpace(candidate.TimePeriod))
    return Reject(TrinketStatsStatus.Quarantined, "time-period-missing", candidate);
```

Do not weaken the Build, time, duplicate, known-card, rollback/hash, or numeric checks.

- [x] **Step 4: Run the focused behavior tests and verify GREEN**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_trinket_stats_verifier.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_behavior_tests.ps1
```

Expected: synthetic verifier harness and the complete behavior suite print PASS.

### Task 3: Align public and maintainer documentation

**Files:**
- Modify: `tests/test_public_documentation_contract.js`
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `DATA_SOURCES.md`
- Modify: `PRIVACY.md`
- Modify: `NOTICE`
- Modify: `docs/maintainer/ARCHITECTURE.md`
- Modify: `docs/maintainer/DEPENDENCIES.md`

**Interfaces:**
- Consumes: the runtime boundary implemented in Tasks 1-2.
- Produces: consistent public statement that the public build does not request, cache, or display Firestone trinket statistics and that future adapters require separate approval.

- [x] **Step 1: Change the documentation contract first**

Remove the required concrete Firestone endpoint. Require `DATA_SOURCES.md` to state that Firestone/Zero to Heroes is historical evaluation context, the public build does not request/cache/display those statistics, existing historical cache is not read/migrated/deleted, and any future adapter requires separate authorization and approval. Require both README variants, `PRIVACY.md`, `NOTICE`, `ARCHITECTURE.md`, and `DEPENDENCIES.md` to match the same current-source boundary.

- [x] **Step 2: Run the documentation contract and verify RED**

Run:

```powershell
node tests/test_public_documentation_contract.js
```

Expected: FAIL because current documents describe Firestone and HearthstoneJSON as active runtime sources and publish the retired endpoint/cache behavior.

- [x] **Step 3: Update documents with current behavior**

Update both README languages and all named governance documents. Keep third-party acknowledgements factual, describe Firestone only as historical evaluation context, state that the public runtime neither requests nor caches nor displays its statistics, state that historical cache is left untouched and unread, and require a separately approved adapter before future use. Remove the retired concrete endpoint and automated retry/cache schedule from current-source documentation.

- [x] **Step 4: Run documentation and repository contracts**

Run:

```powershell
node tests/test_public_documentation_contract.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_contract_tests.ps1
```

Expected: both print PASS and no current public document claims Firestone is an active runtime source.

### Task 4: Specify equipped-trinket behavior with synthetic tests

**Files:**
- Create: `tests/ActiveTrinketEffectsHarness.cs`
- Create: `tests/test_active_trinket_effects.ps1`
- Modify: `tests/run_behavior_tests.ps1`

- [x] **Step 1: Add a compiled synthetic harness**

Cover exact known-ID resolution, unknown-ID diagnostics, pirate-only golden-copy requirements, precise tavern-spell cost modification, card/board synergy scoring, start-of-combat modification, summon modification, and preservation through shallow simulation copies. Assert that none of these paths consume quote recommendations or `TrinketRecommendationsVisible`.

- [x] **Step 2: Run the harness and verify RED**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_active_trinket_effects.ps1
```

Expected: compilation or assertions fail because the equipped-effect boundary does not yet exist.

### Task 5: Implement the local equipped-trinket boundary

**Files:**
- Create: `src/BobCoach/Core/TrinketEffectRegistry.cs`
- Create: `src/BobCoach/Core/ActiveTrinketContext.cs`
- Create: `src/BobCoach/Core/TrinketEffectResolver.cs`
- Modify: `src/BobCoach/Core/EffectiveGameRules.cs`
- Modify: `src/BobCoach/Core/GameState.cs`
- Modify: `src/BobCoach/GameStateExtractor.cs`
- Modify: `src/BobCoach/Core/TripleRuleEvaluator.cs`
- Modify: `src/BobCoach/Core/GameRuleEvaluator.cs`
- Modify: `src/BobCoach/Core/FeatureExtractor.cs`
- Modify: `src/BobCoach/Core/DecisionEngine.cs`
- Modify: `src/BobCoach/Core/CombatContext.cs`
- Modify: `src/BobCoach/Core/CombatSimulator.cs`
- Modify: `src/BobCoach/Core/Simulator.cs`
- Modify: `src/BobCoach/BobCoach.csproj`

- [x] **Step 1: Add exact-ID registry and immutable active context**

Register only deterministic effects the current model can express. Resolve unknown IDs without guessing and expose them for rate-limited diagnostics.

- [x] **Step 2: Merge hard rules before consumers run**

Resolve the context after `ActiveTrinkets` extraction, compose it with anomaly rules, and route per-card golden requirements and exact spell-cost changes through the existing evaluator entry points.

- [x] **Step 3: Apply synergy and combat effects**

Feed deterministic tags into feature/action scoring. Apply start-of-combat and summon effects inside the combat model, keeping opponent and player contexts separate.

- [x] **Step 4: Run focused and full behavior tests**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_active_trinket_effects.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_behavior_tests.ps1
```

Expected: both pass using synthetic data only.

### Task 6: Align equipped-effect documentation

**Files:**
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `DATA_SOURCES.md`
- Modify: `PRIVACY.md`
- Modify: `NOTICE`
- Modify: `docs/maintainer/ARCHITECTURE.md`
- Modify: `docs/maintainer/DEPENDENCIES.md`
- Modify: `tests/test_public_documentation_contract.js`

- [x] **Step 1: Document the independent product boundary**

State that the public build does not compare trinket offers, while equipped trinkets are interpreted from HDT game state and versioned local rules without Firestone statistics or historical cache access.

- [x] **Step 2: Run documentation contracts**

```powershell
node tests/test_public_documentation_contract.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_contract_tests.ps1
```

### Task 7: Full build, package, and sensitive-data verification

> Final status: implementation, full test suite, Release build, repository validation, deterministic package/whitelist checks, sensitive-information validation, and production source/DLL/package string audit pass.

**Files:**
- Verify: all modified files and generated Release/package outputs

**Interfaces:**
- Consumes: Tasks 1-6.
- Produces: fresh evidence for tests, Release compilation, repository policy, package allowlist/identity/sensitive-information audit, endpoint absence, and clean diff whitespace.

- [x] **Step 1: Run the complete test suite**

Run:

```powershell
npm test
```

Expected: exit 0; contract, behavior, and package suites print PASS.

- [x] **Step 2: Build Release against an installed HDT**

Run the repository-documented Release build command with the discovered HDT installation path, without altering CI/CD or user data:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/build_release.ps1
```

Expected: exit 0 and a Release `BobCoach.dll` is produced using the repository's HDT auto-detection.

- [x] **Step 3: Validate repository and release package policy**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/validate_repository.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_package_tests.ps1
```

Expected: exit 0; release allowlist, identity, and sensitive-information checks pass.

- [x] **Step 4: Audit forbidden endpoint and preserved feature surface**

Search production C# files, the generated DLL, and any generated release manifest/archive listing for `static.zerotoheroes.com`, the retired trinket-stat URL path, and Firestone-specific parser/retry symbols. Search source for `EvaluateTrinkets`, `TrinketRecommendationService`, `ShowTrinketHints`, Power.log trinket choice handling, and the default-off gate.

Expected: no forbidden endpoint/parser/retry/runtime wiring in production source or DLL; all local recognition/recommendation/UI symbols remain; no user cache directory was inspected or touched.

- [x] **Step 5: Check the final worktree without committing**

Run:

```powershell
git diff --check
git status --short --branch
git diff --stat
```

Expected: no whitespace errors; branch remains `codex/add-bilingual-readme`; only intended source, tests, documents, the approved design, and this plan are uncommitted; no commit, push, PR, release, CI/CD, VM cleanup, or user-data operation occurred.

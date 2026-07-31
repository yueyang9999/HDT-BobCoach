# Task 6: Versioned State-Action Feature Schema

## Status

Implemented the season-independent infrastructure for a versioned, named
45-feature state-action vector. This work remains Shadow-only: it neither
creates a trainable policy nor changes the visible `legacy-v1.1.0`
recommendation or fallback.

## Delivered

- Added `ActionFeatureSchema.Version = 1` with the exact fixed-order 45-name
  vector and name lookup.
- Added `ActionFeatureVector`, separate categorical `ActionTypeBiases.OneHot`,
  `ISeasonFeatureExtension`, and fail-closed null extension behavior.
- Added deterministic `ActionFeatureExtractor.Extract(GameState,
  ActionDescriptor, GameState)` with a value and binary coverage entry for
  each feature.
- Kept build, season, card-pool, entity, and semantic uncertainty explicit.
  Semantic and card-pool-dependent features use `0` with coverage `0` until
  trustworthy version-matched facts exist.
- Renamed simulator copying to `DetachedCopy`, structurally cloned board,
  hand, and shop minions, and preserved rule identities and discovery state.
- Registered the production files, behavior test, and release compile-set
  contract (187 source items; SHA-256
  `35be313ad1bd209dfb4838421ff69cd386124544b2776952dca508e3c6dbe29e`).

## TDD Evidence

RED command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Before the production feature types existed, the compiler reported that
`ActionFeatureSchema` and `ActionFeatureExtractor` did not exist.

GREEN command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Result: `PASS 45 named state-action features are deterministic and preference-free`.

## Verification

- `tests/test_algorithm_features.ps1`: PASS.
- `tests/test_value_function_weight_contract.ps1`: PASS.
- `tests/test_release_build_contract.js`: PASS, compile set `187`, expected
  SHA-256 verified.
- `tools/build/validate_repository.ps1`: PASS, 401 tracked or unignored files
  scanned.
- `git diff --check`: PASS.
- `npm test` with process-local `BOBCOACH_HDT_DIR` set to the installed HDT
  `1.54.0` baseline: contract suite and behavior suite passed; deterministic
  Release DLL builds passed three times with identical SHA-256; deterministic
  offline package hashes passed. The final temporary AppData installation
  contract failed because installer protection detected the already-running
  `HearthstoneDeckTracker` process (PID 81244) and correctly refused to write.
  The process was not stopped.

## Self-Review

- The behavior test asserts the exact schema ordering, finite deterministic
  output, binary per-feature coverage, preference isolation, unknown season
  behavior, unknown entity pair coverage, and detached simulator mutation
  safety across supported action types.
- Pair and triple features require resolved identities across both source and
  simulated ownership sets. The season extension additionally requires known
  build, season, and card-pool identities.
- The extractor does not use `ProfileEngine`, user preference weights,
  feature weights, strategy heuristics, or a policy-quality score.
- New source uses C# 7.3-compatible constructs and adds no dependencies.

## Next-Season Data Boundary

1. **Independent of next-season data:** Yes. The schema, vector/coverage
   interface, deterministic extraction, detached simulation, and separate
   action-type encoding contain no season-specific game facts.
2. **Assumptions without evidence:** Next-season entity semantics, mechanics,
   card-pool membership, composition meanings, refresh/freeze value, safe
   leveling risk, economy curves, and labels remain unverified.
3. **Missing-data degradation:** Unknown or incomplete build, season,
   card-pool, entity, and mechanic inputs do not receive inferred meanings.
   Affected semantic features are `0` with coverage `0`; current runtime
   policy visibility remains `legacy-v1.1.0` with Shadow observation only.
4. **Engineering-only tests:** Schema/order, finite values, coverage,
   determinism, detached mutation safety, preference isolation, contracts,
   repository validation, and deterministic packaging prove implementation
   behavior only. They do not establish strategy quality, accuracy, win rate,
   or placement.
5. **Calibration after data arrives:** Bind semantic features to trustworthy
   version-matched facts, validate mappings, establish distributions from real
   labeled scenarios, calibrate coverage/confidence/OOD gates, then retain the
   resulting policy in Shadow through Task 12 effect acceptance.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

## Final Fresh-Review Remediation: Entity And Phase Boundaries

The complete-range fresh review of `0070303..16e6fc5` found two remaining
Important fail-closed gaps and one stale comment:

- blank or whitespace-wrapped `unknown` card identities could receive trusted
  pair/triple coverage;
- any non-empty phase, including `combat`, contributed to shop observation
  completeness;
- `Simulator` comments still described the old shallow-copy behavior.

### RED

Regression coverage was added before implementation for blank and
whitespace-wrapped unknown entity identities, plus combat and blank phases.

```text
FAIL unresolved entity pair   coverage expected=0 actual=1
```

### GREEN Implementation

- Card identity checks now use the same trim-aware resolved identity predicate
  in both the coverage gate and `OwnedCounts`.
- Observation completeness counts phase presence only when the value is exactly
  `shop` using ordinal comparison.
- Simulator documentation now describes detached copies consistently with the
  implementation.

These changes only tighten missing-observation handling. They introduce no
season facts, learned values, strategic thresholds, or visible-policy changes.

### Final Verification

- `tests/test_algorithm_features.ps1`: PASS.
- `tests/test_value_function_weight_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1` with the local HDT `1.54.0` baseline: PASS;
  the existing `CardSemanticFactSource.CardId` compiler warning remains.
- `tests/run_contract_tests.ps1` with the same baseline: PASS.
- `node tests/test_release_build_contract.js`: PASS, compile set `187`, SHA-256
  `35be313ad1bd209dfb4838421ff69cd386124544b2776952dca508e3c6dbe29e`.
- `tools/build/validate_repository.ps1`: PASS, 402 files scanned.
- `git diff --check`: PASS; Git reports only existing LF-to-CRLF conversion
  notices for three modified source/test files.

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

## Review Remediation: Version-Matched Fact Boundary

The review found that `ActionFeatureExtractor` treated static `ActionType`
defaults, default rule evaluation, raw keyword flags, raw card identifiers,
and a target tier as verified semantic facts. None of those sources proves a
build/season/card-pool matched effect, cost, entity identity, or mechanic.

The correction is deliberately fail-closed. Until a trustworthy,
version-matched fact provider is introduced, these features now return exactly
`value=0` and `coverage=0`: `immediate_keyword_delta`, `wasted_gold`,
`legal_follow_up_count`, `playable_follow_up_count`, `unknown_effect_count`,
`simulator_coverage`, `stochastic_range`, `low_health_x_immediate_power`,
`pair_x_matching_shop`, `remaining_gold_x_follow_ups`, and
`target_tier_delta`. `remaining_gold_x_follow_ups` was also closed because it
derives directly from the unavailable follow-up count.

### TDD Evidence

RED command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Actual RED result: `FAIL unverified immediate_keyword_delta value expected=0
actual=1`. The new regression constructs a normal-looking build/season/card
pool with raw keyword, entity, target-tier, and follow-up inputs, then repeats
the boundary for `unknown` build/season/card-pool and an `unknown` entity.

GREEN command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Actual GREEN result: `PASS 45 named state-action features are deterministic
and preference-free`.

### Remediation Verification

- `tests/test_algorithm_features.ps1`: PASS.
- `tests/test_value_function_weight_contract.ps1`: PASS.
- `node tests/test_release_build_contract.js`: PASS, compile set `187` and
  SHA-256 `35be313ad1bd209dfb4838421ff69cd386124544b2776952dca508e3c6dbe29e`.
- `tools/build/validate_repository.ps1`: PASS, 402 files scanned.
- `git diff --check`: PASS before report/staging; rerun after report and
  staging is required before commit.
- `npm test` with the local HDT 1.54.0 baseline: contract suite, behavior
  suite, three deterministic Release DLL builds, and deterministic offline ZIP
  hashes passed. The final temporary AppData installer contract failed because
  the running `HearthstoneDeckTracker` process (PID 81244) correctly prevented
  the installer from writing. The process was not stopped.

### Self-Review

- The extractor no longer uses `SimulatorSupport`, default costs/rules, raw
  keyword flags, target tiers, or raw shop matching to create semantic
  coverage.
- `unknown_effect_count` no longer counts unrelated `nextState` board items;
  without a version-matched effect/affected-entity source it is unavailable.
- Direct structural observations remain unchanged. No card, mechanic, cost, or
  season facts were invented, and no dependency was added.

### Next-Season Data Boundary (Remediation)

1. **Independent of next-season data:** Yes. This change removes unverified
   inference and requires a future fact source rather than adding data.
2. **Unsupported assumptions:** Version-matched effect support, stochasticity,
   costs, legal targets, affected entities, keyword mechanics, entity identity,
   target tiers, and shop matching remain unverified.
3. **Missing-data degradation:** Each affected feature is exactly `0/0`; the
   decision path continues to use Shadow observation with `legacy-v1.1.0` as
   the visible policy and fallback.
4. **Engineering-only tests:** The regressions prove failure closure,
   determinism, schema stability, and detached simulation behavior only. They
   make no claim about decision quality, win rate, or placement.
5. **Calibration after data:** Add a trustworthy version-matched fact provider,
   validate every effect/entity/rule mapping, calibrate coverage and confidence
   on real labeled scenarios, then retain the new policy in Shadow until effect
   acceptance.

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

## Final Review Remediation: Exact Extension Provenance

The remaining Important review finding is closed in the implementation
submitted for fresh review. `ISeasonFeatureExtension` now declares its exact
`GameBuild`, `SeasonId`, and `CardPoolIdentity`. The extractor calls an
extension only when all three identities are non-empty, non-whitespace, and
not `unknown` on both the observed state and the extension, and every identity
matches with `StringComparison.Ordinal`. Any missing, unknown, or mismatched
identity returns exactly `season_extension_value=0` with coverage `0`.

### TDD Evidence

RED command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Actual RED result before production changes: `FAIL season extension build
mismatch value expected=0 actual=7`.

GREEN result after the minimal interface and gate change: `PASS 45 named
state-action features are deterministic and preference-free`. The regression
also covers exact matches; build, season, and card-pool mismatches; and each
state-side and extension-side `unknown` identity.

### Verification

- `tests/test_algorithm_features.ps1`: PASS.
- `tests/test_value_function_weight_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1` with the local HDT 1.54.0 baseline: PASS.
- `tests/run_contract_tests.ps1` with the local HDT 1.54.0 baseline: PASS.
- `node tests/test_release_build_contract.js`: PASS, compile set `187`,
  SHA-256 `35be313ad1bd209dfb4838421ff69cd386124544b2776952dca508e3c6dbe29e`.
- `tools/build/validate_repository.ps1`: PASS, 402 files scanned.
- `git diff --check`: PASS.
- `npm test` with the local HDT 1.54.0 baseline: contract and behavior suites,
  deterministic Release DLL builds, and deterministic offline ZIP hashes
  passed. The command exited `1` only when the final AppData installer
  contract correctly refused to write while `HearthstoneDeckTracker` PID
  81244 was running; the protected process was not stopped or bypassed.

The behavior suite continues to emit its existing
`CardSemanticFactSource.CardId` compiler warning and one existing UI contract
warning. Neither is introduced or suppressed by this remediation.

### Next-Season Data Boundary

1. **Independent of next-season data:** Yes. The change defines provenance
   identity and a generic exact-match gate; it contains no card, mechanic,
   pool, weight, or strategy facts.
2. **Unsupported assumptions:** No trustworthy complete next-season build,
   season, card-pool, entity, or mechanic fact source currently exists, so no
   extension value is treated as available without matching provenance.
3. **Missing-data degradation:** Any empty, whitespace, `unknown`, or
   mismatched provenance fails closed to value `0` and coverage `0`. The new
   policy remains Shadow and `legacy-v1.1.0` remains visible and the fallback.
4. **Engineering-only tests:** These tests prove interface compatibility,
   deterministic exact matching, and conservative failure closure only. They
   do not prove strategy quality, accuracy, win rate, placement, or uplift.
5. **Calibration after data arrives:** Populate extensions only from a
   trustworthy version-matched fact source, validate the three identities and
   semantic mappings, then calibrate coverage/confidence/OOD gates using real
   labeled scenarios while retaining Shadow through effect acceptance.

## Follow-up Review Remediation: Conservative Extraction And Fixed Mappings

This follow-up closes four Important findings without changing the 45-name
order, schema version, one-hot ordering, decision policy, or Shadow-only
boundary.

### TDD Evidence

Each regression was added to `tests/AlgorithmFeatureBehavior.cs` before its
production fix and run with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Observed RED results:

- `FAIL whitespace unknown season provenance value expected=0 actual=7`.
  Matching raw `" unknown "` state and extension identities were incorrectly
  accepted as provenance.
- `FAIL non-finite season extension NaN coverage expected=0 actual=1`.
  `TryEvaluate=true` with a non-finite value was sanitized to zero but still
  presented as available.
- `FAIL missing source board immediate_attack_delta value expected=0 actual=4`.
  A null source board was treated as an empty board and produced a trusted
  cross-state delta.
- `FAIL feature schema must reject external mutation`.
  The public fixed schema list could be rewritten by a caller. The paired
  behavior test also covers the independently mutable action-type list.

The same focused command was GREEN after the minimal implementation changes:

```text
PASS 45 named state-action features are deterministic and preference-free
```

### Delivered

- Provenance sentinel recognition now compares `identity.Trim()` to
  `unknown` with the existing ordinal-ignore-case sentinel convention, while
  exact state-to-extension provenance comparisons still use the original
  strings and `StringComparison.Ordinal`. Independent state-side and
  extension-side whitespace `unknown` regressions prove both paths fail
  closed.
- A season extension has coverage `1` only when its provenance matches,
  `TryEvaluate` returns true, and the returned value is finite. `NaN`,
  positive infinity, and negative infinity are all exactly `0/0`.
- Attack, health, and unit-count board deltas require both source and next
  boards. A null board on either side is exactly `0/0`.
- `ActionFeatureSchema.Names` and `ActionTypeBiases.OrderedTypes` now expose
  `Array.AsReadOnly` views through their existing indexable collection API.
  External writes throw, while count, order, lookup, and one-hot behavior are
  unchanged.

### Files

- `src/BobCoach/Core/Decision/ActionFeatureSchema.cs`
- `src/BobCoach/Core/Decision/ActionFeatureExtractor.cs`
- `tests/AlgorithmFeatureBehavior.cs`
- `.superpowers/sdd/algorithm-task-6-report.md`

### Verification

- `tests/test_algorithm_features.ps1`: PASS.
- `tests/test_value_function_weight_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1` with `BOBCOACH_HDT_DIR` set to local HDT
  `1.54.0`: PASS. It retained the existing `CardSemanticFactSource.CardId`
  compiler warning only.
- `tests/run_contract_tests.ps1` with the same HDT baseline: PASS.
- `node tests/test_release_build_contract.js`: PASS, compile set `187`,
  SHA-256 `35be313ad1bd209dfb4838421ff69cd386124544b2776952dca508e3c6dbe29e`.
- `tools/build/validate_repository.ps1`: PASS, 402 files scanned.
- `git diff --check 0070303..HEAD`: PASS after remediation commit.

### Self-Review

- State-side and extension-side whitespace-unknown regressions are separate,
  so each sentinel gate is proven independently of a provenance mismatch.
- The non-finite test loops over all three IEEE non-finite values; coverage
  is asserted alongside value.
- The null-board test covers both source and next-state absence for all three
  direct board-delta features. No unrelated feature receives a new missing
  data rule.
- Mutation tests assert both rejection and preservation of schema/action
  mapping behavior. `OneHot` still derives its index from the fixed ordered
  action collection.
- The change remains season-independent, uses no user preferences or learned
  weights, adds no dependencies, and makes no claim about strategy quality.

### Simulator Observation Hardening

The regression was written before the production change and first failed with:

```text
FAIL simulator preserves missing hand
```

`tests/AlgorithmFeatureBehavior.cs` now drives `Simulator.Simulate` through a
`FreezeShop` action for states with missing source hand, board, or shop
collections. The detached state retains the missing collection rather than
normalizing it to a trusted empty list. It also covers a null minion in each
collection: simulation does not throw, dependent slots, congestion, board
deltas, and board-full coverage are `0/0`, and observation completeness
reports the missing observations while retaining coverage for that diagnostic.

The focused GREEN command was:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1
```

Result: `PASS 45 named state-action features are deterministic and preference-free`.

### Next-Season Data Boundary

1. **Independent of next-season data:** Yes. This is input validation and
   interface immutability only.
2. **Unsupported assumptions:** No new card, mechanic, entity, cost, pool,
   calibration, or policy facts are introduced.
3. **Missing-data degradation:** Whitespace/unknown provenance, non-finite
   extension outputs, missing minion collections, and null minion entries fail
   closed to `0/0` for dependent policy features.
4. **Engineering-only tests:** Regressions prove conservative coverage,
   deterministic mapping, and mutation resistance only; they do not establish
   decision quality, win rate, placement, or uplift.
5. **Calibration after data:** A future trustworthy version-matched fact
   provider must still populate/calibrate semantic features while policy stays
   in Shadow through effect acceptance.

### Simulator Observation Hardening Verification

- `tests/test_algorithm_features.ps1`: PASS.
- `tests/test_value_function_weight_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1` with process-local `BOBCOACH_HDT_DIR` set
  to the installed HDT `1.54.0` baseline: PASS. The existing
  `CardSemanticFactSource.CardId` compiler warning remains.
- `tests/run_contract_tests.ps1` with the same baseline: PASS.
- `node tests/test_release_build_contract.js`: PASS, compile set `187`,
  SHA-256 `35be313ad1bd209dfb4838421ff69cd386124544b2776952dca508e3c6dbe29e`.
- `tools/build/validate_repository.ps1`: PASS, 402 files scanned.
- `git diff --check`: PASS before the final report update; rerun after this
  update and staging is required before commit.

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

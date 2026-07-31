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

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

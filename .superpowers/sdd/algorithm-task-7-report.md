# Task 7: Versioned Ranking Model Artifact Validation

## Status

Implemented the season-independent contract and fail-closed loader for local
ranking-model artifacts. The committed fixture is synthetic and test-only.
No model is embedded in `Resources/DecisionModels`, and this task does not
compose `RankingModelLoader` into the recommendation runtime.

## Delivered

- Added `RankingModelEnvelope`, `RankingModelPayload`,
  `RankingModelRuntimeContext`, and `ModelValidationException`.
- Added a strict `RankingModelLoader.Load(Stream)` path: 1 MiB envelope and
  decoded-payload limits, Base64 decoding, fixed-time SHA-256 comparison,
  unknown-property rejection, exact feature schema/name ordering, array
  dimensions, finite numeric values, positive standard deviations, and a
  positive OOD distance threshold.
- Added `LoadForRuntime`, which rejects unresolved runtime identities,
  unaccepted artifacts, synthetic identities, and game-build, season, or
  card-pool mismatches.
- Added a synthetic envelope and behavioral coverage for checksum corruption,
  schema drift, reordered names, non-finite values, dimensional errors,
  malformed JSON, and runtime rejection of synthetic/unaccepted inputs.
- Registered the loader and behavioral runner in the project and behavior
  suite. The release compile-set contract now has 188 source items with
  SHA-256 `0c87d352d926e3e715c0ad305a39a6424ab86c59c9dc3dad491778c82a5b3849`.

## TDD Evidence

RED command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_model_contract.ps1
```

Before the production types existed, the compiler reported `CS0246` for
`RankingModelPayload` and `RankingModelEnvelope`.

GREEN command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_model_contract.ps1
```

Result recorded during implementation:

```text
PASS model loader rejects corruption, schema drift, and invalid numbers
```

## Verification

- `tests/test_algorithm_model_contract.ps1`: PASS. The loader rejects
  corruption, schema drift, and invalid numeric values.
- `tests/run_behavior_tests.ps1`: PASS. The full behavior suite completed;
  the pre-existing `CardSemanticFactSource.CardId` CS0649 warning was emitted
  twice and was not suppressed.
- `tests/run_contract_tests.ps1`: PASS. All 13 repository and release
  contracts completed.
- `node tests/test_release_build_contract.js`: PASS. Compile set `188` and
  the expected SHA-256 verified.
- `tools/build/validate_repository.ps1`: PASS. Scanned 407 tracked or
  unignored files.
- `git diff --check`: PASS, with only existing Git LF-to-CRLF notices.
- A direct `Release|x64` MSBuild with the local HDT `1.54.0` baseline:
  PASS. Produced `BobCoach.dll` version `1.1.0.0`, 733,184 bytes, in the
  external Task 7 validation directory.

## Self-Review

- Checksum verification precedes payload JSON deserialization.
- Schema validation uses the immutable Task 6 feature order and the defined
  action-type bias count; arbitrary feature ordering cannot pass.
- The synthetic fixture carries explicit `synthetic-` identities and a
  `not-accepted` status. It is accepted only by artifact-validation tests,
  never by `LoadForRuntime`.
- No production model resource or production `RankingModelLoader.Load(...)`
  call was added. The loader therefore cannot affect recommendations in this
  task.
- The implementation is C# 7.3-compatible and adds no dependency.

## Next-Season Data Boundary

1. **Independent of next-season data:** Yes. Serialization, cryptographic
   integrity, input size/schema/numeric validation, identity matching, and
   rejection behavior contain no season-specific card, mechanic, weight, or
   threshold facts.
2. **Unsupported assumptions:** No real model weights, normalization values,
   OOD threshold, benchmark metric, training identity, or strategy quality is
   supported by evidence. The fixture values are arbitrary finite data.
3. **Missing-data degradation:** A missing, corrupt, unknown, mismatched, or
   unaccepted artifact must yield `ModelUnavailable` with confidence `0` when
   a later runtime composition uses this contract. The new policy remains
   `Shadow`; visible recommendations and fallback remain `legacy-v1.1.0`.
   This task deliberately does not add that composition.
4. **Engineering-only tests:** The synthetic tests prove artifact loading and
   rejection behavior only. They do not prove model quality, accuracy, win
   rate, placement, composition strength, or a useful OOD threshold.
5. **Calibration after data arrives:** Build a candidate externally from at
   least 300 trustworthy, version-matched labeled scenarios; validate exact
   build/season/card-pool identity; calibrate normalization, OOD and
   confidence gates; compare in Shadow; then obtain human review and effect
   acceptance before any production embedding or visible-policy change.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

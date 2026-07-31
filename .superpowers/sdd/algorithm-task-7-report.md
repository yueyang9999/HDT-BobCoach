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

## Review Remediation TDD Evidence

The focused command for every review-remediation cycle was:

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_model_contract.ps1
```

RED evidence was recorded before each production fix:

```text
FAIL artifact model identity did not throw ModelValidationException
FAIL missing envelope checksum expected message=artifact json actual=Invalid ranking model artifact: payload checksum
FAIL invalid envelope UTF-8 expected message=artifact utf-8 actual=Invalid ranking model artifact: checksum
FAIL artifact game build mismatch expected message=artifact game build mismatch actual=Invalid ranking model artifact: artifact identity mismatch
```

After the respective fixes for unresolved artifact identity rejection, required
JSON fields, strict UTF-8 decoding, and specific runtime mismatch diagnostics,
the same focused command produced:

```text
PASS model loader rejects corruption, schema drift, and invalid numbers
```

Additional boundary tests for unknown JSON fields, invalid Base64, envelope
size, all array dimensions, numeric bounds, payload UTF-8, and accepted
resolved runtime loading were added as regression coverage. Those paths were
already fail-closed, so this coverage expansion did not have a production
change or a new RED result.

## Review Remediation Final Verification

Fresh verification after the remediation completed with exit code 0:

- `tests/test_algorithm_model_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1`: PASS. The existing
  `CardSemanticFactSource.CardId` CS0649 warning appeared twice and was not
  suppressed.
- `tests/run_contract_tests.ps1`: PASS. All 13 contracts completed.
- `node tests/test_release_build_contract.js`: PASS. Compile set `188` and
  SHA-256 `0c87d352d926e3e715c0ad305a39a6424ab86c59c9dc3dad491778c82a5b3849`
  matched.
- `tools/build/validate_repository.ps1`: PASS. Scanned 408 tracked or
  unignored files.
- Direct `Release|x64` build against HDT `1.54.0`: PASS. The external
  validation directory contains `BobCoach.dll`, 734,720 bytes, SHA-256
  `F8D36D7F295B29FE81ADB8A2FDE10320FE8E720A5B8F67357BD0BBBAAF795FC5`.

## Review Remediation Approval Binding TDD Evidence

The focused RED/GREEN command was:

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_model_contract.ps1
```

RED output before binding an effect-accepted runtime context to its specific
artifact was:

```text
FAIL approved payload checksum did not throw ModelValidationException
```

The focused regression now covers exact ordinal approval matching for the
payload SHA-256, model ID, training-set identity, and season range. Each
resolved-but-different approval value is isolated so the other approved values
match the candidate. It also covers `unknown` and empty approval values, plus
a fully matching accepted baseline.

After adding explicit approved-artifact fields to `RankingModelRuntimeContext`
and fail-closed comparisons in `LoadForRuntime`, GREEN output was:

```text
PASS model loader rejects corruption, schema drift, and invalid numbers
```

## Review Remediation Approval Binding Final Verification

Fresh verification after exact approval binding completed with exit code 0:

- `tests/test_algorithm_model_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1`: PASS. The existing
  `CardSemanticFactSource.CardId` CS0649 warning was emitted and not
  suppressed.
- `tests/run_contract_tests.ps1`: PASS. All 13 contracts completed.
- `node tests/test_release_build_contract.js`: PASS. Compile set `188` and
  SHA-256 `0c87d352d926e3e715c0ad305a39a6424ab86c59c9dc3dad491778c82a5b3849`
  matched.
- `tools/build/validate_repository.ps1`: PASS. Scanned 408 tracked or
  unignored files.
- Direct `Release|x64` build against HDT `1.54.0`: PASS. The external
  validation directory contains `BobCoach.dll`, 736,256 bytes, SHA-256
  `B656A895E1FB78D13A63BC0F78483C00B469ECB7F968C86C1C688F096EB28E44`.

## Approval Binding Final Rerun

After adding the empty approved-identity regressions, the complete final
verification was rerun against the final working tree with exit code 0:

- `tests/test_algorithm_model_contract.ps1`: PASS.
- `tests/run_behavior_tests.ps1`: PASS. The existing
  `CardSemanticFactSource.CardId` CS0649 warning was emitted and not
  suppressed.
- `tests/run_contract_tests.ps1`: PASS. All 13 contracts completed.
- `node tests/test_release_build_contract.js`: PASS. Compile set `188` and
  SHA-256 `0c87d352d926e3e715c0ad305a39a6424ab86c59c9dc3dad491778c82a5b3849`
  matched.
- `tools/build/validate_repository.ps1`: PASS. Scanned 408 tracked or
  unignored files.
- Direct `Release|x64` build against HDT `1.54.0`: PASS. The external
  validation directory contains `BobCoach.dll`, 736,256 bytes, SHA-256
  `B656A895E1FB78D13A63BC0F78483C00B469ECB7F968C86C1C688F096EB28E44`.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

## Duplicate-Property Review Remediation TDD Evidence

RED command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_model_contract.ps1
```

Exact RED output after adding same-value duplicate envelope and payload JSON
properties, before the production fix:

```text
FAIL duplicate envelope property did not throw ModelValidationException
```

GREEN command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_model_contract.ps1
```

Exact GREEN output after strict duplicate-property rejection and the four
case-variant approval-binding regressions:

```text
PASS model loader rejects corruption, schema drift, and invalid numbers
```

The envelope and payload now reject duplicate JSON property names before DTO
deserialization. The approved payload SHA-256, model ID, training-set
identity, and season range each reject a value that differs only by case,
using `StringComparison.Ordinal`.

## Duplicate-Property Review Remediation Final Verification

Fresh final verification completed with exit code 0:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_model_contract.ps1`
  output: `PASS model loader rejects corruption, schema drift, and invalid numbers`.
- With `BOBCOACH_HDT_DIR=E:\HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker`,
  `tests\run_behavior_tests.ps1` output: `PASS behavior suite`. The existing
  `CardSemanticFactSource.CardId` CS0649 warning appeared twice and was not
  suppressed.
- With the same `BOBCOACH_HDT_DIR`, `tests\run_contract_tests.ps1` output:
  `PASS contract suite` (13 contracts).
- `node tests\test_release_build_contract.js` output:
  `PASS release build contract compile=188 sha256=0c87d352d926e3e715c0ad305a39a6424ab86c59c9dc3dad491778c82a5b3849`.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\build\validate_repository.ps1`
  output: `PASS repository validation: 408 Git tracked or untracked non-ignored file(s) scanned; large-file limit is 5242880 bytes (5 MiB).`
- `git diff --check`: PASS (exit code 0).

## Required Conclusion (Preserved)

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

## Canonical-Property And Synthetic-Gate Review RED Evidence

Focused command with process-level
`BOBCOACH_HDT_DIR=%LOCALAPPDATA%\HearthstoneDeckTracker\app-1.54.1`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_model_contract.ps1
```

Exact RED output after adding the envelope and payload case-variant alias
regressions, before changing production code (exit code 1):

```text
FAIL case-variant envelope property did not throw ModelValidationException
```

After canonical property enforcement fixed the first RED, the same focused
command reached the synthetic season-range regression and produced this exact
second RED output (exit code 1):

```text
FAIL synthetic artifact expected message=synthetic artifact actual=Invalid ranking model artifact: approved payload checksum mismatch
```

The smallest production fixes enforce exact canonical DTO property names
before deserialization, include `SeasonRange` in the synthetic-artifact gate,
and apply the reserved-prefix check to trimmed values. The focused command then
completed GREEN (exit code 0):

```text
PASS model loader rejects corruption, schema drift, and invalid numbers
```

## Canonical-Property And Synthetic-Gate Final Verification

Fresh verification against the final production and test changes produced:

- `tests\test_algorithm_model_contract.ps1`: PASS with
  `PASS model loader rejects corruption, schema drift, and invalid numbers`.
- Controller rerun of `tests\run_behavior_tests.ps1` with process-level
  `BOBCOACH_HDT_DIR=C:\Users\19844\AppData\Local\HearthstoneDeckTracker\app-1.54.1`:
  PASS. The two existing `CardSemanticFactSource.CardId` CS0649 warnings and
  the existing UI lifecycle static warning were not suppressed.
- `tests\run_contract_tests.ps1`: PASS, all 13 contracts.
- `node tests\test_release_build_contract.js`: PASS, compile set `188`,
  SHA-256 `0c87d352d926e3e715c0ad305a39a6424ab86c59c9dc3dad491778c82a5b3849`.
- `tools\build\validate_repository.ps1`: PASS, 408 tracked or untracked
  non-ignored files scanned.
- Release build against the existing HDT `1.54.1` directory with
  `-AllowNewerHdt`: PASS. `BobCoach.dll` is 737,280 bytes with SHA-256
  `D4D5B09947FE22B92A41D204EFB191FC8BB3A793549818F6761287B48A08D4DF`.
  The managed command policy blocked cleanup of the two-file
  `%TEMP%\bobcoach_build_8679604f-0aef-4f06-8e55-b918406b97c3` output after
  its location, file count, and byte count were verified.
- `git diff --check`: PASS, with only Git's LF-to-CRLF notices.

Self-review confirmed that the diff contains no production model, runtime
policy activation, season content, inferred weights, or strategy-quality
claim. Unknown identities still fail closed, the new policy remains Shadow,
and `legacy-v1.1.0` remains the visible recommendation and fallback policy.

## Required Conclusion (Preserved)

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

# Task 8: Deterministic Pairwise Action Ranking

## Status

Implemented the season-independent linear pairwise ranking and training
infrastructure. The unified policy is diagnostic-only and returns confidence
`0`; it is not composed into player-visible runtime behavior. No real model,
season content, inferred weight, or strategy-quality claim was added.

## Delivered

- Added normalized linear scoring with one-hot action-type biases, finite-value
  validation, contribution factors, and deterministic ordinal `ActionKey`
  tie-breaking.
- Added diagnostic `UnifiedRankingPolicy` composition over candidate
  generation, hard constraints, detached simulation, feature extraction, and
  ranking. Production construction rejects unresolved, unaccepted, synthetic,
  and mismatched identities.
- Added zero-dependency pair construction that requires explicit expert grades
  and `acceptedActionKeys`, excludes `not-evaluable`, and never treats observed
  player actions as labels.
- Added deterministic seeded pairwise logistic training. The in-memory test
  path returns a test-only synthetic envelope containing payload bytes and a
  SHA-256 checksum; the external candidate path remains
  `blocked-by-season-data`.
- Added whole-game, time-aware splitting with
  `stateFingerprint|preferred.candidateFingerprint` deduplication.
- Added focused C# and Node coverage and registered it in the behavior and
  contract suites. The release compile contract now has 190 source items with
  SHA-256 `cd4c0fe24414d968df50fe8904ca2e3876ff22878fb0d381660b9c9f4eec848f`.

## TDD Evidence

Recovered WIP first exposed an `ActionKey` contract mismatch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_linear_ranker.ps1
```

```text
FAIL top candidate expected=strong actual=Refresh|-1|strong|test
```

`RankedAction.ActionKey` is the stable full action identity, so the assertion
was corrected to the contract value rather than changing production identity.
The focused GREEN output is:

```text
PASS linear ranker uses one deterministic score surface
```

The deterministic training envelope RED command was:

```powershell
node tests/test_pairwise_training.js
```

Before adding the checksum envelope, the exact failure was:

```text
actual: test-only-synthetic-training-result-v1
expected: test-only-synthetic-training-envelope-v1
```

After adding `payloadBase64` and `payloadSha256`, the GREEN output is:

```text
PASS pairwise trainer is deterministic and split-safe; no strategy quality asserted
```

The focused policy test was then extended to reject a synthetic artifact.
Before the production composition gate, the exact RED output was:

```text
FAIL synthetic model
```

After adding the synthetic identity rejection, the C# focused test returned
the GREEN output shown above.

## Files Changed

- `src/BobCoach/Core/Decision/LinearPairwiseRanker.cs`
- `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- `src/BobCoach/BobCoach.csproj`
- `tools/model/build_pairs.js`
- `tools/model/train_pairwise.js`
- `tools/model/split_dataset.js`
- `tests/fixtures/algorithm/pairs.synthetic.jsonl`
- `tests/AlgorithmLinearRankerBehavior.cs`
- `tests/test_algorithm_linear_ranker.ps1`
- `tests/test_pairwise_training.js`
- `tests/run_contract_tests.ps1`
- `tests/run_behavior_tests.ps1`
- `tests/test_release_build_contract.js`

## Verification

- `tests/test_algorithm_linear_ranker.ps1`: PASS against the exact local HDT
  `1.54.0.7376` baseline.
- `node tests/test_pairwise_training.js`: PASS.
- `npm test`: PASS with process-level
  `BOBCOACH_HDT_DIR=...\HearthstoneDeckTracker\app-1.54.0`. Contract, behavior,
  and package suites all completed. The existing `CardSemanticFactSource.CardId`
  CS0649 warning and existing UI lifecycle warning were not suppressed.
- `tools/build/build_release.ps1`: PASS against HDT `1.54.0.7376`. Produced
  `BobCoach.dll`, 742,400 bytes, SHA-256
  `1AE1EECCAA327DB99B5CB7998739FB332DDFA0BF17938DECEE6D10A033AA399F`, in a
  GUID-scoped `%TEMP%` output directory.
- `git diff --check`: PASS, with only Git LF-to-CRLF notices.
- `tools/build/validate_repository.ps1`: the Task 8 personal-path finding was
  removed. The scanner still reports one issue in the committed baseline file
  `.superpowers/sdd/algorithm-task-7-report.md`; Task 8 did not rewrite
  historical Task 7 evidence. The scan covered 417 tracked or untracked,
  non-ignored files and found no other issue.

## Self-Review

- Pair labels can be created only from explicit expert grades where the
  higher-grade candidate is also in `acceptedActionKeys`. Player behavior,
  combat outcome, and placement do not enter the emitted pair.
- `not-evaluable` candidates cannot enter training pairs. Whole games remain
  in one split, and the newest games form the holdout.
- Training bytes are deterministic for identical input and seed. The test-only
  synthetic payload is checksum-bound and is not emitted as a production
  ranking model.
- `UnifiedRankingPolicy.CreateProduction` rejects unresolved identity,
  unaccepted effects, synthetic artifacts, and build/season/card-pool
  mismatches. Unknown state identity returns `ModelUnavailable` with
  confidence `0`.
- Successful unified rankings remain explicitly `shadow-only` with confidence
  `0`. No production call site or visible-policy selection was added;
  `legacy-v1.1.0` remains player visible and the fallback.
- No dependency, CI/CD, environment file, secret, installer behavior, HDT
  process, deployment, or release configuration was changed.

## Next-Season Data Boundary

1. **Independent of next-season data:** Yes. Ranker arithmetic, stable
   ordering, schema validation, pair construction gates, seeded training,
   checksum binding, whole-game splitting, and diagnostic policy composition
   contain no season-specific facts.
2. **Unsupported assumptions:** Preferred actions, model weights,
   normalization values, action-type biases, composition strength, refresh
   value, economy curves, calibration thresholds, and any quality uplift lack
   trustworthy version-matched evidence.
3. **Missing-data degradation:** The external training path does not produce a
   candidate; unresolved or mismatched model/state identity yields
   `ModelUnavailable` with confidence `0`; successful rankings remain Shadow
   diagnostics. Visible recommendations and fallback remain
   `legacy-v1.1.0`.
4. **Engineering-only tests:** Hand-authored vectors prove scoring,
   explanations, finite checks, and tie-breaking. Synthetic pair rows prove
   label isolation, split safety, byte determinism, and checksum integrity.
   They do not prove decision quality, win rate, placement, composition
   strength, refresh value, or model accuracy.
5. **Calibration after data arrives:** Collect at least 300 trustworthy,
   version-matched expert-labeled scenarios; build pairs from explicit grades;
   reserve newest whole games as holdout; train and validate candidates outside
   the repository; calibrate normalization, OOD, confidence, and coverage;
   compare in Shadow and complete human review and effect acceptance before
   any visible-policy change.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

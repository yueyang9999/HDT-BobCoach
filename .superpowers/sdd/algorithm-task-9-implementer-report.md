# Task 9: Confidence, Coverage, And Distribution-Shift Gate

## Status

Implemented the season-independent confidence gate and integrated it into the
diagnostic unified ranking policy. Missing or unauthorized calibration,
critical-state gaps, unsupported feature coverage, distribution shift, and low
confidence now fail closed with the exact fallback reason. The unified policy
remains Shadow-only; `legacy-v1.1.0` remains player visible and is the runtime
fallback.

- Baseline commit: `458318981b6e6b438522a0d8203c352eecdd0921`
- Final Task 9 commit: the commit containing this report; its SHA is recorded in
  the post-commit handoff because a commit cannot contain its own stable SHA.
- Target commit message: `feat: gate ranked decisions by confidence and coverage`

## Delivered

- Added `ConfidenceAssessment`, `ConfidenceCalibration`,
  `ConfidenceCalibrationApprovalContext`, and
  `DecisionConfidenceEvaluator`.
- Bound calibration to authoritative holdout and calibration identities. A
  missing, `unknown`, or ordinally mismatched identity returns
  `ModelUnavailable` with confidence `0`.
- Implemented the required fallback precedence: `ModelUnavailable`,
  `CriticalStateMissing`, `UnsupportedMechanic`, `OutOfDistribution`, then
  `LowConfidence`.
- Computed finite 45-feature distribution distance from accepted model means,
  standard deviations, and bounds. The final schema feature,
  `season_extension_value`, is included in OOD detection.
- Required decision-critical feature coverage and complete simulator support.
  `SimulatorSupport.Partial` and `SimulatorSupport.None` both set
  `simulator_coverage` to `0` and increment `unknown_effect_count`; only
  `Deterministic` and `ExpectedValue` count as complete.
- Preserved ranked diagnostics and the evaluator's exact fallback reason for
  the orchestrator while leaving visible selection to the legacy policy.
- Added a four-argument production factory for authority-bound calibration.
  The existing two-argument factory supplies no calibration and therefore
  remains conservatively fail closed.
- Registered the evaluator and focused tests. The release compile contract is
  191 source items with SHA-256
  `f9c8a29f7b32aea7d8336c963660d6473527dc4b175f9ae89253ecb20316d8cd`.

## TDD Evidence

The initial focused RED command was:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_confidence.ps1
```

Before production confidence types existed, compilation reported `CS0246` for
`ConfidenceCalibration` and `ConfidenceAssessment`. After the initial
implementation exposed a decision-critical coverage defect, the raw assertion
failure was:

```text
FAIL critical zero coverage confidence expected=0 actual=0.5
```

The evaluator was changed so decision-critical zero coverage forces
`UnsupportedMechanic` and confidence `0`. The focused GREEN output was:

```text
PASS confidence floors force explicit safe fallback
```

The review-fix wave added an independent approval authority. Its RED compile
evidence was:

```text
tests\AlgorithmConfidenceBehavior.cs(316,20): error CS0246:
未能找到类型或命名空间名“ConfidenceCalibrationApprovalContext”
```

After implementing and binding that context, focused coverage passed for
missing approval, holdout mismatch, calibration mismatch, authority-bound
production construction, partial-simulator penalty, and last-feature OOD.

## Files Changed

- `src/BobCoach/BobCoach.csproj`
- `src/BobCoach/Core/Decision/DecisionConfidenceEvaluator.cs`
- `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- `tests/AlgorithmConfidenceBehavior.cs`
- `tests/AlgorithmLinearRankerBehavior.cs`
- `tests/run_behavior_tests.ps1`
- `tests/test_algorithm_confidence.ps1`
- `tests/test_algorithm_linear_ranker.ps1`
- `tests/test_release_build_contract.js`
- `.superpowers/sdd/algorithm-task-9-implementer-report.md`

## Verification

- `tests/test_algorithm_confidence.ps1`: PASS,
  `confidence floors force explicit safe fallback`.
- `tests/test_algorithm_linear_ranker.ps1`: PASS,
  `linear ranker uses one deterministic score surface`.
- `tests/test_algorithm_orchestrator.ps1`: PASS,
  `shadow mode preserves legacy output and contains unified failures`.
- `tests/test_release_build_contract.js`: PASS, compile items `191`, SHA-256
  `f9c8a29f7b32aea7d8336c963660d6473527dc4b175f9ae89253ecb20316d8cd`.
- Fresh full `npm test`: PASS in 223.6 seconds with exit code `0`; contract,
  behavior, and offline package suites all completed against the configured
  exact HDT `1.54.0.7376` baseline.
- `git diff --check`: PASS, with only Git LF-to-CRLF notices.
- `tools/build/validate_repository.ps1`: scanned 422 Git tracked or untracked
  non-ignored files and reported exactly one issue: the pre-existing personal
  absolute path in committed baseline file
  `.superpowers/sdd/algorithm-task-7-report.md`. Task 9 does not rewrite
  historical Task 7 evidence and introduced no additional validator finding.

## Review

The internal implementation review reported:

```text
Critical: 0
Important: 0
Minor: 0
APPROVED YES
```

The first fresh fixed-package review reported `Critical: 0`, `Important: 2`,
and `Minor: 1`. The review found that calibration was not bound to the exact
model ID and that malformed feature vectors could reach threshold evaluation
instead of failing closed. The repair binds calibration and its independent
approval context to `ModelId`, rejects non-finite or structurally invalid
vectors as `UnsupportedMechanic` with confidence `0` and planning disabled,
and removes the unused O(n^2) ranked-action mapping. Regression tests cover
calibration/approval model mismatch, non-finite values under extreme legal
thresholds, and a wrong-length vector. A replacement fixed package requires
re-review by the same independent reviewer.

## Next-Season Data Boundary

1. **Independent of next-season data:** Yes. Confidence composition, reason
   precedence, explicit coverage, OOD arithmetic, identity binding,
   fail-closed control flow, and Shadow integration use no season-specific
   card or strategy facts.
2. **Unsupported assumptions:** Operational coverage floors, score-margin
   scale, OOD bound, confidence threshold, planning threshold, model weights,
   and any recommendation-quality uplift lack trustworthy version-matched
   evidence.
3. **Missing-data degradation:** Missing/unapproved calibration or identity,
   unknown critical state, decision-critical zero coverage, or unsupported
   simulation lowers confidence to `0` and preserves the exact fallback reason.
   The orchestrator keeps visible output on `legacy-v1.1.0`.
4. **Engineering-only tests:** Synthetic vectors and states prove finite
   arithmetic, precedence, schema coverage, deterministic ranking, authority
   binding, fallback containment, and regression behavior only. They do not
   prove next-season strategy superiority, accuracy, win rate, or placement.
5. **Calibration after data arrives:** Use real version-matched training and
   validation data to estimate distributions and thresholds, freeze them into
   the candidate envelope, verify against the current-season holdout, collect
   at least 300 trustworthy labeled scenarios, complete Task 12 Shadow
   comparison and human review, then pass effect acceptance before any visible
   policy change.

The data-dependent effect claim remains `blocked-by-season-data`. Engineering
acceptance does not imply effect acceptance.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

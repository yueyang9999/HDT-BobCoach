# Task 11: Contribution Explanations And Plugin Shadow Integration

## Status

The implementation and engineering verification are complete, and the task is
awaiting review against a frozen Task 11 review bundle. All Task 11 tests,
contracts, package checks, the Release build, and diff checks pass. Repository
validation reports only the pre-existing Task 7 report personal-path finding
documented below. The unified policy remains hard-coded to `Shadow`;
`legacy-v1.1.0` remains the player-visible policy and fallback.

Effect acceptance remains `blocked-by-season-data`. This task does not claim
improved placement, win rate, accuracy, or decision quality.

## Delivered

- Added contribution-derived explanations that use a fixed player-facing phrase
  map, select only factors present on the chosen action, and never expose raw
  weights or confidence formulas.
- Added `DecisionPolicyFactory` as the production composition boundary for the
  legacy policy, candidate generator, and unified ranking graph.
- Preserved strict runtime model approval: the three-argument factory overload
  fails closed without approval context, while the four-argument overload uses
  `RankingModelLoader.LoadForRuntime` and reports unavailable model identity as
  `unknown`.
- Reused one factory-owned `CandidateGenerator` for runtime ranking, lifecycle
  fingerprints, and observed-action inference.
- Integrated the plugin with a hard-coded `DecisionRuntimeMode.Shadow`
  orchestrator. Missing, malformed, mismatched, or unapproved model artifacts
  keep the unified policy unavailable and leave visible recommendations on
  `legacy-v1.1.0`.
- Added explicit in-memory user opt-in for local `decision-events.jsonl`
  recording. Consent is not persisted as a setting, profile, or session object,
  and recording is disabled unless both consent and the Power.log prerequisites
  are active.
- Bound decision-created, Shadow comparison, visible recommendation, observed
  action, and final placement events to the same decision lifecycle with
  deduplication.
- Added pure observed-action inference. It emits a candidate action key only for
  one unambiguous match, emits `observation|unresolved|unknown` for multiple
  matches, and emits nothing when no candidate matches.
- Required refresh inference to remain within the same turn, observe a changed
  shop entity set, and reconcile either the exact paid refresh cost or one
  consumed free refresh. Automatic turn-start shop replacement is not recorded
  as a player refresh.
- Added model ID and payload checksum fields to local decision events, normalized
  to `unknown` when no approved model exists.

## TDD Evidence

Recorded RED evidence from the implementation and review-remediation loops:

- Explanation and plugin runtime contract tests failed before the explanation
  builder and Shadow lifecycle wiring existed.
- The explicit recording-consent contract failed before the in-memory opt-in
  guard and Yes/No disclosure were added.
- The factory compatibility test failed to compile before the three-argument
  overload was restored as a fail-closed entry point.
- Observed-action behavior tests failed to compile before the production helper
  existed.

Focused GREEN evidence after remediation:

- `node tests/test_offline_runtime_contract.js`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_explanations.ps1`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_observed_actions.ps1`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_decision_events.ps1`
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_linear_ranker.ps1`
- `node tests/test_log_config_consent.js`
- `node tests/test_release_build_contract.js`

## Files Changed

Production:

- `src/BobCoach/BobCoach.csproj`
- `src/BobCoach/BobCoachPlugin.cs`
- `src/BobCoach/Core/ActionEnumerator.cs`
- `src/BobCoach/Core/Decision/DecisionEventRecorder.cs`
- `src/BobCoach/Core/Decision/DecisionLifecycleTracker.cs`
- `src/BobCoach/Core/Decision/DecisionPolicyFactory.cs`
- `src/BobCoach/Core/Decision/ExplanationBuilder.cs`
- `src/BobCoach/Core/Decision/ObservedActionInference.cs`
- `src/BobCoach/Core/Decision/RankingModel.cs`
- `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`

Tests and release contracts:

- `tests/AlgorithmDecisionEventBehavior.cs`
- `tests/AlgorithmExplanationBehavior.cs`
- `tests/AlgorithmLinearRankerBehavior.cs`
- `tests/AlgorithmObservedActionInferenceBehavior.cs`
- `tests/run_behavior_tests.ps1`
- `tests/test_algorithm_confidence.ps1`
- `tests/test_algorithm_explanations.ps1`
- `tests/test_algorithm_linear_ranker.ps1`
- `tests/test_algorithm_observed_actions.ps1`
- `tests/test_offline_runtime_contract.js`
- `tests/test_release_build_contract.js`

## Review

Pending fresh review against the frozen Task 11 review bundle. Reviewer results
must be recorded only after that review completes.

## Verification

The final verification used the exact local HDT `1.54.0` baseline:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_behavior_tests.ps1`
  passed with exit code `0`. The complete behavior suite passed, including the
  Task 11 Shadow, lifecycle, observed-action, confidence, and explanation cases.
  The existing `CardSemanticFact.CardId` `CS0649` warning remains non-fatal.
- `npm test` passed with exit code `0` in `240.7s`. Contract, behavior, and
  package suites all completed successfully. The release compile allowlist is
  `196` items with SHA-256
  `1daabc5d5cb8db52bff033b0ee72a0535f0deef3a80cb4f08ccfb98dfb833e29`.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/build_release.ps1`
  passed with exit code `0` against HDT `1.54.0`. The Release DLL is `772608`
  bytes with SHA-256
  `A7322D00A76899B0EDC2819ABD4AAB058ADDC085A8F53BE182CFE4232032F9FB`.
- `git diff --check` passed with exit code `0`; Git emitted only line-ending
  conversion warnings and no whitespace errors.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/validate_repository.ps1`
  reported exactly one issue across `435` tracked or untracked non-ignored
  files: the pre-existing personal absolute path in
  `.superpowers/sdd/algorithm-task-7-report.md`. Task 11 introduces no new
  repository-validation finding, and the historical Task 7 report was not
  modified.

## Next-Season Data Boundary

1. **Is the change independent of next-season data?**
   Yes for explanation formatting, composition, lifecycle ordering,
   deduplication, consent, model validation, fail-closed behavior, and Shadow
   recording. None of those mechanisms requires next-season strategic facts.

2. **Which assumptions currently lack evidence?**
   There is no reliable complete version-matched next-season card pool, mechanic
   mapping, calibrated model, feature distribution, labeled scenario set, or
   evidence that unified rankings improve real decisions.

3. **How does the system degrade when data is missing?**
   Runtime model approval fails closed, model identity is recorded as `unknown`,
   unified output is diagnostic-only in Shadow, and `legacy-v1.1.0` continues to
   provide visible recommendations and fallback.

4. **Which tests prove engineering correctness only?**
   Synthetic model streams, malformed artifacts, controlled score
   contributions, factory/component parity fixtures, lifecycle event tests,
   action-delta fixtures, deterministic build checks, and offline runtime
   contracts prove interfaces and containment. They do not prove strategy
   superiority.

5. **What calibration remains after real next-season data becomes available?**
   Task 12 must bind a checksum-identical candidate to exact build, season, and
   card-pool evidence; evaluate at least 300 trustworthy version-matched labeled
   scenarios; compare Shadow results under a frozen protocol; and complete human
   effect review. A separate owner-approved release change is still required for
   any player-visible activation.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

# Task 10: Expected Refresh And Selective Deterministic Beam Planning

## Status

Implemented the season-independent expected-shop availability boundary and
selective deterministic beam planner. Planning is requested only by the Task 9
confidence gate, is bounded to Top-4 roots, width 6, depth 4, and a 25 ms
monotonic budget, and remains diagnostic-only in production. The unified policy
stays in Shadow; `legacy-v1.1.0` remains player visible and is the runtime
fallback.

- Baseline commit: `a387f8e0fc8ac0a6754f47e5cc9330eb027fa984`
- Final Task 10 commit: the commit containing this report; its SHA is recorded
  in the post-commit handoff because a commit cannot contain its own stable SHA.
- Target commit message: `feat: add selective deterministic beam planning`

## Delivered

- Added `ExpectedShopEstimate`, `IExpectedShopPoolProvider`, and
  `ExpectedShopEstimator` as the version-matched refresh-value boundary.
- Missing, incomplete, unresolved, or identity-mismatched pool data returns an
  unavailable estimate with value `0`, coverage `0`, and identity `unknown`.
- Preserved provider-supplied fractional coverage for available refresh facts;
  non-refresh actions do not call the expected-shop estimator.
- Added `BeamPlanner`, `PlanningResult`, transition and monotonic-clock
  contracts, and explicit completed, timed-out, unsupported, and not-requested
  outcomes.
- Enforced deterministic ordinal ordering, Top-4 roots, beam width 6, depth 4,
  detached simulation transitions, and timeout retention of the best observed
  path.
- Unsupported stochastic actions terminate. An unavailable refresh terminates
  without applying a transition, producing a concrete refreshed state, adding
  expected value, or inheriting an invented continuation score.
- Limited production continuation to deterministic buy, sell, and upgrade
  actions. Production planning cannot replace the originally ranked root.
- Integrated planning only when `DecisionConfidenceEvaluator.ShouldPlan` is
  true and exposed the planning status as Shadow diagnostics.
- Registered the new production sources and behavior harness, and updated the
  release compile contract to 193 source items with SHA-256
  `4fc9305dc8783701d2334d0a6838762c7612e8b721cce5aa2dc901b9111bf807`.

## TDD Evidence

The initial focused RED command was:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_beam_planner.ps1
```

Before production planning types existed, compilation reported missing types
for `BeamPlanner`, `ExpectedShopEstimator`, the pool provider, and the monotonic
elapsed-time source. A second RED wave exposed the missing feature-extractor
constructor integration and planning status propagation through confidence
behavior tests. The RED output is retained outside the repository at
`$env:TEMP\bobcoach-task10-red.txt`.

After implementation, the focused GREEN output was:

```text
PASS 45 named state-action features are deterministic and preference-free
PASS confidence floors force explicit safe fallback
LATENCY ranking P50=0.004ms P95=0.007ms P99=0.010ms
LATENCY planning P50=0.093ms P95=0.148ms P99=0.254ms
PASS beam planning is deterministic, bounded, and timeout-safe
```

The tests cover unavailable estimates, exact identity matching, fractional
coverage, stable traversal, hard bounds, timeout retention, detached states,
fail-closed refresh behavior, selective invocation, and diagnostic-only root
selection.

## Files Changed

- `src/BobCoach/BobCoach.csproj`
- `src/BobCoach/Core/Decision/ActionFeatureExtractor.cs`
- `src/BobCoach/Core/Decision/ExpectedShopEstimator.cs`
- `src/BobCoach/Core/Decision/BeamPlanner.cs`
- `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- `tests/AlgorithmBeamPlannerBehavior.cs`
- `tests/AlgorithmConfidenceBehavior.cs`
- `tests/AlgorithmFeatureBehavior.cs`
- `tests/AlgorithmLinearRankerBehavior.cs`
- `tests/run_behavior_tests.ps1`
- `tests/test_algorithm_beam_planner.ps1`
- `tests/test_algorithm_confidence.ps1`
- `tests/test_algorithm_features.ps1`
- `tests/test_algorithm_linear_ranker.ps1`
- `tests/test_release_build_contract.js`
- `.superpowers/sdd/algorithm-task-10-implementer-report.md`

`.superpowers/sdd/progress.md` was not modified.

## Verification

- Focused feature, confidence, linear-ranker, and beam-planner behavior suites:
  PASS.
- Fresh full `npm test`: PASS in 224.2 seconds with exit code `0`; contract,
  behavior, release build, and offline package suites completed against the
  configured exact HDT `1.54.0.7376` baseline.
- Release DLL reproducibility: three builds produced 760832 bytes and SHA-256
  `5D5CE166B4726AAC6C5D9E03A91C2411C2A62E9047E13B1F641694BF534DDD39`.
- Release compile contract: 193 items, SHA-256
  `4fc9305dc8783701d2334d0a6838762c7612e8b721cce5aa2dc901b9111bf807`.
- `git diff --check`: PASS, with only Git LF-to-CRLF notices.
- `tools/build/validate_repository.ps1`: scanned 426 Git tracked or untracked
  non-ignored files and reported exactly one issue: the pre-existing personal
  absolute path in committed baseline file
  `.superpowers/sdd/algorithm-task-7-report.md`. Task 10 did not modify that
  historical report and introduced no additional validator finding.

The full suite used a local ASCII-only copy of the exact HDT baseline because
the decision-event runner's .NET Framework configuration could not load
`Newtonsoft.Json` through a `codeBase` URL containing non-ASCII path segments.
The baseline assemblies themselves were unchanged; this is a local validation
path limitation, not a production-code bypass.

## Next-Season Data Boundary

1. **Independent of next-season data:** Yes. Availability contracts, explicit
   identity checks, deterministic traversal, state detachment, bounds, timeout
   handling, stable tie-breaking, selective invocation, and diagnostic-only
   production integration use no next-season strategy facts.
2. **Unsupported assumptions:** Next-season pool membership and copy counts,
   card power, refresh value, economy curves, continuation scoring, and a safe
   root-replacement margin lack trustworthy version-matched evidence. No such
   values were synthesized.
3. **Missing-data degradation:** Expected-shop estimation returns unavailable
   with value `0`, coverage `0`, and identity `unknown`. Refresh expansion stops
   without a state or continuation value, simulator coverage falls closed,
   confidence drops, planning cannot replace the ranked root, and visible
   output remains `legacy-v1.1.0`.
4. **Engineering-only tests:** Synthetic pools, states, graphs, scores, and
   clocks prove interfaces, identity containment, deterministic traversal,
   bounds, timeout behavior, latency-harness mechanics, fallback, and Shadow
   integration only. They do not prove refresh value, strategy superiority,
   accuracy, win rate, or placement.
5. **Calibration after data arrives:** Load a complete version-matched pool;
   derive expected-shop inputs, continuation scoring, and any root-replacement
   margin from real labeled scenarios; validate on holdout Shadow comparisons;
   collect at least 300 trustworthy version-matched labeled scenarios; and
   complete human review and effect acceptance before changing visible policy.

The data-dependent estimator implementation and every effect claim remain
`blocked-by-season-data`. Engineering acceptance does not imply effect
acceptance.

## Required Conclusion

New-algorithm infrastructure is implemented or being completed, but reliable
complete next-season content is not currently available. Critical data remains
missing, actual decision improvement is unverified, the new policy remains in
Shadow, and `legacy-v1.1.0` continues to provide visible recommendations and
fallback.

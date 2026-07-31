# Task 10: Expected Refresh And Selective Deterministic Beam Planning

## Status

Implemented the season-independent expected-shop availability boundary and
selective deterministic beam planner. Planning is requested only by the Task 9
confidence gate, is bounded to Top-4 roots, width 6, depth 4, and a 25 ms
monotonic budget checked at every interruptible synchronous-call boundary, and
remains diagnostic-only in production. The unified policy stays in Shadow;
`legacy-v1.1.0` remains player visible and is the runtime fallback.

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
- Kept the original Task 8 score ordering only for Top-4 root selection. Child
  traversal, beam pruning, and retained-path selection use deterministic ordinal
  path ordering and never compose uncalibrated continuation scores.
- Enforced beam width 6, depth 4, detached simulation transitions, and timeout
  retention of the deepest ordinal successful deterministic path observed so
  far.
- Unsupported stochastic actions terminate. An unavailable refresh terminates
  without applying a transition, producing a concrete refreshed state, adding
  expected value, inheriting an invented continuation score, or entering the
  completed-path candidates. Null actions and null/terminal transitions also
  fail closed without entering completed-path candidates.
- Limited production continuation to deterministic buy, sell, and upgrade
  actions. Production planning cannot replace the originally ranked root, and
  no public constructor exposes a root-replacement switch.
- Checked the budget before and after each root transition, expected-shop
  estimate, child ranking call, and child transition. These checks stop at all
  interruptible boundaries; a single synchronous provider, `Rank`, or `Apply`
  call cannot be preempted, so this implementation does not claim a strict wall
  clock ceiling while one such call is executing.
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
PASS linear ranker uses one deterministic score surface
VALIDATION machine=Ningbing os=Microsoft Windows NT 6.2.9200.0 processors=32 framework=4.0.30319.42000
LATENCY ranking P50=0.050ms P95=0.087ms P99=0.176ms
LATENCY planning P50=0.106ms P95=0.233ms P99=0.297ms
PASS beam planning is deterministic, bounded, and timeout-safe
```

The tests cover unavailable estimates, exact identity matching, fractional
coverage, score-independent continuation traversal, hard bounds, budget checks
around each interruptible call boundary, timeout retention, detached states,
fail-closed refresh/null/unsupported behavior, selective invocation,
diagnostic-only root selection, and the absence of a public replacement switch.

### Review Remediation TDD

The review-fix RED wave first proved that
`ExpectedShopEstimate.Unavailable("known-pool")` leaked the resolved identity:

```text
FAIL unavailable estimate never retains a resolved identity expected=unknown actual=known-pool
```

After the minimum identity fix, the next RED proved that continuation scores
were still controlling traversal:

```text
FAIL continuation traversal uses ordinal keys rather than uncalibrated scores
expected=root/child-a|...|root/child-f actual=root/child-h|...|root/child-c
```

The review RED outputs are retained outside the repository at
`$env:TEMP\bobcoach-task10-review-red.txt` and
`$env:TEMP\bobcoach-task10-review-red-planner.txt`. The final focused GREEN is
retained at `$env:TEMP\bobcoach-task10-review-green-final.txt`.

The ranking latency loop now executes the production `LinearPairwiseRanker`
over 16 feature vectors for all 1,000 decisions rather than timing a LINQ sort
over pre-scored `RankedAction` objects. It also prints the validation machine,
OS, processor count, and framework before reporting percentiles. The machine
name above is ASCII-normalized for the repository report; the harness prints
the host-provided value directly.

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
- Fresh full `npm test`: PASS in 225.6 seconds with exit code `0`; contract,
  behavior, release build, and offline package suites completed against the
  configured exact HDT `1.54.0.7376` baseline.
- Release DLL reproducibility: three builds produced 760832 bytes and SHA-256
  `72B165A4C626E8811766353F572A11C4E59345E1A20E1C1804435615DF8D9F6C`.
- Release compile contract: 193 items, SHA-256
  `4fc9305dc8783701d2334d0a6838762c7612e8b721cce5aa2dc901b9111bf807`.
- `git diff --check`: PASS, with only Git LF-to-CRLF notices.
- `tools/build/validate_repository.ps1`: scanned 427 Git tracked or untracked
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
   identity checks, score-independent continuation traversal, state detachment,
   bounds, interruptible-boundary timeout handling, stable tie-breaking,
   selective invocation, and diagnostic-only production integration use no
   next-season strategy facts.
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

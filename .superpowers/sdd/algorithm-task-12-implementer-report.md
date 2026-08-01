# Task 12: Engineering Gates And Effect-Acceptance Interface

## Status

Implementation and implementer self-review are complete. Focused contracts, the
full npm suite, an explicit Release build, repository validation, and whitespace
validation pass. A fresh review against a frozen Task 12 bundle is still
required; this report does not claim reviewer approval.

Effect acceptance remains exactly `blocked-by-season-data`. No model was
trained, generated, copied, embedded, or activated. Unified ranking remains in
Shadow and `legacy-v1.1.0` remains the player-visible recommendation and
fallback.

## Delivered

- Added a production-policy benchmark runner and external-root wrapper that
  generate paired, versioned baseline/candidate prediction evidence, latency
  samples, and exactly seven safety probes without gameplay recording.
- Required fresh detached states, production candidate generation, canonical
  production fingerprints, ordinal scenario ordering, duplicate rejection, and
  matching build/season/card-pool identities.
- Required candidate bytes to pass the model loader and the four-argument
  `DecisionPolicyFactory` path before normal benchmark evidence can be written.
- Added fail-closed evidence evaluation. Incomplete, synthetic, prior-season,
  untrusted, mismatched, or insufficient evidence returns
  `blocked-by-season-data` with `metrics: null`.
- Added metric calculation only for 300 or more real, trustworthy,
  version-matched expert-labeled scenarios with representative Shadow evidence
  and completed human severe-error review. The evaluator never emits an
  activation decision.
- Added synthetic engineering fixtures and focused negative tests for malformed
  identities, mismatched fingerprints, missing probes, non-finite latency,
  incomplete timeout paths, and evidence-gate failures.
- Documented external evidence layout, new-season maintenance, frozen-threshold
  evaluation, Shadow retention, and rollback procedures.
- Replaced three personal absolute paths in the historical Task 7 report with
  `<HDT_DIRECTORY>` so repository validation can pass without changing the
  historical commands or conclusions.

## RED And GREEN Evidence

The initial RED checks failed because the benchmark wrapper, C# runner,
evaluator, synthetic fixture, and focused tests did not exist. Subsequent
negative-contract additions failed before their corresponding fail-closed
validation was implemented. The final timeout-path RED cleared
`actualSelectedActionKey` and `actualTopActionKeys`; it passed incorrectly until
both runner and evaluator required a non-empty safe completed action with Top-1
equal to the selected action.

Final focused GREEN results:

- `tests/test_algorithm_benchmark_contract.ps1`: `PASS benchmark evidence is
  paired, deterministic, recorder-free, and versioned`.
- `node tests/test_algorithm_evaluation.js`: `PASS evaluator validates
  structure, reports blocked evidence, and fails closed on invalid inputs`.
- `node tests/test_offline_runtime_contract.js`: `PASS ranking stays Shadow with
  ordered local lifecycle and legacy-visible fallback`.
- `git diff --check`: exit code `0`.

## Full Verification

With `BOBCOACH_HDT_DIR` set to the external HDT 1.54.0 reference directory:

- `npm test`: exit code `0` in approximately 251 seconds. Contract, behavior,
  and package suites all passed.
- Explicit Release build: `BobCoach.dll` size `773632` bytes, SHA-256
  `F5230D7BD813DDB9401110AC51E58C32FA84AE389CD41D65D2B551F53287CAE1`.
- `tools/build/validate_repository.ps1`: passes after the Task 7 report path
  placeholders are applied.
- `git diff --check`: exit code `0`.

The first full-suite attempt lacked `BOBCOACH_HDT_DIR` and failed. A second
attempt was terminated by the command timeout. Only the final fresh run above is
treated as passing evidence.

## Implementer Self-Review

- Normal-mode paths are resolved beneath an explicit non-repository,
  non-system-drive data root; path escape is rejected.
- The runner emits exactly the six required fallback probes plus
  `planner-timeout`. Timeout requires `FallbackReason.None`,
  `PlannerStatus.TimedOut`, and a non-empty safe completed action path.
- Scenario/state/candidate identities are cross-validated and duplicate or mixed
  evidence fails closed.
- The evaluator suppresses metrics until all real-data prerequisites exist and
  does not authorize embedding, activation, or visible takeover.
- No model resource directory or activation switch was added.

## Review Risk

The normal runner currently supplies a runtime context with effect acceptance
set to true and uses `DecisionPolicyFactory.Create(..., runtimeContext)`, while
`RankingModelLoader.LoadForRuntime` also requires the candidate artifact itself
to declare `EffectAcceptanceStatus == "accepted"`. This may create a circular
future workflow in which an artifact must already claim effect acceptance before
the benchmark evidence used to decide effect acceptance can be generated.

The implementation preserves the explicit Task 12 contract that normal
benchmarking must use the four-argument production factory path. A fresh reviewer
must classify this risk. If it is Critical or Important, remediation is required
before Task 12 can be accepted; this report does not waive it.

## Required Conclusion

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

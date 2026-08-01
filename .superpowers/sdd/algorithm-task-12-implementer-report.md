# Task 12: Engineering Gates And Effect-Acceptance Interface

## Status

Implementation and implementer self-review are complete. Earlier full-suite,
Release-build, repository-validation, and whitespace evidence remains recorded
below. The original frozen review reported two Critical/four Important/one
Minor findings. Three subsequent fresh review waves reported, respectively, one
Critical/two Important/one Minor, zero Critical/two Important/zero Minor, and
zero Critical/two Important/zero Minor findings. Every reported finding has now
been repaired, but a new fresh review against the final frozen Task 12 bundle
is still required; this report does not claim reviewer approval.

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

## Resolved Review Risk

The circular workflow has been removed. Normal effect evaluation now uses the
internal `CreateForEffectEvaluation` path with an owner-approved, versioned,
frozen, checksum-bound protocol and matching calibration identities. The public
production factory still uses `LoadForRuntime`, requires an accepted artifact,
and remains the only player-visible composition path.

## Contract Repair Follow-up (2026-08-01)

### RED

- `tests/test_algorithm_linear_ranker.ps1` failed with two CS7036 errors: the
  new internal `CreateForEffectEvaluation` contract requires an explicit,
  four-argument `EffectEvaluationContext`, while the behavior test still used
  the removed three-argument form.
- `tests/test_offline_runtime_contract.js` failed because it still required
  legacy/unified `DecisionRuntimeMode` orchestration as benchmark evidence.
  That contradicts the isolated evaluation contract: candidate evidence must
  use the real Unified diagnostic surface, not an orchestrated visible-mode
  result. The benchmark contract was already green in the repaired workspace.
- After the reviewer added protocol cross-binding cases,
  `node tests/test_algorithm_evaluation.js` failed at line 213 because a protocol
  with a mutated `holdoutIdentity` still produced a non-null
  `evaluationProtocol`. Inspection also showed that the C# evaluation factory
  accepted any resolved benchmark identity instead of the one Task 12 benchmark.
- The first fresh linear-ranker invocation was blocked by the missing
  `BOBCOACH_HDT_DIR` shell variable. This was an environment precondition, not a
  code failure; verification was rerun with the installed HDT 1.54.0 reference.

### GREEN

- `AlgorithmLinearRankerBehavior.cs` now supplies a complete, checksum-bound,
  owner-approved frozen evaluation context. It proves a
  `blocked-by-season-data` non-synthetic artifact is composable only by the
  isolated evaluation factory, and rejects accepted, synthetic, unknown-model,
  protocol, candidate-checksum, candidate-model, season-extension, and
  calibration identity violations. It also proves the public production
  factory returns the `ModelUnavailable` fallback for the same pre-acceptance
  artifact.
- Benchmark/offline text contracts now require
  `CreateForEffectEvaluation`, `bundle.Legacy.Decide`, direct Unified decision
  diagnostics, and the plugin's hard-coded Shadow mode. They reject both
  `LegacyOnly` and `Unified` orchestration modes as candidate evidence, reject
  gameplay recording, reject a forged runtime context in the runner, and scope
  the strict `LoadForRuntime` assertion to the public production factory while
  allowing the internal evaluation factory's validated `Load` call.
- `DecisionPolicyFactory` now binds effect evaluation to
  `season-2026-next-v1`. The JavaScript evaluator binds the same benchmark and
  validates resolved holdout/training/season identities, accepted calibration,
  calibration-to-protocol/input/candidate identities, approval identities,
  positive margin scale, and all three confidence thresholds. The protocol hash
  remains the canonical SHA-256 of the protocol with only its own
  `protocolSha256` field removed.

Focused verification with `BOBCOACH_HDT_DIR` set to the HDT 1.54.0 reference:

- `powershell -File tests/test_algorithm_linear_ranker.ps1`: `PASS linear
  ranker uses one deterministic score surface`.
- `powershell -File tests/test_algorithm_benchmark_contract.ps1`: two Release
  builds produced identical `BobCoach.dll` files (`782336` bytes,
  SHA-256 `F167804D7034EE217A606B3D61EFA8C63F15171954CBD578AE63E78168F2DC6A`),
  followed by `PASS benchmark evidence is paired, deterministic, recorder-free,
  and versioned`.
- `node tests/test_offline_runtime_contract.js`: all four contracts passed,
  including `PASS ranking stays Shadow with ordered local lifecycle and
  legacy-visible fallback`.
- `node tests/test_algorithm_evaluation.js`: `PASS evaluator validates
  structure, reports blocked evidence, and fails closed on invalid inputs`.
- `git diff --check`: exit code 0.

### Follow-up Self-review

- The evaluation path remains internal and requires a non-synthetic,
  checksum-bound, owner-approved protocol and matching calibration identities.
- Public production composition still uses `LoadForRuntime` and remains
  fail-closed; no public acceptance or activation path was added.
- No model was trained, generated, copied, embedded, or activated. Unified
  remains Shadow and `legacy-v1.1.0` remains visible/fallback.

## Fresh-review Repair Wave (2026-08-01)

The original frozen Task 12 review reported two Critical, four Important, and
one Minor finding. The repair wave now:

- records the planner's real selected action key, ordered Top-N actions,
  best-complete path, and planner status in benchmark evidence;
- maps `model-unavailable` probes to `modelSource: none` and all other probes
  to `modelSource: candidate`;
- centralizes external-root/output validation in `path_contracts.psm1`; normal
  mode rejects repository/system-drive data roots, self-test output must remain
  outside the repository, and both modes reject reparse-point or junction
  escapes;
- replaces repository-local owner approval with an externally supplied,
  detached Ed25519 signature verified against the checked-in empty trust-root
  file; and
- replaces self-attested readiness booleans with external label-provenance,
  Shadow-comparison, and severe-error-review manifests that bind the exact
  scenario set, version identities, holdout, evidence files, model checksum,
  and frozen evaluation protocol checksum.

Fresh verification after the repair wave, with `BOBCOACH_HDT_DIR` set to the
external HDT 1.54.0 reference directory:

- `node tests/test_algorithm_evaluation.js`: pass.
- `node tests/test_algorithm_workspace_contract.js`: pass.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  tests/test_algorithm_benchmark_contract.ps1`: pass; Release `BobCoach.dll`
  SHA-256 `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
- `node tests/test_offline_runtime_contract.js`: pass.
- `npm test`: exit code 0 in approximately 311 seconds; contract, behavior,
  and package suites pass. The deterministic package SHA-256 is
  `E1D559D21221987A03E2DDE335D80D9DEC73B1A1D4D62CA50E7F7785827D59A9`.
- `git diff --check`: exit code 0 before this report-only update.

The readiness result remains `effectAcceptanceStatus:
blocked-by-season-data` with `metrics: null`. The repair wave did not add a
training, generation, copy, embedding, activation, or visible-policy path.

## Final Fresh-review Repair Wave (2026-08-01)

The latest fresh review found zero Critical and two Important issues: effect
metrics used the planner diagnostic first step instead of the production
`DecisionResult.SelectedAction`, and evidence writers reopened a checked output
directory by string path, leaving a junction/reparse replacement window.

### RED

- `node tests/test_algorithm_evaluation.js` failed with `invalid evidence:
  candidate planning path and selected action are inconsistent` when the
  planner diagnostic first step intentionally differed from production Top-1.
- `tests/test_algorithm_benchmark_contract.ps1` failed its secure-output
  contract before the runner used a locked evidence directory. The added
  concurrent rename/junction case demonstrated that a path-based writer could
  be redirected after validation.

### GREEN

- Benchmark prediction and safety evidence now always use
  `DecisionResult.SelectedAction`; `topActionKeys[0]` must equal that production
  action. `bestCompletePath` remains non-metric planner diagnostic evidence and
  may begin with a different action.
- Both the benchmark runner and evaluator route output through
  `SecureEvidenceDirectory`. It opens and rejects reparse points along the
  output chain, creates missing segments relative to retained parent handles,
  and retains every directory handle without delete sharing. It creates and
  flushes a unique staging file relative to the retained output-directory
  handle, then publishes relative to that same handle without replacing an
  existing target. Dynamic contracts cover hierarchy creation, rejected rename,
  first publication, rejected overwrite, and staging cleanup.
- Protocol-only numeric canonicalization now gives C# and JavaScript the same
  17-significant-digit scientific representation. The focused contracts prove
  equivalent `1`, `1.0`, and `1e0` spellings produce one checksum and reject
  integers outside JavaScript's safe range. Other evidence and signature
  canonicalization is unchanged.
- The path-boundary report now states the actual contract: normal mode rejects
  repository/system-drive data roots, while self-test output is required only
  to remain outside the repository; both reject reparse-point escapes.
- `node tests/test_algorithm_evaluation.js`: pass.
- Three consecutive `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  tests/test_algorithm_benchmark_contract.ps1` invocations passed; each pair of
  Release builds produced identical `BobCoach.dll` files (`784384` bytes,
  SHA-256 `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`).
- `node tests/test_algorithm_workspace_contract.js`: pass.
- `node tests/test_offline_runtime_contract.js`: all four contracts pass.
- `git diff --check`: exit code 0 before this report update.

Full-suite, explicit Release-build, and repository-validation evidence must be
refreshed after the final Task 12 and whole-branch reviews; this section does
not reuse the earlier repair wave's results for the current implementation.

The result remains `blocked-by-season-data` with no accepted/rejected effect
decision or formal improvement metric. Unified ranking remains Shadow and
`legacy-v1.1.0` remains player-visible and the fallback.

## Required Conclusion

“新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。”

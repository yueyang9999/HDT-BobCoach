# Task 12: Engineering Gates And Effect-Acceptance Interface

## Status

Implementation, implementer self-review, final Task 12 fresh review, final
whole-branch fresh review, and final engineering verification are complete.
The final Task 12 reviewer approved `cc3dc48..f0b9f88` with zero Critical, zero
Important, and zero Minor findings. The final whole-branch reviewer approved
`0070303..07f9d23` with zero Critical, zero Important, and two Minor findings.
The two Minor findings are documented in the final section below; neither
changes runtime routing or the effect-acceptance boundary.

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
  first publication, rejected overwrite, staging cleanup, and UTF-8 stdin
  content through the evaluator's PowerShell writer boundary.
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
- `npm test`: exit code 0 in 241 seconds; contract, behavior, and package suites
  pass. The deterministic package SHA-256 is
  `E1D559D21221987A03E2DDE335D80D9DEC73B1A1D4D62CA50E7F7785827D59A9`.
- Explicit Release build in a new external evidence directory: `BobCoach.dll`
  is `784384` bytes with SHA-256
  `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
- `tools/build/validate_repository.ps1`: passes with 448 tracked or untracked,
  non-ignored files scanned.
- `git diff --cached --check` and `git diff --check`: exit code 0 before this
  report update.

The result remains `blocked-by-season-data` with no accepted/rejected effect
decision or formal improvement metric. Unified ranking remains Shadow and
`legacy-v1.1.0` remains player-visible and the fallback.

## Drive-root Fresh-review Repair (2026-08-01)

The next fresh review reported zero Critical, one Important, and zero Minor
findings. `SecureEvidenceIo.Normalize` removed the trailing separator from a
volume root, converting an absolute root such as `E:\` into the drive-relative
path `E:`. A caller that declared `E:\` as its trusted root could therefore
publish into the drive's current working directory instead of the declared
absolute output hierarchy.

### RED

- `tests/test_algorithm_benchmark_contract.ps1` acquired a secure evidence
  directory beneath the repository's non-system volume root and failed with
  `secure evidence drive-root DataRoot did not preserve its declared absolute
  path`. The failing implementation created the uniquely named directory under
  the repository instead of at the declared volume-root location.

### GREEN

- `SecureEvidenceIo` now preserves a full path when it is exactly the volume
  root and builds containment prefixes without appending a duplicate separator.
- `path_contracts.psm1` uses the same root-preserving normalization for data,
  output, physical-parent, reparse traversal, and resolved-data paths. Its
  existing policy still rejects a `DataRoot` that contains the repository.
- Three consecutive benchmark contract invocations passed. Each invocation
  produced two identical Release `BobCoach.dll` files (`784384` bytes,
  SHA-256 `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`).
- `node tests/test_algorithm_evaluation.js`, `node
  tests/test_algorithm_workspace_contract.js`, and `node
  tests/test_offline_runtime_contract.js` passed.
- `git diff --check` passed before this report-only update.

The repair changes only absolute-path handling and engineering contracts. It
does not train, generate, copy, embed, or activate a model, and it does not
produce an accepted/rejected effect result, improvement percentage, or formal
effect metric. The Task 12 result remains `blocked-by-season-data`; Unified
ranking remains Shadow and `legacy-v1.1.0` remains player-visible and the
fallback.

## Unsafe-integer Fresh-review Repair (2026-08-01)

The remaining Minor review finding was an evidence gap: the production C# and
JavaScript protocol loaders rejected integers outside JavaScript's safe range,
but the focused contracts did not dynamically prove both production paths.

### RED

- With the JavaScript production safe-integer guard temporarily removed, the
  evaluator contract failed with `Missing expected exception: production
  protocol loader must reject integers outside the JavaScript safe range`.
- With the C# production safe-integer guard temporarily removed, the benchmark
  contract failed with `self-test did not dynamically prove
  protocolUnsafeIntegerRejected`.
- Both mutations were restored before the GREEN and full-suite runs.

### GREEN and final verification

- `tests/test_algorithm_evaluation.js` now passes an unsafe integer through the
  production `loadExternalProtocolContext` path and requires a fail-closed
  `not safely representable` error.
- `PolicyBenchmarkRunner --self-test` passes an unsafe integer through the
  production `CanonicalJson` path and reports
  `protocolUnsafeIntegerRejected: true`; the PowerShell benchmark contract
  requires that field.
- `node tests/test_algorithm_evaluation.js`, `node
  tests/test_algorithm_workspace_contract.js`, `node
  tests/test_offline_runtime_contract.js`, and
  `tests/test_algorithm_benchmark_contract.ps1` passed.
- `npm test`, with the external HDT 1.54.0.7376 reference supplied through the
  process-local `BOBCOACH_HDT_DIR`, exited 0 in 240 seconds. Its 337-line stdout
  log contains 138 `PASS` lines and no failure-pattern matches; stderr is empty.
  The logs are
  `E:\HDT-BobCoach-evidence\verification\task12-unsafe-integer-npm-test.stdout.log`
  and
  `E:\HDT-BobCoach-evidence\verification\task12-unsafe-integer-npm-test.stderr.log`.
- An independent Release build in
  `E:\HDT-BobCoach-evidence\builds\task12-unsafe-integer-20260801-1430`
  produced `BobCoach.dll` at `784384` bytes with SHA-256
  `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
- `tools/build/validate_repository.ps1` passed with 448 tracked or untracked,
  non-ignored files scanned. `git diff --check` and
  `git diff --cached --check` exited 0 before this report update.

This test-only repair does not alter runtime policy or season-data status. No
model was trained, generated, copied, embedded, or activated. Task 12 remains
`blocked-by-season-data`; Unified ranking remains Shadow and
`legacy-v1.1.0` remains player-visible and the fallback.

## Secure-input Fresh-review Repair (2026-08-01)

The latest fresh review reported zero Critical, one Important, and zero Minor
findings. The PowerShell wrapper validated benchmark inputs before build and
staging, but the compiled runner later reopened scenarios, probes, model, and
protocol by string path. A concurrent directory rename and junction replacement
could therefore change which objects the runner consumed. Scenario parsing and
holdout checksum calculation also reopened the scenario path separately.

### RED

- `tests/test_algorithm_benchmark_contract.ps1` failed with `Unable to find type
  [BobCoach.ModelTools.SecureInputSnapshot]` after adding a dynamic contract that
  captures an input, attempts to rename its containing directory and replace it
  with a junction, and then verifies the captured bytes remain the trusted
  content.

### GREEN

- `SecureInputSnapshot` opens the declared `DataRoot`, every input directory
  segment, and every input file through retained Windows handles. It rejects
  reparse points, verifies final physical containment, omits delete sharing on
  all handles, and omits write sharing on input files.
- The snapshot reads scenarios, probes, candidate model, and evaluation protocol
  exactly once. `PolicyBenchmarkRunner` parses scenarios and calculates the
  holdout checksum from the same `scenarioBytes`, parses the protocol and probes
  from their captured bytes, and loads the model through an in-memory stream.
  It no longer performs production input reads by string path.
- The wrapper now passes the validated `DataRoot` to the runner as
  `--data-root`. Static integration contracts require the secure snapshot and
  each byte-backed consumer so the runner cannot silently regress to path-based
  reads.
- Three consecutive benchmark contract invocations passed. Each invocation
  produced two identical Release `BobCoach.dll` files (`784384` bytes,
  SHA-256 `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`).
- `node tests/test_algorithm_evaluation.js`, `node
  tests/test_algorithm_workspace_contract.js`, and `node
  tests/test_offline_runtime_contract.js` passed. `git diff --check` passed
  before this report update.

This repair changes only benchmark input integrity and engineering contracts.
It does not train, generate, copy, embed, or activate a model, and it does not
produce an accepted/rejected effect result, improvement percentage, or formal
effect metric. Task 12 remains `blocked-by-season-data`; Unified ranking remains
Shadow and `legacy-v1.1.0` remains player-visible and the fallback.

### Fresh verification on `9359722`

- `tests/test_algorithm_benchmark_contract.ps1`,
  `tests/test_algorithm_evaluation.js`,
  `tests/test_algorithm_workspace_contract.js`, and
  `tests/test_offline_runtime_contract.js` all exited 0. Their stdout and stderr
  logs are under
  `E:\HDT-BobCoach-evidence\verification\task12-secure-snapshot-20260801-1445`;
  every stderr log is empty.
- Full `npm test` exited 0 in 239.3 seconds with 142 `PASS` lines and empty
  stderr. The only broad failure-pattern match was the successful summary
  `passed: 7 failed: 0`.
- The independent Release build at
  `E:\HDT-BobCoach-evidence\builds\task12-secure-snapshot-20260801-1450`
  produced `BobCoach.dll` at 784384 bytes with SHA-256
  `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
- `tools/build/validate_repository.ps1`, `git diff --check`, and
  `git diff --cached --check` all exited 0.
- Self-review found no Critical, Important, or Minor issue in the secure-input
  repair. Production benchmark inputs are captured once beneath the validated
  data root and all downstream parsers consume those captured bytes; the change
  does not modify production recommendation routing, effect acceptance, or
  model activation.

## Evaluator Secure-input Fresh-review Repair (2026-08-01)

The fresh review of `review-cc3dc48..9359722d.diff` (SHA-256
`1D89DA19E61C749C35801A99CEAAA3E43C3A78D80AC0CAA7F3A4FB5AB6DBEB41`)
reported zero Critical, one Important, and zero Minor findings. The JavaScript
evaluator validated scenarios, prediction evidence, latency/safety evidence,
protocol, approval, and provenance paths before reopening them with
`fs.readFileSync`. That left a reparse/directory-replacement window between
validation and consumption even though the C# benchmark runner already used a
retained-handle snapshot.

### RED

- `node tests/test_algorithm_evaluation.js` failed with `evaluator inputs must
  come from one retained-handle secure snapshot` before the evaluator had a
  secure read bridge or byte-backed input path.

### GREEN

- `secure_read_evidence.ps1` loads the existing `SecureEvidenceIo.cs` primitive,
  captures all named inputs through `SecureInputSnapshot.Capture`, and returns
  only the captured bytes as base64 JSON while the retained handles remain
  open.
- The evaluator now invokes that bridge once for the complete CLI input set.
  JSON/JSONL parsing, raw file SHA-256, canonical SHA-256, protocol binding, and
  evidence binding consume only the captured bytes. The exported protocol and
  evidence context loaders use the same secure snapshot boundary.
- The focused evaluator test executes the real bridge and requires the Node
  source to have no `fs.readFileSync(file)` external-input path. The benchmark
  contract continues to dynamically prove that the retained handles reject a
  concurrent directory rename/junction replacement.
- `node tests/test_algorithm_evaluation.js`,
  `tests/test_algorithm_benchmark_contract.ps1`,
  `node tests/test_algorithm_workspace_contract.js`, and
  `node tests/test_offline_runtime_contract.js` exited 0. The focused benchmark
  build produced `BobCoach.dll` at 784384 bytes with SHA-256
  `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
- `git diff --check` exited 0 before this report update.

This repair changes only evaluator input integrity and engineering contracts.
It does not train, generate, copy, embed, or activate a model and does not emit
accepted/rejected effect results, improvement percentages, or formal effect
metrics. Task 12 remains `blocked-by-season-data`; Unified ranking remains
Shadow and `legacy-v1.1.0` remains player-visible and the fallback.

## Final Exact-HEAD Verification (2026-08-01)

The final implementation commit `558d90eb2aaa3ff0d3c446cef285671c6e9d0bb6`
was verified from a clean worktree. An initial invocation pointed
`BOBCOACH_HDT_DIR` at the parent `build-reference` directory and exited with
`Missing HDT dependency: HearthstoneDeckTracker.exe`; it was discarded. The
complete verification was rerun with the installed HDT 1.54.0.7376 directory.

- `tests/test_algorithm_benchmark_contract.ps1`,
  `tests/test_algorithm_evaluation.js`,
  `tests/test_algorithm_workspace_contract.js`, and
  `tests/test_offline_runtime_contract.js` all exited 0 with empty stderr.
- Full `npm test` exited 0 in 264.4 seconds with 139 `PASS` lines and empty
  stderr.
- An independent Release build produced `BobCoach.dll` at 784384 bytes with
  SHA-256
  `8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
- `tools/build/validate_repository.ps1`, `git diff --check`, and
  `git diff --cached --check` all exited 0.
- Logs are under
  `E:\HDT-BobCoach-evidence\verification\task12-final-head-558d90e-20260801-1520`;
  Release evidence is under
  `E:\HDT-BobCoach-evidence\builds\task12-final-head-558d90e-20260801-1530`.

Final self-review found no Critical, Important, or Minor issue in the evaluator
secure-input repair. The CLI captures scenarios, baseline/candidate predictions,
latency, safety, protocol, approval, provenance, Shadow comparison, and severe
error review inputs in one retained-handle snapshot. Parsing and both raw and
canonical checksums consume only those captured bytes. The exported protocol
and evidence loaders use the same boundary. No recommendation-routing,
effect-acceptance, model-training, model-generation, model-copy, model-embedding,
or model-activation path changed.

Task 12 therefore remains `blocked-by-season-data` with `metrics: null`. Unified
ranking remains Shadow and `legacy-v1.1.0` remains player-visible and the
fallback.

## Required Conclusion

“新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。”

## Fresh-review Repair At `24cf00f` (2026-08-01)

The fresh review at `558d90eb2aaa3ff0d3c446cef285671c6e9d0bb6`
reported zero Critical, two Important, and zero Minor findings. The repair commit
`24cf00fcef4d6a06339dc4603eaff043f50b38fd` addresses both findings:

- trusted owner keys are now captured through the retained-handle secure input
  snapshot before parsing, so the signed owner-approval decision path is
  reachable without reopening the trust root by path;
- safety evidence schema v2 binds the actual selected and top-ranked action
  surfaces to the independently measured `legacy-v1.1.0` surfaces whenever a
  fallback occurs, and production safety probes use `CandidateBenchmarkAdapter`.

The repair also fixes the complete signed-evidence-chain contract to pass the
validated label map into metric calculation. The repository trust-root fixture
remains an empty keyset; the Ed25519 chain test creates temporary signing
material only for the duration of the test.

Focused verification at `24cf00f` passed:

- `node tests/test_algorithm_evaluation.js`;
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  tests/test_algorithm_benchmark_contract.ps1`;
- `git diff --check` and `git diff --cached --check` before the repair commit.

The benchmark contract built the Release DLL twice at 784384 bytes with SHA-256
`8DA71739DCC903F3A15150E33538B7153AB80344FBD2ECB9D9B4ED9008A16E7E`.
No fresh reviewer has reviewed `24cf00f` yet, and full `npm test`, offline
runtime, repository validation, an independent final Release build, and the
whole-branch review remain pending. Do not update `progress.md` until the fresh
Task 12 review has zero Critical and zero Important findings.

Task 12 remains `blocked-by-season-data` with no effect acceptance result or
formal improvement metric. Unified ranking remains Shadow and
`legacy-v1.1.0` remains player-visible and the fallback.

## Final Whole-Branch Review And Engineering Acceptance (2026-08-01)

The final Task 12 fixed package was
`.superpowers/sdd/review-cc3dc48..f0b9f88.diff`, 325815 bytes, SHA-256
`A57A3C4BF7C71D8C8F6C871D51047DDA7A6F47F9471DB0D4FEAFFBBACAD50C00`.
Its fresh reviewer reported Critical 0, Important 0, Minor 0, and `APPROVE`.
`progress.md` was then updated in the separate documentation commit `ce3fa4b`.

Whole-branch fresh review required two repair rounds after the first package:

- ranking-entrypoint failures are contained at the plugin boundary, with a
  plugin-level behavior contract proving exceptions do not escape and the
  player-visible result fails closed;
- `build_pairs.js` and `train_pairwise.js` now resolve input and output paths
  beneath `DataRoot` through the shared reparse-aware path contract, with real
  child-junction read/write escape regressions. Their maintainer commands now
  use `DataRoot`-relative paths.

The final frozen whole-branch package covers `0070303..07f9d23`:

- file: `.superpowers/sdd/review-0070303..07f9d23.diff`;
- size: 775344 bytes;
- SHA-256:
  `6358391297CFF01FF5962E1917870FB0F4E9ACF5FFF1A7C380A13DCC75330107`;
- fresh review: Critical 0, Important 0, Minor 2, `APPROVE`.

The two non-blocking Minor findings were handled as follows:

- this report was stale inside the frozen package; this final documentation
  update synchronizes its status after review and verification;
- `tests/test_algorithm_model_contract.ps1` retains an author-machine `E:`
  fallback when `BOBCOACH_HDT_DIR` is unset. It is recorded as a portability
  risk; final validation explicitly supplied the exact HDT 1.54.0.7376
  reference directory, and the approved code package was not changed again.

Final exact-code-HEAD verification ran on `07f9d23553eff8b83e9f9efd5bd114d2a5142f5b`:

- six focused commands exited 0: algorithm evaluation, workspace contract,
  offline runtime contract, benchmark contract, orchestrator, and plugin
  ranking entrypoint;
- full `npm test` exited 0 in approximately 309.5 seconds with 140 `PASS`
  lines and no `FAIL` lines;
- the independent offline runtime contract exited 0;
- the independent Release build exited 0 and produced `BobCoach.dll` at
  784896 bytes with SHA-256
  `8BF904C1CCC3B9CF6C2297005410E833987354AFA0E6968B395654B3B9C4BE56`;
- repository validation exited 0 after scanning 451 tracked or untracked,
  non-ignored files;
- `git diff --check` and `git diff --cached --check` exited 0.

Verification logs are under
`E:\HDT-BobCoach-evidence\verification\whole-final-07f9d23-20260801-185035`;
the independent build is under
`E:\HDT-BobCoach-evidence\builds\whole-final-07f9d23-20260801-185035`.
The full test suite left one temporary directory on `C:` containing 1147 files,
391 directories, and 88959304 bytes. It is recorded for owner-approved cleanup
and was not deleted during this task.

No model was trained, generated, copied, embedded, or activated. Synthetic
evidence produced no effect acceptance decision, improvement percentage, or
formal effect metric. Without an owner-approved, versioned, frozen protocol
bound to a checksum, the evaluator does not return an accepted or rejected
effect result. Task 12 remains `blocked-by-season-data`, Unified remains Shadow,
and `legacy-v1.1.0` remains player-visible and the fallback.

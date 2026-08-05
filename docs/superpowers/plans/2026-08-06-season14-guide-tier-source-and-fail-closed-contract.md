# Season14 Guide Tier Source and Fail-Closed Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the first algorithm-iteration round by adding explicit fail-closed contract tests for `GameState.CurrentCompGuideTier`, proving loaded-catalog decisions are identical to empty-catalog when tier data is absent, then running all directed/full acceptance checks and recording evidence.

**Architecture:** The production decision path is already implemented in `DecisionEngine.CompGuideWeightBonus`, which consumes `CompGuideCatalog.TryGetWeight` and `TryGetTierWeight`. This round adds contract regression coverage without changing the decision algorithm, then verifies repository, directed tests, npm suite, release validation, and policy benchmark self-test.

**Tech Stack:** C# 7.3/.NET Framework 4.7.2 harness compiled with Roslyn; PowerShell test runners; Node.js validation tools.

## Global Constraints

- Continue on `codex/season14-algorithm-iteration`; do not create a sub-branch for this small contract/evidence round.
- Do not push, rebase, `reset --hard`, force push, delete files, or alter git history.
- Do not modify `.env`, keys, CI/CD, database schema, or install global dependencies.
- Do not read/write `%APPDATA%\bob-coach\data\`; do not add `BGDUO` data.
- Keep all evidence under `E:\HDT-BobCoach-evidence\season14\`.
- Do not resolve `unresolvedOfficialRows=10` as completed; it is not part of this round.
- Decision paths were changed in HEAD `11af795`, so policy benchmark/self-test evidence is required.
- All success claims require fresh command output, not prior logs.

---

### Task 1: Add Explicit Guide Tier Fail-Closed Contract Tests

**Files:**
- Modify: `E:\HDT-BobCoach\tests\AlgorithmGuideWeightBehavior.cs`

**Interfaces:**
- Consumes: existing `GameState.CurrentCompGuideTier`, `DecisionEngine.GetShopCardScores`, `DecisionEngine.GetRecommendedBuyIndices`, `DecisionEngine.GetBestAction`, and the existing test helpers.
- Produces: two new public-less test methods and two new call sites in `RunBehavior()`.

- [ ] **Step 1: Add the failing test calls**

In `RunBehavior()`, add:

```csharp
        AssertDefaultGuideTierIsNull();
        AssertLoadedCatalogNullTierFailClosedAcrossDecisionPaths(
            emptyCatalog, loadedCatalog);
```

Place these after `AssertKnownWeightConsumedByBestAction(emptyCatalog, loadedCatalog);`.

- [ ] **Step 2: Run the directed test and verify it fails on missing methods**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_guide_weight.ps1
```

Expected: FAIL because `AssertDefaultGuideTierIsNull` and `AssertLoadedCatalogNullTierFailClosedAcrossDecisionPaths` are not defined.

- [ ] **Step 3: Add the new assertion methods**

Add these methods before `CreateEmptyCatalog()`:

```csharp
    private static void AssertDefaultGuideTierIsNull()
    {
        var state = new GameState();
        AssertTrue(
            state.CurrentCompGuideTier == null,
            "new GameState must default CurrentCompGuideTier to null");
    }

    private static void AssertLoadedCatalogNullTierFailClosedAcrossDecisionPaths(
        object emptyCatalog, object loadedCatalog)
    {
        var emptyScores = Score(NewEngine(emptyCatalog), "SYNTH_CARD", null);
        var loadedScores = Score(NewEngine(loadedCatalog), "SYNTH_CARD", null);
        AssertNear(emptyScores, loadedScores, 0.0001,
            "loaded catalog with null tier must not change shop card score");

        var emptyBuy = NewEngine(emptyCatalog)
            .GetRecommendedBuyIndices(BuyState(), 1);
        var loadedBuy = NewEngine(loadedCatalog)
            .GetRecommendedBuyIndices(BuyState(), 1);
        AssertEqual(emptyBuy.Count, loadedBuy.Count,
            "loaded catalog with null tier must not change buy index count");
        for (var i = 0; i < emptyBuy.Count; i++)
        {
            AssertEqual(emptyBuy[i], loadedBuy[i],
                "loaded catalog with null tier must not change buy index " + i);
        }

        var emptyAction = NewEngine(emptyCatalog).GetBestAction(BestActionState());
        var loadedAction = NewEngine(loadedCatalog).GetBestAction(BestActionState());
        AssertEqual(emptyAction.Type, loadedAction.Type,
            "loaded catalog with null tier must not change best action type");
        AssertEqual(emptyAction.TargetIndex, loadedAction.TargetIndex,
            "loaded catalog with null tier must not change best action target");
    }
```

The score helper passes a null tier explicitly. For buy indices and best action, call `SetTier(state, null)` on fresh states returned by `BuyState()` and `BestActionState()` before invoking the engine, because those helpers normally set `T0`.

- [ ] **Step 4: Run the directed test and verify it passes**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_algorithm_guide_weight.ps1
```

Expected: `PASS algorithm guide weight behavior assertions=N` with `N` higher than before.

- [ ] **Step 5: Commit**

```powershell
git add tests/AlgorithmGuideWeightBehavior.cs
git commit -m "test(season14): lock null guide tier fail-closed contract"
```

### Task 2: Run Directed, Full, and Policy Evidence Verification

**Files:**
- Create evidence logs under `E:\HDT-BobCoach-evidence\season14\generated\`.

- [ ] **Step 1: Run the six directed validations**

Run:

```powershell
node "E:\HDT-BobCoach-evidence\season14\tools\validate_season14_guides.js" | Tee-Object "E:\HDT-BobCoach-evidence\season14\generated\t_season14_directed_2026-08-06.log"
node "E:\HDT-BobCoach-evidence\season14\tools\validate_season14_registries.js" | Tee-Object -Append "E:\HDT-BobCoach-evidence\season14\generated\t_season14_directed_2026-08-06.log"
node "E:\HDT-BobCoach-evidence\season14\tools\validate_season14_rule_catalog.js" | Tee-Object -Append "E:\HDT-BobCoach-evidence\season14\generated\t_season14_directed_2026-08-06.log"
node tests\test_season14_guide_fetch.js | Tee-Object -Append "E:\HDT-BobCoach-evidence\season14\generated\t_season14_directed_2026-08-06.log"
node tests\test_season14_guide_sync.js | Tee-Object -Append "E:\HDT-BobCoach-evidence\season14\generated\t_season14_directed_2026-08-06.log"
node tests\test_season14_guide_weights.js | Tee-Object -Append "E:\HDT-BobCoach-evidence\season14\generated\t_season14_directed_2026-08-06.log"
```

Expected: every command exits `0`; log contains all six `PASS`/`ALL_PASS` lines.

- [ ] **Step 2: Run `npm test` with HDT environment**

Run:

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker'
npm test 2>&1 | Tee-Object "E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_npm_test_2026-08-06.log"
if ($LASTEXITCODE -ne 0) { throw "npm test failed" }
```

Expected: exit `0`, no failed suites.

- [ ] **Step 3: Run repository validation and whitespace check**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\build\validate_repository.ps1 2>&1 | Tee-Object "E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_validate_repo_2026-08-06.log"
if ($LASTEXITCODE -ne 0) { throw "validate_repository failed" }
git diff --check
```

Expected: `validate_repository.ps1` exits `0`; `git diff --check` exits `0`.

- [ ] **Step 4: Run policy benchmark self-test and evaluator contract test**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\model\run_policy_benchmark.ps1 -SelfTest -OutputDirectory E:\HDT-BobCoach-evidence\season14\generated\benchmark-self-test 2>&1 | Tee-Object "E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_benchmark_2026-08-06.log"
if ($LASTEXITCODE -ne 0) { throw "policy benchmark self-test failed" }
node tests\test_algorithm_evaluation.js 2>&1 | Tee-Object -Append "E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_benchmark_2026-08-06.log"
```

Expected: self-test writes deterministic paired evidence and exits `0`; evaluator contract passes and reports `blocked-by-season-data` for synthetic/absent real data.

- [ ] **Step 5: Record the no-arg evaluator limitation**

Run:

```powershell
node tools\model\evaluate_policy.js 2>&1 | Tee-Object "E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_evaluate_policy_2026-08-06.log"
```

Expected: exits non-zero with a usage/argument error. Record this as `blocked-by-season-data`; do not claim an effect metric.

### Task 3: Evidence Log and Handover Update

**Files:**
- Create: `E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_round1_2026-08-06.log`
- Create: `G:\software\Codex\Handover document\BobCoach_Season14_单人模式_T6_算法迭代第一轮_2026-08-06.md`

- [ ] **Step 1: Write round summary evidence**

Write a short summary to `E:\HDT-BobCoach-evidence\season14\generated\t_algorithm_round1_2026-08-06.log` containing:

```text
HEAD=<full commit hash>
working_tree=clean
directed_checks=6/6 pass
npm_test=pass
validate_repository=pass
git_diff_check=pass
policy_benchmark_self_test=pass
evaluate_policy_contract=pass
evaluate_policy_real_effect=blocked-by-season-data
unresolvedOfficialRows=not-resolved
```

- [ ] **Step 2: Write the T6 handover document**

Create `G:\software\Codex\Handover document\BobCoach_Season14_单人模式_T6_算法迭代第一轮_2026-08-06.md` documenting:

- Current branch and HEAD.
- Implemented consumption points and fail-closed rules.
- Test coverage added.
- Directed/full/policy verification evidence paths and exit status.
- `evaluate_policy.js` real-effect limitation.
- Remaining risks: `GameStateExtractor` does not yet set `CurrentCompGuideTier`; `unresolvedOfficialRows=10` remains open; CI/CD/plugin network fetch/release remain out of scope.

- [ ] **Step 3: Commit the plan, evidence-adjacent docs, and handover summary**

Only commit repository files. Evidence under `E:\HDT-BobCoach-evidence\` is not inside the repository and is referenced by path, not committed.

```powershell
git add docs/superpowers/plans/2026-08-06-season14-guide-tier-source-and-fail-closed-contract.md
git commit -m "docs(season14): record first algorithm iteration round"
```

## Self-Review

**Spec coverage:**

- First-priority consumption path: already implemented at HEAD and verified by Task 2/1.
- Fail-closed on unloaded catalog, unknown card, unknown tier, missing raw weight, null tier: covered by existing tests plus new loaded-null-tier contract.
- Default `CurrentCompGuideTier` null contract: added in Task 1.
- Directed six checks: Task 2 Step 1.
- `npm test`, `validate_repository.ps1`, `git diff --check`: Task 2 Steps 2-3.
- Decision-path benchmark/evaluator evidence: Task 2 Steps 4-5.
- `unresolvedOfficialRows=10` not marked resolved: handover records it as not resolved.

**Placeholder scan:** no TBD/TODO placeholders; all steps contain exact commands and code.

**Type consistency:** uses only existing public `GameState` fields, existing `DecisionEngine` methods, and existing private helper names in `AlgorithmGuideWeightBehavior.cs`.

# BobCoach Next-Season Algorithm Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local, deterministic state-action ranking pipeline that reduces severe recruit-phase decision errors while retaining the frozen v1.1.0 policy as the visible baseline and runtime fallback until every activation gate passes.

**Architecture:** Add an orchestration boundary around the existing `DecisionEngine`, then introduce stable candidate descriptors, named state-action features, a checksum-validated linear pairwise model, conservative confidence/OOD gating, and selective deterministic beam planning. Training, labeling, and acceptance reporting run offline against an explicit external data root; plugin inference remains embedded, offline, and deterministic.

**Tech Stack:** .NET Framework 4.7.2, C# 7.3, old-style MSBuild project files, Newtonsoft.Json, SHA-256 from `System.Security.Cryptography`, Node.js standard library only, PowerShell test harnesses, x64 HDT plugin runtime.

## Global Constraints

- Preserve v1.1.0 as policy ID `legacy-v1.1.0`, golden benchmark baseline, visible shadow-mode recommendation, and runtime fallback until all activation gates pass.
- Do not add cloud inference, network requirements, paid APIs, Python, native ML runtimes, or new npm/NuGet packages.
- Keep plugin inference local, deterministic, offline-capable, and free.
- Compile with .NET Framework 4.7.2 and C# 7.3; add every production `.cs` file explicitly to `src/BobCoach/BobCoach.csproj`.
- Before creating new directories, update `AGENTS.md` with purpose, naming, data, and cleanup rules.
- Commit only synthetic, minimal fixtures. Raw replays, labels, datasets, candidate models, benchmark reports, and validation evidence stay outside the repository.
- Every model/data tool requires an explicit `--data-root` outside the repository; it must reject the repository root and must not default to the system drive.
- Keep player preference out of strategic feature extraction and ranking labels.
- Hard constraints may encode legality and tested invariants only; strategy heuristics remain soft evidence.
- Unknown entities and unsupported mechanics lower confidence and may force fallback; they never receive full confidence through neutral defaults.
- Do not deepen or reactivate the current randomized MCTS path; selective deterministic beam planning is the only new lookahead path.
- Normal ranking P95 must be at most 10 ms; planned decisions P95 must be at most 25 ms.
- Visible activation requires: at least 20% relative severe-error reduction, at least +10 percentage points Top-1 accuracy, at least +15 percentage points Top-3 coverage, no high-impact category regression worse than 5 percentage points, and 100% critical fallback tests.
- Any failed gate leaves the unified policy in shadow mode; aggregate improvement cannot hide a new severe error.

## File And Ownership Map

| Path | Responsibility |
| --- | --- |
| `src/BobCoach/Core/Decision/Contracts.cs` | Stable decision result, policy, confidence, planning, and explanation contracts |
| `src/BobCoach/Core/Decision/LegacyDecisionPolicy.cs` | Frozen adapter around v1.1.0 `IDecisionProvider` behavior |
| `src/BobCoach/Core/Decision/ActionDescriptor.cs` | Stable action identity and simulator-support metadata |
| `src/BobCoach/Core/Decision/CandidateGenerator.cs` | Legal action enumeration without strategic prioritization |
| `src/BobCoach/Core/Decision/HardConstraintFilter.cs` | Legality and invariant-only filtering with reason codes |
| `src/BobCoach/Core/Decision/DecisionEventRecord.cs` | Versioned event-alignment record and state/candidate fingerprints |
| `src/BobCoach/Core/Decision/DecisionEventRecorder.cs` | Atomic JSONL append, deduplication, and later event attachment |
| `src/BobCoach/Core/Decision/DecisionLifecycleTracker.cs` | Per-game decision IDs, unchanged-frame reuse, and post-action generation boundaries |
| `src/BobCoach/Core/Decision/DecisionOrchestrator.cs` | Legacy/unified execution, shadow selection, fallback, and comparison |
| `src/BobCoach/Core/Decision/DecisionPolicyFactory.cs` | Single composition root shared by the plugin and offline benchmark runner |
| `src/BobCoach/Core/Decision/ActionFeatureSchema.cs` | Ordered names, schema version, group, direction, and normalization contract |
| `src/BobCoach/Core/Decision/ActionFeatureExtractor.cs` | Cross-season and season-extension state-action features |
| `src/BobCoach/Core/Decision/RankingModel.cs` | Model envelope/payload DTOs, validation, and embedded-resource loading |
| `src/BobCoach/Core/Decision/LinearPairwiseRanker.cs` | Deterministic normalized dot-product ranking and decomposition |
| `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs` | Candidate simulation, ranking, confidence, optional planning, explanation |
| `src/BobCoach/Core/Decision/DecisionConfidenceEvaluator.cs` | Margin, completeness, semantic coverage, distribution distance, fallback |
| `src/BobCoach/Core/Decision/ExpectedShopEstimator.cs` | Deterministic expected-refresh feature estimates |
| `src/BobCoach/Core/Decision/BeamPlanner.cs` | Selective Top-4, width-6, depth-4 deterministic planning |
| `src/BobCoach/Core/Decision/ExplanationBuilder.cs` | Player-facing text derived only from score contributions/planner status |
| `src/BobCoach/Resources/DecisionModels/linear-v1.envelope.json` | Approved embedded model envelope only |
| `tests/fixtures/algorithm/` | Synthetic schema, model, event, and scenario fixtures only |
| `tests/Algorithm*.cs` and `tests/test_algorithm_*.ps1` | Focused C# behavior harnesses compiled by PowerShell |
| `tools/model/contracts.js` | Shared Node validation and canonical fingerprint helpers |
| `tools/model/PolicyBenchmarkRunner.cs` | Offline C# runner for same-state Legacy/unified prediction and latency evidence |
| `tools/model/run_policy_benchmark.ps1` | Compiles and runs the benchmark harness without recording gameplay data |
| `tools/model/build_pairs.js` | Converts explicit graded labels into defensible pairwise rows |
| `tools/model/train_pairwise.js` | Regularized pairwise logistic trainer and model-envelope writer |
| `tools/model/evaluate_policy.js` | Scenario metrics, category regressions, latency, and activation decision |
| `tools/model/split_dataset.js` | Deduplication and whole-game/time-aware dataset split |

## Execution Schedule

| Phase | Tasks | Effective solo-development days | Exit condition |
| --- | --- | ---: | --- |
| P0: benchmark and contracts | 1-2 | 3 | Frozen legacy adapter, synthetic golden contract, external-data rules |
| P1: pipeline isolation | 3-5 | 4-5 | Stable candidates, aligned events, shadow orchestration |
| P2: unified ranking | 6-8 | 6-7 | Immutable simulation, 45 named features, validated model, linear ranker |
| P3: planning and confidence | 9-11 | 4-5 | Conservative fallback, deterministic beam planner, shadow plugin integration |
| P4: calibration and acceptance | 12 | 3-5 | At least 300 scenarios, category report, latency report, activation decision |
| Contingency | applies after Tasks 6, 10, or 12 | 0-5 | New-season semantic gaps and simulator corrections resolved with tests |

The total estimate remains 20-30 effective personal-development days. This sequence is intentionally not compressed into one calendar week because benchmark quality, event alignment, and current-season semantic coverage are prerequisites for a trustworthy improvement claim.

---

### Task 1: Establish Directory Rules And Freeze The Benchmark Contract

**Files:**
- Modify: `AGENTS.md`
- Create: `tests/fixtures/algorithm/scenario-schema-v1.json`
- Create: `tests/fixtures/algorithm/v1.1.0-golden.synthetic.json`
- Create: `tools/model/contracts.js`
- Create: `tests/test_algorithm_workspace_contract.js`
- Modify: `tests/run_contract_tests.ps1`

**Interfaces:**
- Consumes: repository directory/public-content rules and `GameAction` fields from `Core/ActionEnumerator.cs`.
- Produces: `scenarioSchemaVersion = 1`, stable action key format `Type|TargetIndex|CardId|PurchaseSource`, `assertExternalDataRoot(root, repositoryRoot)`, and a frozen synthetic baseline fixture used by Tasks 4, 8, and 12.

- [ ] **Step 1: Add the failing workspace contract test**

```javascript
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const repo = path.resolve(__dirname, '..');
const agents = fs.readFileSync(path.join(repo, 'AGENTS.md'), 'utf8');
for (const required of [
  'src/BobCoach/Core/Decision/',
  'src/BobCoach/Resources/DecisionModels/',
  'tests/fixtures/algorithm/',
  'tools/model/',
  'explicit external data root'
]) assert(agents.includes(required), `missing directory rule: ${required}`);

const { assertExternalDataRoot, actionKey } = require('../tools/model/contracts');
assert.throws(() => assertExternalDataRoot(repo, repo), /outside the repository/);
assert.throws(() => assertExternalDataRoot(path.join(process.env.SystemDrive, 'bobcoach-algorithm'), repo), /system drive/);
assert.strictEqual(actionKey({ type: 'Upgrade', targetIndex: -1, cardId: '', purchaseSource: '' }),
  'Upgrade|-1||');
console.log('PASS algorithm workspace and stable action contracts');
```

- [ ] **Step 2: Run the test and verify the contract is absent**

Run: `node tests/test_algorithm_workspace_contract.js`

Expected: FAIL because `tools/model/contracts.js` does not exist or the new directory rules are absent.

- [ ] **Step 3: Add directory, privacy, and cleanup rules before creating directories**

Append under `## Directory Contract` in `AGENTS.md`:

```markdown
- `src/BobCoach/Core/Decision/`: new decision contracts, ranking, confidence, planning, fallback, and explanation runtime code.
- `src/BobCoach/Resources/DecisionModels/`: checksum-validated approved models embedded into the plugin; candidate and rejected models are prohibited.
- `tests/fixtures/algorithm/`: synthetic, minimal algorithm schemas and fixtures only; no replay-derived states or labels.
- `tools/model/`: deterministic offline model tooling: Node.js standard-library-only data preparation/training/evaluation plus the C# 7.3 policy benchmark harness compiled by its PowerShell runner; no downloaded runtime or package dependencies.
```

Append under `## Cleanup`:

```markdown
- Algorithm tools require an explicit external data root outside the repository. Raw replays, labels, datasets, candidate models, acceptance reports, and validation evidence are never written to the repository and must be removable without changing source.
```

- [ ] **Step 4: Add shared Node contracts and synthetic golden fixtures**

```javascript
// tools/model/contracts.js
'use strict';
const path = require('path');
function assertExternalDataRoot(root, repositoryRoot) {
  if (!root) throw new Error('--data-root is required');
  const resolved = path.resolve(root);
  const repo = path.resolve(repositoryRoot);
  const relative = path.relative(repo, resolved);
  if (relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..'))
    throw new Error('data root must be outside the repository');
  const systemRoot = path.parse((process.env.SystemDrive || '') + path.sep).root;
  if (systemRoot && path.parse(resolved).root.toLowerCase() === systemRoot.toLowerCase())
    throw new Error('data root must not be on the system drive');
  return resolved;
}
function actionKey(action) {
  return [action.type, action.targetIndex, action.cardId || '', action.purchaseSource || ''].join('|');
}
module.exports = { assertExternalDataRoot, actionKey, scenarioSchemaVersion: 1 };
```

```json
{
  "scenarioSchemaVersion": 1,
  "required": ["scenarioId", "gameId", "capturedAtUtc", "category", "state", "candidates", "actionLabels", "acceptedActionKeys", "severity"],
  "categories": ["level", "refresh", "buy", "sell", "freeze", "play", "transition"],
  "severities": ["normal", "high-impact", "severe"],
  "actionGrades": ["preferred", "acceptable", "weak", "severe-error", "not-evaluable"]
}
```

```json
{
  "benchmarkId": "v1.1.0-golden-synthetic-v1",
  "policyId": "legacy-v1.1.0",
  "scenarioSchemaVersion": 1,
  "scenarios": [
    {
      "scenarioId": "synthetic-upgrade-affordable",
      "gameId": "synthetic-game-1",
      "capturedAtUtc": "2026-07-31T00:00:00Z",
      "category": "level",
      "state": { "gameActive": true, "turn": 2, "gold": 5, "tavernTier": 1, "tavernUpgradeCost": 5 },
      "candidates": [{ "type": "Upgrade", "targetIndex": -1, "cardId": "", "purchaseSource": "" }],
      "actionLabels": [{ "actionKey": "Upgrade|-1||", "grade": "preferred" }],
      "acceptedActionKeys": ["Upgrade|-1||"],
      "severity": "high-impact",
      "legacyActionKey": "Upgrade|-1||"
    }
  ]
}
```

- [ ] **Step 5: Register and run the contract test**

Add `test_algorithm_workspace_contract.js` to the Node list in `tests/run_contract_tests.ps1`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_contract_tests.ps1`

Expected: PASS, including `PASS algorithm workspace and stable action contracts`.

- [ ] **Step 6: Commit the contract boundary**

```powershell
git add AGENTS.md tests/fixtures/algorithm/scenario-schema-v1.json tests/fixtures/algorithm/v1.1.0-golden.synthetic.json tools/model/contracts.js tests/test_algorithm_workspace_contract.js tests/run_contract_tests.ps1
git commit -m "test: freeze algorithm workspace and baseline contracts"
```

---

### Task 2: Introduce Decision Contracts And The Frozen Legacy Policy

**Files:**
- Create: `src/BobCoach/Core/Decision/Contracts.cs`
- Create: `src/BobCoach/Core/Decision/LegacyDecisionPolicy.cs`
- Create: `tests/AlgorithmPolicyContractBehavior.cs`
- Create: `tests/test_algorithm_policy_contract.ps1`
- Modify: `tests/run_behavior_tests.ps1`
- Modify: `src/BobCoach/BobCoach.csproj`

**Interfaces:**
- Consumes: `IDecisionProvider.GetBestAction(GameState)`, `GameAction`, and `GameState`.
- Produces: `IDecisionPolicy.Decide(GameState)`, `DecisionResult`, `RankedAction`, `DecisionFactor`, `PlannerStatus`, `FallbackReason`, `LegacyDecisionPolicy`, and `UnavailableDecisionPolicy.For(FallbackReason)` for all later runtime tasks.

- [ ] **Step 1: Write the failing policy contract harness**

```csharp
var expected = new GameAction { Type = ActionType.Refresh, TargetIndex = -1 };
var policy = new LegacyDecisionPolicy(new FakeProvider(expected));
var result = policy.Decide(new GameState { GameActive = true });
AssertEqual("legacy-v1.1.0", policy.PolicyId, "policy id");
AssertSame(expected, result.SelectedAction, "selected action");
AssertEqual(1, result.RankedActions.Count, "ranked count");
AssertEqual(FallbackReason.None, result.FallbackReason, "fallback");
AssertEqual(PlannerStatus.NotRequested, result.PlannerStatus, "planner status");

private sealed class FakeProvider : IDecisionProvider
{
    private readonly GameAction _action;
    public FakeProvider(GameAction action) { _action = action; }
    public GameAction GetBestAction(GameState state) { return _action; }
    public List<int> GetRecommendedBuyIndices(GameState state, int topN = 3) { return new List<int>(); }
    public LevelUpSuggestion GetLevelUpSuggestion(GameState state) { return new LevelUpSuggestion("", ""); }
}
```

- [ ] **Step 2: Run the harness and verify compilation fails**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_policy_contract.ps1`

Expected: compiler errors for missing `LegacyDecisionPolicy`, `DecisionResult`, and related enums.

- [ ] **Step 3: Implement the stable contracts**

```csharp
namespace BobCoach.Engine.Decision
{
    public enum PlannerStatus { NotRequested, Completed, TimedOut, Unsupported }
    public enum FallbackReason { None, ModelUnavailable, CriticalStateMissing, UnsupportedMechanic, OutOfDistribution, LowConfidence, NoSafeAction }

    public sealed class DecisionFactor
    {
        public string FeatureName { get; private set; }
        public double Contribution { get; private set; }
        public DecisionFactor(string featureName, double contribution)
        { FeatureName = featureName ?? ""; Contribution = contribution; }
    }

    public sealed class RankedAction
    {
        public GameAction Action { get; private set; }
        public string ActionKey { get; private set; }
        public double Score { get; private set; }
        public IList<DecisionFactor> Factors { get; private set; }
        public RankedAction(GameAction action, string actionKey, double score, IList<DecisionFactor> factors)
        { Action = action; ActionKey = actionKey; Score = score; Factors = factors ?? new List<DecisionFactor>(); }
    }

    public sealed class DecisionResult
    {
        public string PolicyId { get; private set; }
        public GameAction SelectedAction { get; private set; }
        public IList<RankedAction> RankedActions { get; private set; }
        public double Confidence { get; private set; }
        public PlannerStatus PlannerStatus { get; private set; }
        public FallbackReason FallbackReason { get; private set; }
        public string Explanation { get; private set; }
        public DecisionResult(string policyId, GameAction selectedAction, IList<RankedAction> rankedActions,
            double confidence, PlannerStatus plannerStatus, FallbackReason fallbackReason, string explanation)
        {
            PolicyId = policyId ?? ""; SelectedAction = selectedAction;
            RankedActions = rankedActions ?? new List<RankedAction>(); Confidence = confidence;
            PlannerStatus = plannerStatus; FallbackReason = fallbackReason; Explanation = explanation ?? "";
        }
    }

    public interface IDecisionPolicy
    {
        string PolicyId { get; }
        DecisionResult Decide(GameState state);
    }
}
```

- [ ] **Step 4: Implement the legacy adapter without changing legacy behavior**

```csharp
public sealed class LegacyDecisionPolicy : IDecisionPolicy
{
    private readonly IDecisionProvider _provider;
    public string PolicyId { get { return "legacy-v1.1.0"; } }
    public LegacyDecisionPolicy(IDecisionProvider provider) { _provider = provider; }
    public DecisionResult Decide(GameState state)
    {
        var action = _provider.GetBestAction(state);
        var ranked = new List<RankedAction>();
        if (action != null) ranked.Add(new RankedAction(action, CreateLegacyActionKey(action), 0d, null));
        return new DecisionResult(PolicyId, action, ranked, 1d, PlannerStatus.NotRequested,
            FallbackReason.None, action == null ? "" : action.Description);
    }
    private static string CreateLegacyActionKey(GameAction action)
    {
        return string.Join("|", action.Type, action.TargetIndex,
            action.CardId ?? "", action.PurchaseSource ?? "");
    }
}

public sealed class UnavailableDecisionPolicy : IDecisionPolicy
{
    private readonly FallbackReason _reason;
    public string PolicyId { get { return "unavailable"; } }

    private UnavailableDecisionPolicy(FallbackReason reason)
    {
        if (reason == FallbackReason.None) throw new ArgumentException("reason");
        _reason = reason;
    }

    public static UnavailableDecisionPolicy For(FallbackReason reason)
    { return new UnavailableDecisionPolicy(reason); }

    public DecisionResult Decide(GameState state)
    {
        return new DecisionResult(PolicyId, null, new List<RankedAction>(), 0d,
            PlannerStatus.NotRequested, _reason, "");
    }
}
```

Task 3 replaces `CreateLegacyActionKey(action)` with `ActionDescriptor.CreateKey(action)` and removes the private duplicate in the same commit.

- [ ] **Step 5: Register sources and run focused behavior tests**

Add both production files as `<Compile Include="Core\Decision\..." />` entries in `BobCoach.csproj`; add the PowerShell harness to `run_behavior_tests.ps1`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_policy_contract.ps1`

Expected: `PASS legacy policy preserves v1.1.0 action and decision contracts`.

- [ ] **Step 6: Commit the policy boundary**

```powershell
git add src/BobCoach/Core/Decision/Contracts.cs src/BobCoach/Core/Decision/LegacyDecisionPolicy.cs src/BobCoach/BobCoach.csproj tests/AlgorithmPolicyContractBehavior.cs tests/test_algorithm_policy_contract.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: isolate frozen legacy decision policy"
```

---

### Task 3: Generate Stable Candidates And Enforce Only Hard Constraints

**Files:**
- Create: `src/BobCoach/Core/Decision/ActionDescriptor.cs`
- Create: `src/BobCoach/Core/Decision/CandidateGenerator.cs`
- Create: `src/BobCoach/Core/Decision/HardConstraintFilter.cs`
- Create: `tests/AlgorithmCandidateBehavior.cs`
- Create: `tests/test_algorithm_candidates.ps1`
- Create: `docs/maintainer/algorithm-rule-migration.md`
- Modify: `src/BobCoach/Core/Decision/LegacyDecisionPolicy.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: `ActionEnumerator.Enumerate(GameState, string)`, `GameAction`, `EffectiveGameRules`, and Task 2 contracts.
- Produces: `ActionDescriptor.From(GameState, GameAction)`, `ActionDescriptor.CreateKey(GameAction)`, `CandidateGenerator.Generate(GameState)`, and `HardConstraintFilter.Apply(GameState, IEnumerable<ActionDescriptor>)`.

- [ ] **Step 1: Write failing candidate identity and constraint tests**

```csharp
var state = new GameState { GameActive = true, Gold = 2, TavernTier = 1, TavernUpgradeCost = 5 };
var refresh = new GameAction { Type = ActionType.Refresh, TargetIndex = -1, CardId = "", PurchaseSource = "" };
var first = ActionDescriptor.From(state, refresh);
var second = ActionDescriptor.From(state, refresh);
AssertEqual(first.ActionKey, second.ActionKey, "deterministic key");
AssertEqual("Refresh|-1||", first.ActionKey, "key format");

var upgrade = ActionDescriptor.From(state,
    new GameAction { Type = ActionType.Upgrade, TargetIndex = -1 });
var result = new HardConstraintFilter().Apply(state, new[] { first, upgrade });
AssertTrue(result.All(x => x.Action.Type != ActionType.Upgrade), "unaffordable upgrade rejected");
AssertTrue(result.Any(x => x.Action.Type == ActionType.Refresh), "strategy-neutral refresh retained");
```

- [ ] **Step 2: Run and verify missing types fail compilation**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_candidates.ps1`

Expected: compiler errors for `ActionDescriptor` and `HardConstraintFilter`.

- [ ] **Step 3: Implement deterministic action descriptors**

```csharp
public enum SimulatorSupport { None, Partial, Deterministic, ExpectedValue }
public sealed class ActionDescriptor
{
    public GameAction Action { get; private set; }
    public string ActionKey { get; private set; }
    public string SourceZone { get; private set; }
    public string TargetZone { get; private set; }
    public int EntityId { get; private set; }
    public int GoldCost { get; private set; }
    public SimulatorSupport SimulatorSupport { get; private set; }
    public static string CreateKey(GameAction action) { return string.Join("|", action.Type, action.TargetIndex, action.CardId ?? "", action.PurchaseSource ?? ""); }
    public static ActionDescriptor From(GameState state, GameAction action)
    {
        var descriptor = new ActionDescriptor
        {
            Action = action,
            ActionKey = CreateKey(action),
            SourceZone = SourceZoneFor(action.Type),
            TargetZone = TargetZoneFor(action.Type),
            EntityId = ResolveEntityId(state, action),
            GoldCost = ResolveGoldCost(state, action),
            SimulatorSupport = SupportFor(action.Type)
        };
        return descriptor;
    }
    private static string SourceZoneFor(ActionType type)
    {
        if (type == ActionType.BuyMinion || type == ActionType.BuySpell) return "shop";
        if (type == ActionType.SellMinion) return "board";
        if (type == ActionType.PickTrinket || type == ActionType.PickDiscover) return "choice";
        return "tavern";
    }
    private static string TargetZoneFor(ActionType type)
    {
        if (type == ActionType.BuyMinion || type == ActionType.BuySpell || type == ActionType.PickDiscover) return "hand";
        if (type == ActionType.SellMinion) return "none";
        return "tavern";
    }
    private static int ResolveEntityId(GameState state, GameAction action)
    {
        if ((action.Type == ActionType.BuyMinion || action.Type == ActionType.BuySpell)
            && action.TargetIndex >= 0 && action.TargetIndex < state.ShopMinions.Count)
            return state.ShopMinions[action.TargetIndex].EntityId;
        if (action.Type == ActionType.SellMinion
            && action.TargetIndex >= 0 && action.TargetIndex < state.BoardMinions.Count)
            return state.BoardMinions[action.TargetIndex].EntityId;
        if (action.Type == ActionType.PickTrinket
            && action.TargetIndex >= 0 && action.TargetIndex < state.TrinketOffer.Count)
            return state.TrinketOffer[action.TargetIndex].EntityId;
        if (action.Type == ActionType.PickDiscover
            && action.TargetIndex >= 0 && action.TargetIndex < state.DiscoverOptions.Count)
            return state.DiscoverOptions[action.TargetIndex].EntityId;
        return 0;
    }
    private static int ResolveGoldCost(GameState state, GameAction action)
    {
        var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
        if ((action.Type == ActionType.BuyMinion || action.Type == ActionType.BuySpell)
            && action.TargetIndex >= 0 && action.TargetIndex < state.ShopMinions.Count)
            return GameRuleEvaluator.GetPurchaseCost(state, state.ShopMinions[action.TargetIndex], state.HeroCardId, rules);
        if (action.Type == ActionType.Refresh)
            return GameRuleEvaluator.GetRefreshCost(state, state.HeroCardId, rules);
        if (action.Type == ActionType.Upgrade)
            return GameRuleEvaluator.GetUpgradeCost(state, rules) ?? int.MaxValue;
        if (action.Type == ActionType.UseHeroPower)
        {
            var power = state.HeroPowers == null ? null : state.HeroPowers.FirstOrDefault(x => x.CardId == action.CardId);
            return power == null ? state.HeroPowerCost : power.Cost;
        }
        return 0;
    }
    private static SimulatorSupport SupportFor(ActionType type)
    {
        if (type == ActionType.Refresh) return SimulatorSupport.ExpectedValue;
        if (type == ActionType.BuyMinion || type == ActionType.SellMinion || type == ActionType.Upgrade)
            return SimulatorSupport.Deterministic;
        if (type == ActionType.BuySpell || type == ActionType.FreezeShop || type == ActionType.UseHeroPower)
            return SimulatorSupport.Partial;
        return SimulatorSupport.None;
    }
}
```

- [ ] **Step 4: Implement generation and hard rejection reason codes**

```csharp
public sealed class CandidateGenerator
{
    private readonly ActionEnumerator _enumerator;
    public CandidateGenerator(ActionEnumerator enumerator) { _enumerator = enumerator; }
    public IList<ActionDescriptor> Generate(GameState state)
    {
        if (state == null || !state.GameActive) return new List<ActionDescriptor>();
        return _enumerator.Enumerate(state, state.HeroCardId)
            .Select(a => ActionDescriptor.From(state, a)).OrderBy(a => a.ActionKey, StringComparer.Ordinal).ToList();
    }
}

public sealed class ConstraintDecision
{
    public ActionDescriptor Candidate { get; private set; }
    public bool Accepted { get; private set; }
    public string ReasonCode { get; private set; }
    public ConstraintDecision(ActionDescriptor candidate, bool accepted, string reasonCode)
    { Candidate = candidate; Accepted = accepted; ReasonCode = reasonCode; }
}
```

`HardConstraintFilter.Evaluate` uses only the explicit codes `legal`, `insufficient-gold`, `invalid-source-index`, `invalid-target`, and `known-invalid-irreversible`; `Apply` returns accepted descriptors. It must not inspect turn curves, composition preference, generic health thresholds, or player profile.

- [ ] **Step 5: Inventory legacy early returns before migrating any rule**

Create `docs/maintainer/algorithm-rule-migration.md` with one row for every strategic return path in `DecisionEngine.GetBestAction`. Use this exact schema and permit only these dispositions:

```markdown
| Legacy location | Trigger | Class | New owner | Differential scenario ID | Removal gate |
| --- | --- | --- | --- | --- | --- |
| `Core/DecisionEngine.cs:<line>` | `<exact predicate>` | Legality / Safety invariant / Strategy heuristic / Duplicate-conflict / Unsupported special case | CandidateGenerator / HardConstraintFilter / ActionFeatureExtractor / Legacy fallback | `<synthetic-or-external-id>` | `<test or fallback condition>` |
```

No legacy rule is removed in this task. Legality moves only after candidate tests pass; safety invariants require a reason-code test; strategy heuristics become named features; unsupported cases remain in legacy fallback.

- [ ] **Step 6: Run candidate tests and existing action behavior tests**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_candidates.ps1`

Expected: `PASS candidate identities are stable and hard constraints remain strategy-neutral`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_behavior_tests.ps1`

Expected: `PASS behavior suite`.

- [ ] **Step 7: Commit candidate generation**

```powershell
git add src/BobCoach/Core/Decision/ActionDescriptor.cs src/BobCoach/Core/Decision/CandidateGenerator.cs src/BobCoach/Core/Decision/HardConstraintFilter.cs src/BobCoach/Core/Decision/LegacyDecisionPolicy.cs src/BobCoach/BobCoach.csproj tests/AlgorithmCandidateBehavior.cs tests/test_algorithm_candidates.ps1 tests/run_behavior_tests.ps1 docs/maintainer/algorithm-rule-migration.md
git commit -m "feat: add stable decision candidates and hard constraints"
```

---

### Task 4: Record Event-Aligned Decisions Without Treating Behavior As Truth

**Files:**
- Create: `src/BobCoach/Core/Decision/DecisionEventRecord.cs`
- Create: `src/BobCoach/Core/Decision/DecisionEventRecorder.cs`
- Create: `src/BobCoach/Core/Decision/DecisionLifecycleTracker.cs`
- Create: `tests/AlgorithmDecisionEventBehavior.cs`
- Create: `tests/test_algorithm_decision_events.ps1`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: Task 3 action keys, `BobCoachDataPaths`, Newtonsoft.Json, and immutable pre-action `GameState` snapshots.
- Produces: `DecisionEventRecord.Create(...)`, `DecisionEventRecorder.RecordCreated(...)`, `AttachRecommendation(...)`, `AttachObservedAction(...)`, `AttachCombatOutcome(...)`, `DecisionLifecycleTracker.AttachObservedActions(...)`, `BeginOrReuse(...)`, and deterministic SHA-256 state/candidate fingerprints.

- [ ] **Step 1: Write failing temporal-alignment and deduplication tests**

```csharp
var before = State(gold: 5, turn: 4);
var record = DecisionEventRecord.Create("decision-1", "game-1", before, candidates);
before.Gold = 2;
AssertEqual(5, record.State.Gold, "snapshot must be detached");
recorder.RecordCreated(record);
recorder.RecordCreated(record);
recorder.AttachObservedAction("decision-1", "BuyMinion|0|CARD_A|shop", "2026-07-31T00:00:02Z");
AssertThrows<ArgumentException>(() =>
    recorder.AttachRecommendation("", Result("legacy-v1.1.0"), "2026-07-31T00:00:03Z"),
    "blank recommendation decision ID");
AssertThrows<ArgumentException>(() =>
    recorder.AttachObservedAction(" ", "BuyMinion|0|CARD_A|shop", "2026-07-31T00:00:03Z"),
    "blank observed-action decision ID");
AssertEqual(1, ReadLines(path).Count(x => x.Contains("DecisionCreated")), "created dedupe");
AssertTrue(ReadLines(path).Last().Contains("PlayerActionObserved"), "later event type");
AssertTrue(!ReadLines(path).Last().Contains("acceptedActionKeys"), "behavior is not a quality label");

var lifecycle = new DecisionLifecycleTracker("game-1", recorder);
var first = lifecycle.BeginOrReuse(before, candidates);
var unchanged = lifecycle.BeginOrReuse(Clone(before), candidates);
AssertEqual(first.DecisionId, unchanged.DecisionId, "unchanged render reuses decision");
lifecycle.AttachObservedActions(new[] { "BuyMinion|0|CARD_A|shop" }, "2026-07-31T00:00:02Z");
var afterAction = lifecycle.BeginOrReuse(before, candidates);
AssertTrue(first.DecisionId != afterAction.DecisionId,
    "a player action starts a new decision even when the state fingerprint later repeats");
```

- [ ] **Step 2: Run and verify the recorder is absent**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_decision_events.ps1`

Expected: compiler errors for `DecisionEventRecord` and `DecisionEventRecorder`.

- [ ] **Step 3: Implement immutable record DTOs and canonical fingerprints**

```csharp
public sealed class DecisionEventRecord
{
    public const int SchemaVersion = 1;
    public string DecisionId { get; private set; }
    public string GameId { get; private set; }
    public GameStateSnapshot State { get; private set; }
    public IList<string> CandidateKeys { get; private set; }
    public string StateFingerprint { get; private set; }
    public string CandidateFingerprint { get; private set; }
    public static DecisionEventRecord Create(string decisionId, string gameId, GameState state,
        IEnumerable<ActionDescriptor> candidates)
    {
        if (string.IsNullOrWhiteSpace(decisionId)) throw new ArgumentException("decisionId");
        if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("gameId");
        if (state == null) throw new ArgumentNullException("state");
        var snapshot = GameStateSnapshot.From(state);
        var keys = (candidates ?? Enumerable.Empty<ActionDescriptor>())
            .Select(x => x.ActionKey).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return new DecisionEventRecord {
            DecisionId = decisionId, GameId = gameId, State = snapshot, CandidateKeys = keys,
            StateFingerprint = Fingerprint(CanonicalJson.Serialize(snapshot)),
            CandidateFingerprint = Fingerprint(CanonicalJson.Serialize(keys))
        };
    }

    private static string Fingerprint(string canonicalJson)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson)))
                .Replace("-", "").ToLowerInvariant();
    }
}
```

`GameStateSnapshot.From` copies only fields needed by the algorithm contract and maps every board, hand, and shop `MinionData` into a detached immutable `MinionSnapshot`; it also records game build, season ID, and card-pool identity. `CanonicalJson.Serialize` writes DTO properties in a fixed declared order, sorts sets and maps ordinally, and formats numeric values with `InvariantCulture`. Tests mutate each source list and one source `MinionData` after `Create` and assert that both snapshot and fingerprints remain unchanged. No player name, filesystem path, or raw log text is recorded.

- [ ] **Step 4: Implement append-only JSONL events with idempotent event keys**

```csharp
public sealed class DecisionEventRecorder
{
    private readonly string _path;
    private readonly object _sync = new object();
    private readonly HashSet<string> _writtenKeys = new HashSet<string>(StringComparer.Ordinal);
    public DecisionEventRecorder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
        _path = path;
        LoadExistingEventKeys(_writtenKeys, path);
    }
    public void RecordCreated(DecisionEventRecord record) { AppendOnce(record.DecisionId + "|created", "DecisionCreated", record); }
    public void AttachRecommendation(string decisionId, DecisionResult result, string observedAtUtc) { AppendOnce(decisionId + "|recommendation", "RecommendationIssued", new { decisionId, observedAtUtc, result }); }
    public void AttachObservedAction(string decisionId, string actionKey, string observedAtUtc) { AppendOnce(decisionId + "|action|" + actionKey, "PlayerActionObserved", new { decisionId, observedAtUtc, actionKey }); }
    public void AttachCombatOutcome(string decisionId, CombatOutcome outcome, string observedAtUtc) { AppendOnce(decisionId + "|combat", "CombatOutcomeObserved", new { decisionId, observedAtUtc, outcome }); }
    private void AppendOnce(string key, string eventType, object payload)
    {
        lock (_sync)
        {
            if (_writtenKeys.Contains(key)) return;
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var line = JsonConvert.SerializeObject(new { eventKey = key, eventType, payload }, Formatting.None)
                + Environment.NewLine;
            var bytes = new UTF8Encoding(false).GetBytes(line);
            using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            _writtenKeys.Add(key);
        }
    }
}
```

`DecisionEventRecorder` also implements `IShadowComparisonSink` once that interface is introduced in Task 5. Every recorder attachment and `RecordComparison` rejects a blank `decisionId`; the parameterless orchestration path is therefore reserved for `NoOpShadowComparisonSink` and isolated in-memory test sinks. A `ShadowComparison` stores the stable `decisionId`, state fingerprint, compared action fingerprints, policy IDs, confidence/fallback fields, and result summary only; it does not copy the complete `GameState` a second time.

Add a small `DecisionLifecycleTracker` scoped to the existing `_currentGameRecord` lifecycle. It receives one non-empty per-game ID at `OnGameStart`, owns an ordinal counter, and derives IDs as `<gameId>:<ordinal>`. `BeginOrReuse` computes the production state and candidate fingerprints before writing anything. It reuses the current ID only while both fingerprints are unchanged and no observed player action has closed that decision. `AttachObservedActions` attaches a deterministic, ordinal-distinct batch to the current ID and closes the generation only after the batch has been written; consequently, multiple inferred effects of one player action remain on the same prior decision, while a later byte-identical state still receives a new ID. State changes without a detected player action also create a new ID. The tracker calls `RecordCreated` synchronously before returning a newly created context, so plugin code cannot issue a recommendation or shadow comparison before `DecisionCreated` exists. It is ephemeral coordination owned by the current `GameRecord`, not a second persisted game-session model.

Use the existing `GameRecord.Timestamp` plus one process-local `Guid` generated at game start to create the non-personal game ID; store it only in the tracker and decision JSONL. Tests inject a fixed game ID and never depend on wall-clock time or randomness. `Reset` is called with `OnGameEnd`/the next `OnGameStart`, preventing IDs or pending actions from crossing games.

`LoadExistingEventKeys` streams existing JSONL, reads only `eventKey`, and fails closed on a malformed line instead of silently losing deduplication. Combat and game outcomes use separate event types and the same `decisionId`; none of these methods writes accepted actions or pairwise labels. The recorder is instantiated only when the existing local replay-recording option is enabled. Shadow mode does not create a new telemetry channel and never uploads data.

- [ ] **Step 5: Run event tests**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_decision_events.ps1`

Expected: `PASS decision events preserve pre-action state and temporal alignment`.

- [ ] **Step 6: Commit event alignment**

```powershell
git add src/BobCoach/Core/Decision/DecisionEventRecord.cs src/BobCoach/Core/Decision/DecisionEventRecorder.cs src/BobCoach/Core/Decision/DecisionLifecycleTracker.cs src/BobCoach/BobCoach.csproj tests/AlgorithmDecisionEventBehavior.cs tests/test_algorithm_decision_events.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: add aligned local decision event records"
```

---

### Task 5: Add Shadow Orchestration And Differential Records

**Files:**
- Create: `src/BobCoach/Core/Decision/DecisionOrchestrator.cs`
- Modify: `src/BobCoach/Core/Decision/DecisionEventRecorder.cs`
- Create: `tests/AlgorithmOrchestratorBehavior.cs`
- Create: `tests/test_algorithm_orchestrator.ps1`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: two `IDecisionPolicy` instances, an `IShadowComparisonSink`, and `DecisionRuntimeMode`.
- Produces: `DecisionOrchestrator.Decide(GameState)`, `DecisionOrchestrator.Decide(GameState, string decisionId)`, `ShadowComparison`, `IShadowComparisonSink`, `NoOpShadowComparisonSink.Instance`, and explicit `Shadow`, `Unified`, or `LegacyOnly` runtime modes.

- [ ] **Step 1: Write failing selection, fallback, and exception-containment tests**

```csharp
var recorder = new CollectingShadowComparisonSink();
var orchestrator = new DecisionOrchestrator(legacy, unified, recorder, DecisionRuntimeMode.Shadow);
var visible = orchestrator.Decide(state);
AssertEqual("legacy-v1.1.0", visible.PolicyId, "shadow visible policy");
AssertEqual(1, recorder.Comparisons.Count, "difference recorded");

var failing = new ThrowingPolicy("unified-linear-v1");
var safe = new DecisionOrchestrator(legacy, failing, recorder, DecisionRuntimeMode.Unified).Decide(state);
AssertEqual("legacy-v1.1.0", safe.PolicyId, "exception fallback");
AssertEqual(FallbackReason.ModelUnavailable, safe.FallbackReason, "fallback reason");

var throwingSink = new ThrowingShadowComparisonSink();
var diagnostics = new CollectingDecisionDiagnostics();
var contained = new DecisionOrchestrator(legacy, unified, throwingSink,
    DecisionRuntimeMode.Shadow, diagnostics).Decide(state, "decision-1");
AssertEqual("legacy-v1.1.0", contained.PolicyId, "recorder failure must not alter visible output");
AssertEqual(1, diagnostics.Entries.Count, "recorder failure is rate-limited diagnostic input");

var fullyContained = new DecisionOrchestrator(legacy, unified, throwingSink,
    DecisionRuntimeMode.Shadow, new ThrowingDecisionDiagnostics()).Decide(state, "decision-1");
AssertEqual("legacy-v1.1.0", fullyContained.PolicyId,
    "diagnostic failure must not escape into the UI call path");

recorder.RecordComparison(ShadowComparison.Create("decision-1", state, legacyResult, unifiedResult));
recorder.RecordComparison(ShadowComparison.Create("decision-1", state, legacyResult, unifiedResult));
recorder.RecordComparison(ShadowComparison.Create("decision-2", state, legacyResult, unifiedResult));
AssertThrows<ArgumentException>(() =>
    recorder.RecordComparison(ShadowComparison.Create("", state, legacyResult, unifiedResult)),
    "production recorder rejects blank decision ID");
AssertEqual(2, recorder.Comparisons.Count, "same decision dedupes; later identical decision remains distinct");
```

- [ ] **Step 2: Run and verify orchestration types are missing**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_orchestrator.ps1`

Expected: compiler errors for `DecisionOrchestrator` and `DecisionRuntimeMode`.

- [ ] **Step 3: Implement deterministic dual-policy orchestration**

```csharp
public enum DecisionRuntimeMode { LegacyOnly, Shadow, Unified }
public interface IShadowComparisonSink
{
    void RecordComparison(ShadowComparison comparison);
}
public sealed class NoOpShadowComparisonSink : IShadowComparisonSink
{
    public static readonly NoOpShadowComparisonSink Instance = new NoOpShadowComparisonSink();
    private NoOpShadowComparisonSink() { }
    public void RecordComparison(ShadowComparison comparison) { }
}
public interface IDecisionDiagnostics
{
    void RecordRateLimited(string source, Exception exception);
}
public sealed class NoOpDecisionDiagnostics : IDecisionDiagnostics
{
    public static readonly NoOpDecisionDiagnostics Instance = new NoOpDecisionDiagnostics();
    private NoOpDecisionDiagnostics() { }
    public void RecordRateLimited(string source, Exception exception) { }
}
public sealed class DecisionOrchestrator
{
    private readonly IDecisionPolicy _legacy;
    private readonly IDecisionPolicy _unified;
    private readonly IShadowComparisonSink _comparisonSink;
    private readonly IDecisionDiagnostics _diagnostics;
    private readonly DecisionRuntimeMode _mode;

    public DecisionOrchestrator(IDecisionPolicy legacy, IDecisionPolicy unified,
        IShadowComparisonSink comparisonSink, DecisionRuntimeMode mode,
        IDecisionDiagnostics diagnostics = null)
    {
        if (legacy == null) throw new ArgumentNullException("legacy");
        if (unified == null) throw new ArgumentNullException("unified");
        _legacy = legacy;
        _unified = unified;
        _comparisonSink = comparisonSink ?? NoOpShadowComparisonSink.Instance;
        _diagnostics = diagnostics ?? NoOpDecisionDiagnostics.Instance;
        _mode = mode;
    }

    public DecisionResult Decide(GameState state)
    {
        return DecideCore(state, null);
    }

    public DecisionResult Decide(GameState state, string decisionId)
    {
        if (string.IsNullOrWhiteSpace(decisionId)) throw new ArgumentException("decisionId");
        return DecideCore(state, decisionId);
    }

    private DecisionResult DecideCore(GameState state, string decisionId)
    {
        var legacy = SafeDecide(_legacy, state, FallbackReason.NoSafeAction);
        if (_mode == DecisionRuntimeMode.LegacyOnly) return legacy;
        var unified = SafeDecide(_unified, state, FallbackReason.ModelUnavailable);
        TryRecordComparison(ShadowComparison.Create(decisionId, state, legacy, unified));
        if (_mode == DecisionRuntimeMode.Shadow) return legacy;
        return unified.FallbackReason == FallbackReason.None ? unified : WithFallback(legacy, unified.FallbackReason);
    }

    private void TryRecordComparison(ShadowComparison comparison)
    {
        try { _comparisonSink.RecordComparison(comparison); }
        catch (Exception ex) { TryRecordDiagnostic("shadow-comparison-sink", ex); }
    }

    private DecisionResult SafeDecide(IDecisionPolicy policy, GameState state, FallbackReason exceptionReason)
    {
        try { return policy.Decide(state); }
        catch (Exception ex)
        {
            TryRecordDiagnostic(policy.PolicyId, ex);
            return UnavailableDecisionPolicy.For(exceptionReason).Decide(state);
        }
    }

    private void TryRecordDiagnostic(string source, Exception exception)
    {
        try { _diagnostics.RecordRateLimited(source, exception); }
        catch { /* diagnostics are never part of the decision result contract */ }
    }

    private static DecisionResult WithFallback(DecisionResult legacy, FallbackReason reason)
    {
        var effectiveReason = legacy.FallbackReason == FallbackReason.None ? reason : legacy.FallbackReason;
        return new DecisionResult(legacy.PolicyId, legacy.SelectedAction, legacy.RankedActions,
            legacy.Confidence, legacy.PlannerStatus, effectiveReason, legacy.Explanation);
    }
}
```

Change the Task 4 declaration to `public sealed class DecisionEventRecorder : IShadowComparisonSink` and add `public void RecordComparison(ShadowComparison comparison) { if (comparison == null) throw new ArgumentNullException("comparison"); if (string.IsNullOrWhiteSpace(comparison.DecisionId)) throw new ArgumentException("decisionId"); AppendOnce(comparison.ComparisonId, "ShadowComparison", comparison); }`. `ShadowComparison.Create` accepts the stable event-lifecycle `decisionId`; `ComparisonId` is the SHA-256 of that decision ID, the canonical state fingerprint, the canonical ordered compared-action fingerprint, and both policy IDs. It stores `DecisionId`, those fingerprints, policy IDs, selected action keys, agreement, confidence, planner status, fallback reason, and score margin only, without duplicating the complete state. The compared-action fingerprint is produced by the same canonical action-key serializer as `DecisionEventRecord`, over the ordinal distinct union of both returned ranked-action lists. Tests prove that repeated renders for one `decisionId` deduplicate while two real decisions with identical state/actions but different IDs remain separate. The one-argument `Decide(state)` overload may create a comparison with no ID only for `NoOpShadowComparisonSink` or an isolated in-memory test sink; production `DecisionEventRecorder` rejects it, and plugin wiring must use the two-argument overload whenever recording is active. The recorder instance exists only inside the existing local game-record lifecycle; otherwise the plugin and deterministic tools inject `NoOpShadowComparisonSink.Instance`, so benchmark execution cannot create gameplay records. Any sink exception is caught by `TryRecordComparison` and passed through `TryRecordDiagnostic`; neither recorder nor diagnostics failures can escape into the HDT UI thread or change the visible recommendation.

- [ ] **Step 4: Run orchestrator tests with an in-process determinism loop**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_orchestrator.ps1`

Expected: `PASS shadow mode preserves legacy output and contains unified failures`.

Inside `AlgorithmOrchestratorBehavior.cs`, call `Decide` 100 times against a detached clone of the same state and assert identical visible policy ID, selected action key, unified action key, confidence, planner status, and fallback reason on every iteration. This avoids treating shell output formatting as a determinism signal.

- [ ] **Step 5: Commit orchestration**

```powershell
git add src/BobCoach/Core/Decision/DecisionOrchestrator.cs src/BobCoach/Core/Decision/DecisionEventRecorder.cs src/BobCoach/BobCoach.csproj tests/AlgorithmOrchestratorBehavior.cs tests/test_algorithm_orchestrator.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: add shadow decision orchestration"
```

---

### Task 6: Extract A Versioned 45-Feature State-Action Schema

**Files:**
- Create: `src/BobCoach/Core/Decision/ActionFeatureSchema.cs`
- Create: `src/BobCoach/Core/Decision/ActionFeatureExtractor.cs`
- Create: `tests/AlgorithmFeatureBehavior.cs`
- Create: `tests/test_algorithm_features.ps1`
- Modify: `src/BobCoach/Core/Simulator.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: `GameState`, `ActionDescriptor`, simulated next state, existing `FeatureExtractor`, `SynergyEngine`, and season semantic sources.
- Produces: `ActionFeatureSchema.Version = 1`, exactly 45 ordered names, `ActionFeatureVector`, and `ActionFeatureExtractor.Extract(GameState, ActionDescriptor, GameState)`.

- [ ] **Step 1: Write failing schema, interaction, and preference-isolation tests**

```csharp
AssertEqual(1, ActionFeatureSchema.Version, "schema version");
AssertEqual(45, ActionFeatureSchema.Names.Count, "feature count");
AssertEqual(45, ActionFeatureSchema.Names.Distinct(StringComparer.Ordinal).Count(), "unique names");
var lowHealth = extractor.Extract(State(health: 6), buy, next);
var healthy = extractor.Extract(State(health: 30), buy, next);
AssertTrue(lowHealth["low_health_x_immediate_power"] != healthy["low_health_x_immediate_power"], "interaction");
AssertEqual(lowHealth.Values.Length, lowHealth.Coverage.Length, "coverage per feature");
AssertTrue(!ActionFeatureSchema.Names.Any(x => x.Contains("preference")), "no player preference");

var source = StateWithOneBoardMinion("CARD_A", attack: 3);
var simulated = new Simulator().Simulate(source, Sell(index: 0));
simulated.ShopMinions[0].Attack = 99;
AssertEqual(3, source.BoardMinions[0].Attack, "simulation must detach MinionData instances");
```

- [ ] **Step 2: Run and verify missing schema fails compilation**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1`

Expected: compiler errors for `ActionFeatureSchema` and `ActionFeatureExtractor`.

- [ ] **Step 3: Define the exact ordered cross-season schema**

```csharp
public static readonly IList<string> Names = new[] {
 "turn", "tavern_tier", "health_armor", "gold_before", "hand_slots_free", "board_slots_free",
 "immediate_attack_delta", "immediate_health_delta", "immediate_keyword_delta", "unit_count_delta", "board_power_delta",
 "net_gold_cost", "remaining_gold", "wasted_gold", "refresh_expected_value", "freeze_expected_value",
 "legal_follow_up_count", "playable_follow_up_count", "hand_congestion", "board_congestion",
 "pair_count_delta", "triple_progress_delta", "scaling_engine_delta", "protected_component_delta",
 "expected_damage_exposure", "low_health_tempo_need", "weak_board_level_risk",
 "current_synergy", "target_compatibility", "abandoned_piece_count", "abandoned_piece_value", "transition_cost",
 "unknown_effect_count", "simulator_coverage", "semantic_coverage", "stochastic_range", "state_observation_completeness",
 "low_health_x_immediate_power", "board_full_x_buy", "level_x_damage_exposure", "pair_x_matching_shop",
 "remaining_gold_x_follow_ups", "transition_x_protected_value", "target_tier_delta", "season_extension_value"
};
```

Action type is a learned one-hot categorical term stored separately in `ActionTypeBiases` with the fixed `ActionType` enum mapping; it is not part of the 45-value vector and is never treated as an ordered scalar. `season_extension_value` is zero with coverage `0` when no season extension recognizes the entity.

- [ ] **Step 4: Implement feature values and per-feature coverage**

```csharp
public sealed class ActionFeatureVector
{
    public double[] Values { get; private set; }
    public double[] Coverage { get; private set; }
    public double this[string name] { get { return Values[ActionFeatureSchema.IndexOf(name)]; } }
    public ActionFeatureVector(double[] values, double[] coverage) { Values = values; Coverage = coverage; }
}

public interface ISeasonFeatureExtension
{
    string SeasonId { get; }
    bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState, out double value);
}
public sealed class NullSeasonFeatureExtension : ISeasonFeatureExtension
{
    public static readonly NullSeasonFeatureExtension Instance = new NullSeasonFeatureExtension();
    public string SeasonId { get { return "none"; } }
    private NullSeasonFeatureExtension() { }
    public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState, out double value)
    { value = 0d; return false; }
}
```

Implement each named feature as a focused private method using the following fixed definitions. `BP(x)` is the existing `FeatureExtractor` board-power function, `KW(x)` counts the known combat keywords already represented by `MinionData`, `Owned(cardId)` counts normal copies across board and hand, and `Clamp01(x)` bounds to `[0,1]`.

| Features | Exact v1 definition |
| --- | --- |
| `turn`, `tavern_tier`, `health_armor`, `gold_before` | `state.Turn`, `state.TavernTier`, `(Health + Armor) / MaxHealth`, `state.Gold` |
| `hand_slots_free`, `board_slots_free` | `10 - HandMinions.Count`, `7 - BoardMinions.Count` |
| `immediate_attack_delta`, `immediate_health_delta`, `immediate_keyword_delta`, `unit_count_delta` | next-board total minus current-board total for attack, health, `KW`, and count |
| `board_power_delta` | `BP(next.BoardMinions) - BP(state.BoardMinions)` |
| `net_gold_cost`, `remaining_gold` | `state.Gold - next.Gold`, `next.Gold` |
| `wasted_gold` | gold left after greedily applying legal follow-ups sorted by descending cost then action key; coverage `0` if any cost is unknown |
| `refresh_expected_value`, `freeze_expected_value` | expected-shop value for Refresh; current-shop `BP` plus pair/semantic value for Freeze; otherwise `0` |
| `legal_follow_up_count`, `playable_follow_up_count` | hard-filtered next-state candidates; second count excludes choices whose target/cost semantics are unsupported |
| `hand_congestion`, `board_congestion` | next hand count / 10 and next board count / 7 |
| `pair_count_delta`, `triple_progress_delta` | delta of `sum(floor(Owned(id)/2))` and delta of `sum(min(Owned(id),3)/3.0)` over distinct non-golden minion IDs |
| `scaling_engine_delta`, `protected_component_delta` | delta of semantic `ScalingEngine` and `ProtectedComponent` tag counts; missing semantics give value `0`, coverage `0` |
| `expected_damage_exposure` | `Clamp01((OpponentPower(state) - BP(next.BoardMinions)) / max(1, OpponentPower(state)))`; opponent data absent gives coverage `0` |
| `low_health_tempo_need` | `1 - Clamp01((Health + Armor) / MaxHealth)` |
| `weak_board_level_risk` | for Upgrade, `Clamp01((SafeLevelPower[nextTier] - BP(state.BoardMinions)) / max(1, SafeLevelPower[nextTier]))`; otherwise `0` |
| `current_synergy`, `target_compatibility` | normalized board semantic synergy; target card's `SynergyEngine.ScoreCard(...).TotalScore`; unsupported target gives coverage `0` |
| `abandoned_piece_count`, `abandoned_piece_value`, `transition_cost` | pieces outside the dominant supported composition; their summed `BP`; `abandoned_piece_value + max(0, -board_power_delta) + 0.5 * max(0, -protected_component_delta)` |
| `unknown_effect_count` | candidate target plus affected next-state entities missing from the semantic source |
| `simulator_coverage`, `semantic_coverage` | supported simulated effects / declared effects; known semantic entities / relevant entities; denominator `0` maps to value `1`, coverage `1` |
| `stochastic_range` | upper minus lower expected outcome reported by the season extension/expected-shop estimator; deterministic actions are `0` |
| `state_observation_completeness` | present critical fields among game-active, shop phase, gold, tavern tier, health, board, hand, shop, opponents divided by `9` |
| six `*_x_*` interactions | product of the two already defined base features named on each side of `_x_` |
| `target_tier_delta` | target entity tier minus current tavern tier; actions without a target are `0` with coverage `0` |
| `season_extension_value` | extension result, or value `0` and coverage `0` when no extension recognizes the entity |

Clamp ratios to `[-5, 5]`, map booleans to `0/1`, and use explicit unknown coverage `0`. Never call `ProfileEngine` or read user preference weights. `feature_distribution_distance` is deliberately not a trained feature: Task 9 computes OOD distance from the completed vector and Task 7 model bounds, avoiding a circular dependency. Action type is represented by Task 7's independent learned per-type biases, not by an ordinal number that would invent a false ordering between enum values.

- [ ] **Step 5: Make simulator states structurally detached**

Rename `ShallowCopy` to `DetachedCopy`. Clone every `MinionData` in board, hand, and shop via `CloneMinion`, and clone every mutable element that a simulation branch can mutate. Add a regression test that mutates simulated minions and expectation collections after each supported action type and asserts the source `GameState`, nested objects, and source fingerprints are unchanged.

- [ ] **Step 6: Run feature and existing value-function tests**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_features.ps1`

Expected: `PASS 45 named state-action features are deterministic and preference-free`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_value_function_weight_contract.ps1`

Expected: `PASS ValueFunction exposes the exact 22 production weights`.

- [ ] **Step 7: Commit the feature schema**

```powershell
git add src/BobCoach/Core/Decision/ActionFeatureSchema.cs src/BobCoach/Core/Decision/ActionFeatureExtractor.cs src/BobCoach/Core/Simulator.cs src/BobCoach/BobCoach.csproj tests/AlgorithmFeatureBehavior.cs tests/test_algorithm_features.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: add named state-action feature schema"
```

---

### Task 7: Validate And Load Versioned Model Artifacts

**Files:**
- Create: `src/BobCoach/Core/Decision/RankingModel.cs`
- Create: `tests/fixtures/algorithm/linear-v1.valid.envelope.json`
- Create: `tests/AlgorithmModelContractBehavior.cs`
- Create: `tests/test_algorithm_model_contract.ps1`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: Task 6 schema and Newtonsoft.Json.
- Produces: `RankingModelEnvelope`, `RankingModelPayload`, `RankingModelLoader.Load(Stream)`, and `ModelValidationException`.

- [ ] **Step 1: Write failing valid, checksum, schema, and numeric tests**

```csharp
var valid = RankingModelLoader.Load(Open("linear-v1.valid.envelope.json"));
AssertEqual(45, valid.Weights.Length, "weight count");
AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(TamperPayload(validEnvelope)), "checksum");
AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithFeatureVersion(99)), "feature schema");
AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithWeight("NaN")), "finite number");
AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(new MemoryStream(new byte[] { 1, 2, 3 })), "invalid json");
```

- [ ] **Step 2: Run and verify model contract types are missing**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_model_contract.ps1`

Expected: compiler errors for `RankingModelLoader` and `ModelValidationException`.

- [ ] **Step 3: Implement the envelope and payload contract**

```csharp
public sealed class RankingModelEnvelope
{
    public int ArtifactSchemaVersion { get; set; }
    public string PayloadBase64 { get; set; }
    public string PayloadSha256 { get; set; }
}
public sealed class RankingModelPayload
{
    public string ModelId { get; set; }
    public int FeatureSchemaVersion { get; set; }
    public string[] FeatureNames { get; set; }
    public double[] Means { get; set; }
    public double[] StandardDeviations { get; set; }
    public double[] Minimums { get; set; }
    public double[] Maximums { get; set; }
    public double[] Weights { get; set; }
    public double[] ActionTypeBiases { get; set; }
    public double Intercept { get; set; }
    public double OodDistanceThreshold { get; set; }
    public string TrainingSetIdentity { get; set; }
    public string SeasonRange { get; set; }
    public IDictionary<string, double> BenchmarkMetrics { get; set; }
    public string CreatedAtUtc { get; set; }
    [JsonIgnore]
    public string PayloadSha256 { get; internal set; }
}
```

- [ ] **Step 4: Implement strict SHA-256-first loading**

```csharp
public static RankingModelPayload Load(Stream stream)
{
    var envelope = DeserializeEnvelopeWithSizeLimit(stream, 1024 * 1024);
    if (envelope.ArtifactSchemaVersion != 1) throw Invalid("artifact schema");
    byte[] payload;
    try { payload = Convert.FromBase64String(envelope.PayloadBase64); }
    catch (FormatException ex) { throw Invalid("payload base64", ex); }
    var checksum = Sha256Hex(payload);
    if (!FixedTimeEquals(checksum, envelope.PayloadSha256)) throw Invalid("checksum");
    var model = DeserializePayload(payload);
    ValidateSchemaNamesLengthsAndFiniteNumbers(model, ActionFeatureSchema.Names);
    model.PayloadSha256 = checksum;
    return model;
}
```

Validation rejects zero/negative standard deviations, a non-positive OOD threshold, non-finite values, array length mismatch, an `ActionTypeBiases` length different from the defined `ActionType` count, duplicate/missing/reordered feature names, missing model identity, and payloads over 1 MiB.

- [ ] **Step 5: Run model-contract tests**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_model_contract.ps1`

Expected: `PASS model loader rejects corruption, schema drift, and invalid numbers`.

- [ ] **Step 6: Commit model loading**

```powershell
git add src/BobCoach/Core/Decision/RankingModel.cs src/BobCoach/BobCoach.csproj tests/fixtures/algorithm/linear-v1.valid.envelope.json tests/AlgorithmModelContractBehavior.cs tests/test_algorithm_model_contract.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: validate local ranking model artifacts"
```

---

### Task 8: Train And Run The Regularized Linear Pairwise Ranker

**Files:**
- Create: `src/BobCoach/Core/Decision/LinearPairwiseRanker.cs`
- Create: `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- Create: `tools/model/build_pairs.js`
- Create: `tools/model/train_pairwise.js`
- Create: `tools/model/split_dataset.js`
- Create: `tests/fixtures/algorithm/pairs.synthetic.jsonl`
- Create: `tests/AlgorithmLinearRankerBehavior.cs`
- Create: `tests/test_algorithm_linear_ranker.ps1`
- Create: `tests/test_pairwise_training.js`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_contract_tests.ps1`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: Tasks 3, 6, and 7 plus pair rows `{ gameId, capturedAtUtc, scenarioId, preferred, other, label }`.
- Produces: `LinearPairwiseRanker.Score(ActionDescriptor, ActionFeatureVector)`, `Rank(...)`, `UnifiedRankingPolicy.Decide(GameState)`, `UnifiedRankingPolicy.CreateProduction(RankingModelPayload, ISeasonFeatureExtension)`, label-derived pair rows, deterministic training envelopes, and whole-game/time-aware splits.

- [ ] **Step 1: Write failing ranker and trainer tests**

```csharp
var model = Model(weights: FeatureWeight("board_power_delta", 2d), intercept: 0d);
var ranker = new LinearPairwiseRanker(model);
var ranked = ranker.Rank(new[] { Candidate("weak", 1d), Candidate("strong", 3d) });
AssertEqual("strong", ranked[0].ActionKey, "top candidate");
AssertEqual("board_power_delta", ranked[0].Factors.OrderByDescending(x => Math.Abs(x.Contribution)).First().FeatureName, "factor");
AssertStable(ranker.Rank(inputs), 100, "tie breaking");
```

```javascript
const first = train(loadSyntheticPairs(), { epochs: 200, learningRate: 0.05, lambda: 0.01, seed: 7 });
const second = train(loadSyntheticPairs(), { epochs: 200, learningRate: 0.05, lambda: 0.01, seed: 7 });
assert.deepStrictEqual(first, second);
assert(score(first, preferred) > score(first, other));
assert.deepStrictEqual(buildPairs(labeledScenario), expectedDefensiblePairs);
assert(!JSON.stringify(buildPairs(labeledScenario)).includes('observedPlayerActionKey'));
assert.throws(() => splitRows(rowsWithSameGameAcrossPartitions), /whole-game split/);
```

- [ ] **Step 2: Run and verify ranker/trainer are absent**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_linear_ranker.ps1`

Expected: compiler errors for `LinearPairwiseRanker`.

Run: `node tests/test_pairwise_training.js`

Expected: FAIL because `tools/model/train_pairwise.js` does not exist.

- [ ] **Step 3: Implement normalized deterministic scoring and ranking**

```csharp
public ScoredFeatureVector Score(ActionDescriptor action, ActionFeatureVector vector)
{
    var contributions = new double[_model.Weights.Length];
    var total = _model.Intercept + _model.ActionTypeBiases[(int)action.Action.Type];
    for (var i = 0; i < contributions.Length; i++)
    {
        var normalized = (vector.Values[i] - _model.Means[i]) / _model.StandardDeviations[i];
        contributions[i] = normalized * _model.Weights[i];
        total += contributions[i];
    }
    return new ScoredFeatureVector(total, contributions);
}
```

`Rank` sorts by descending finite score and then ordinal `ActionKey`. The action-type bias is trained and regularized beside the feature weights; the trainer encodes it as a one-hot delta rather than an enum ordinal. `UnifiedRankingPolicy` generates, filters, simulates on detached states, extracts, ranks, and returns all candidates. Its `CreateProduction` static method is the sole internal dependency graph builder; it normalizes a null season extension to `NullSeasonFeatureExtension.Instance` and constructs the production `CandidateGenerator`, `HardConstraintFilter`, detached `Simulator`, `ActionFeatureExtractor`, and `LinearPairwiseRanker`. Tasks 9-11 modify that same method to add confidence, expected-shop estimation, planning, and explanations without changing the score surface or creating a second graph.

- [ ] **Step 4: Implement zero-dependency pairwise logistic training**

```javascript
function pairLossGradient(preferred, other, label, weights, lambda) {
    const delta = preferred.features.map((v, i) => v - other.features[i]);
    const actionDelta = oneHotDelta(preferred.actionType, other.actionType, actionTypeCount);
    const trainingDelta = delta.concat(actionDelta);
    const signedMargin = label * dot(weights, trainingDelta);
    const scale = -label / (1 + Math.exp(Math.max(-60, Math.min(60, signedMargin))));
    return weights.map((w, i) => scale * trainingDelta[i] + 2 * lambda * w);
}
```

`build_pairs.js` parses `--data-root`, `--scenarios`, and `--output`, validates every action grade, and emits pairs only for a higher-grade accepted action against an explicitly lower-grade action in the same pre-action state. It excludes `not-evaluable` actions, never derives a preference from `observedPlayerActionKey` or final placement, and retains game/time/state/candidate identities. `train_pairwise.js` parses `--data-root`, `--pairs`, `--output`, `--epochs`, `--learning-rate`, `--lambda`, and `--seed`; sort input by `(gameId, scenarioId, actionKey)` before seeded shuffling. Write the payload bytes, compute SHA-256 over those exact bytes, then write the envelope. `split_dataset.js` deduplicates `stateFingerprint|candidateFingerprint`, groups by `gameId`, and reserves the newest whole games as the final holdout.

- [ ] **Step 5: Run focused and deterministic training tests**

Run: `node tests/test_pairwise_training.js`

Expected: `PASS pairwise training is deterministic and split-safe`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_linear_ranker.ps1`

Expected: `PASS linear ranker uses one deterministic score surface`.

- [ ] **Step 6: Commit ranking and training**

```powershell
git add src/BobCoach/Core/Decision/LinearPairwiseRanker.cs src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs src/BobCoach/BobCoach.csproj tools/model/build_pairs.js tools/model/train_pairwise.js tools/model/split_dataset.js tests/fixtures/algorithm/pairs.synthetic.jsonl tests/AlgorithmLinearRankerBehavior.cs tests/test_algorithm_linear_ranker.ps1 tests/test_pairwise_training.js tests/run_contract_tests.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: add deterministic pairwise action ranking"
```

---

### Task 9: Gate Recommendations By Confidence, Coverage, And Distribution Shift

**Files:**
- Create: `src/BobCoach/Core/Decision/DecisionConfidenceEvaluator.cs`
- Create: `tests/AlgorithmConfidenceBehavior.cs`
- Create: `tests/test_algorithm_confidence.ps1`
- Modify: `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: ranked actions, Task 6 feature coverage, Task 7 training bounds, and critical state fields.
- Produces: `ConfidenceAssessment Evaluate(GameState, IList<RankedAction>, ActionFeatureVector, RankingModelPayload)` and `DistributionDistance(ActionFeatureVector, RankingModelPayload)`.

- [ ] **Step 1: Write failing confidence-floor tests**

```csharp
AssertFallback(Evaluate(State(gameActive: false), completeVector), FallbackReason.CriticalStateMissing);
AssertFallback(Evaluate(State(), Vector(semanticCoverage: 0.2)), FallbackReason.UnsupportedMechanic);
AssertFallback(Evaluate(State(), Vector(boardPowerDelta: 40.0), Model(maximumBoardPowerDelta: 5.0)), FallbackReason.OutOfDistribution);
var close = Evaluate(State(), completeVector, scores: new[] { 1.00, 0.99 });
AssertTrue(close.ShouldPlan, "small margin requests planning");
var clear = Evaluate(State(), completeVector, scores: new[] { 2.0, 0.2 });
AssertTrue(clear.Score > close.Score, "margin raises confidence");
AssertTrue(clear.DistributionDistance >= 0d, "distance is reported");
```

- [ ] **Step 2: Run and verify confidence types are absent**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_confidence.ps1`

Expected: compiler errors for `DecisionConfidenceEvaluator`.

- [ ] **Step 3: Implement conservative confidence composition**

```csharp
public sealed class ConfidenceAssessment
{
    public double Score { get; private set; }
    public bool ShouldFallback { get; private set; }
    public FallbackReason FallbackReason { get; private set; }
    public bool ShouldPlan { get; private set; }
    public double DistributionDistance { get; private set; }

    public ConfidenceAssessment(double score, bool shouldFallback, FallbackReason fallbackReason,
        bool shouldPlan, double distributionDistance)
    {
        Score = score;
        ShouldFallback = shouldFallback;
        FallbackReason = fallbackReason;
        ShouldPlan = shouldPlan;
        DistributionDistance = distributionDistance;
    }
}

var distance = DistributionDistance(vector, payload);
var marginScore = Clamp01((top.Score - runnerUp.Score) / _marginScale);
var completeness = CriticalStateCompleteness(state);
var coverage = Math.Min(Average(vector.Coverage), vector["semantic_coverage"]);
var distribution = Math.Exp(-Math.Max(0d, distance));
var score = Math.Min(Math.Min(marginScore, completeness), Math.Min(coverage, distribution));
```

`DistributionDistance` iterates the completed 45-value vector in schema order. For each feature it computes the absolute z-score using `Means[i]` and `StandardDeviations[i]`, plus an extra normalized excess when the value falls below `Minimums[i]` or above `Maximums[i]`; the returned value is the root-mean-square of those finite per-feature distances. It never reads a synthetic distance feature. Critical `GameActive`, phase, gold, tavern tier, and candidate validity failures force `CriticalStateMissing`. Semantic coverage below `0.60` forces `UnsupportedMechanic`; distribution distance above `payload.OodDistanceThreshold` forces `OutOfDistribution`; confidence below `0.55` requests `LowConfidence` fallback. A top-two normalized margin below `0.15`, protected sell, upgrade, or survival-risk feature above `0.70` requests planning when supported. Reason precedence is `CriticalStateMissing`, `UnsupportedMechanic`, `OutOfDistribution`, then `LowConfidence`, so a lower-priority condition cannot overwrite the diagnostic cause.

- [ ] **Step 4: Integrate confidence without swallowing fallback reasons**

`UnifiedRankingPolicy` keeps an internal `CandidateEvaluation` tuple for every candidate: `{ ActionDescriptor Descriptor, ActionFeatureVector Features, RankedAction Ranked }`. After deterministic ranking it resolves the top `CandidateEvaluation` by `ActionKey` and passes that exact `Features` instance to `Evaluate`; it never reconstructs the vector from display factors. `Decide` returns its ranked list even on fallback, sets the exact assessment reason, and leaves final legacy selection to `DecisionOrchestrator`. Unsupported simulator actions receive an uncertainty penalty feature before ranking and cannot be promoted by confidence. Modify `UnifiedRankingPolicy.CreateProduction` in this task so it constructs and injects exactly one `DecisionConfidenceEvaluator`; tests must instantiate the policy through this method as well as through focused component fixtures.

- [ ] **Step 5: Run confidence and orchestration tests**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_confidence.ps1`

Expected: `PASS confidence floors force explicit safe fallback`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_orchestrator.ps1`

Expected: `PASS shadow mode preserves legacy output and contains unified failures`.

- [ ] **Step 6: Commit confidence gating**

```powershell
git add src/BobCoach/Core/Decision/DecisionConfidenceEvaluator.cs src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs src/BobCoach/BobCoach.csproj tests/AlgorithmConfidenceBehavior.cs tests/test_algorithm_confidence.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: gate ranked decisions by confidence and coverage"
```

---

### Task 10: Add Expected Refresh And Selective Deterministic Beam Planning

**Files:**
- Create: `src/BobCoach/Core/Decision/ExpectedShopEstimator.cs`
- Create: `src/BobCoach/Core/Decision/BeamPlanner.cs`
- Create: `tests/AlgorithmBeamPlannerBehavior.cs`
- Create: `tests/test_algorithm_beam_planner.ps1`
- Modify: `src/BobCoach/Core/Decision/ActionFeatureExtractor.cs`
- Modify: `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`

**Interfaces:**
- Consumes: Task 8 ranker, simulator, card-pool facts, Task 9 `ShouldPlan`, and monotonic elapsed-time source.
- Produces: `ExpectedShopEstimator.Estimate(GameState)`, `BeamPlanner.Plan(GameState, IList<RankedAction>, TimeSpan)`, and `PlanningResult`.

- [ ] **Step 1: Write failing deterministic, bounded, and timeout tests**

```csharp
var planner = Planner(clock: new FakeClock(stepMilliseconds: 1));
var first = planner.Plan(state, ranked, TimeSpan.FromMilliseconds(25));
var second = planner.Plan(state, ranked, TimeSpan.FromMilliseconds(25));
AssertEqual(first.ActionKey, second.ActionKey, "deterministic plan");
AssertTrue(first.ExpandedRoots <= 4, "Top-4 roots");
AssertTrue(first.MaximumBeamWidth <= 6, "beam width");
AssertTrue(first.MaximumDepth <= 4, "depth");

var timedOut = Planner(clock: new FakeClock(stepMilliseconds: 30))
    .Plan(state, ranked, TimeSpan.FromMilliseconds(25));
AssertEqual(PlannerStatus.TimedOut, timedOut.Status, "timeout status");
AssertTrue(timedOut.BestCompletePath.Count > 0, "best completed path retained");
```

- [ ] **Step 2: Run and verify planning types are absent**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_beam_planner.ps1`

Expected: compiler errors for `BeamPlanner` and `ExpectedShopEstimator`.

- [ ] **Step 3: Implement deterministic expected-shop estimation**

```csharp
public ExpectedShopEstimate Estimate(GameState state)
{
    var eligible = _pool.GetEligible(state.TavernTier, state.AvailableTribes)
        .Where(x => x.RemainingCopies > 0).OrderBy(x => x.CardId, StringComparer.Ordinal).ToList();
    var denominator = eligible.Sum(x => x.RemainingCopies);
    var expectedPower = denominator == 0 ? 0d : eligible.Sum(x => x.RemainingCopies * x.RelevantPower) / denominator;
    return new ExpectedShopEstimate(expectedPower, eligible.Count, denominator == 0 ? 0d : 1d);
}
```

The estimate aggregates tier, available pool, remaining copies, relevant tribes/mechanics, and unknown mass. It does not sample a random shop or claim a concrete refreshed board.

- [ ] **Step 4: Implement Top-4/width-6/depth-4 beam search with a hard budget**

```csharp
public sealed class PlanningResult
{
    public PlannerStatus Status { get; private set; }
    public string ActionKey { get; private set; }
    public IList<string> BestCompletePath { get; private set; }
    public int ExpandedRoots { get; private set; }
    public int MaximumBeamWidth { get; private set; }
    public int MaximumDepth { get; private set; }

    private PlanningResult(PlannerStatus status, string actionKey, IList<string> path,
        int expandedRoots, int maximumBeamWidth, int maximumDepth)
    {
        Status = status;
        ActionKey = actionKey ?? "";
        BestCompletePath = path ?? new List<string>();
        ExpandedRoots = expandedRoots;
        MaximumBeamWidth = maximumBeamWidth;
        MaximumDepth = maximumDepth;
    }

    public static PlanningResult NotRequested()
    { return new PlanningResult(PlannerStatus.NotRequested, "", new List<string>(), 0, 0, 0); }

    public static PlanningResult Completed(string actionKey, IList<string> path,
        int expandedRoots, int maximumBeamWidth, int maximumDepth)
    { return new PlanningResult(PlannerStatus.Completed, actionKey, path, expandedRoots, maximumBeamWidth, maximumDepth); }

    public static PlanningResult TimedOut(string actionKey, IList<string> path,
        int expandedRoots, int maximumBeamWidth, int maximumDepth)
    { return new PlanningResult(PlannerStatus.TimedOut, actionKey, path, expandedRoots, maximumBeamWidth, maximumDepth); }
}

var beam = roots.Take(4).Select(CreateRootNode).ToList();
for (var depth = 1; depth <= 4 && beam.Count > 0; depth++)
{
    if (_clock.Elapsed >= budget) return ToTimedOutResult(bestComplete, statistics);
    var expanded = ExpandDeterministically(beam)
        .OrderByDescending(x => x.PathScore)
        .ThenBy(x => x.PathKey, StringComparer.Ordinal)
        .Take(6).ToList();
    bestComplete = BestComplete(bestComplete, expanded);
    beam = expanded.Where(x => x.CanContinue).ToList();
}
return ToCompletedResult(bestComplete, statistics);
```

`ToCompletedResult` and `ToTimedOutResult` copy the selected root `ActionKey`, the complete ordered action-key path, and observed root/depth/width statistics into the immutable result; a timeout before any complete path returns the original top-ranked root as a one-item safe path. Use detached simulation states. Refresh nodes consume `ExpectedShopEstimate` and stop concrete expansion. Unsupported stochastic actions end the path with their uncertainty penalty. The planner only replaces the root action when the best complete path exceeds the current path by an additive calibrated margin valid for negative scores. `UnifiedRankingPolicy` resolves `PlanningResult.ActionKey` against the existing ranked actions, uses that action as `DecisionResult.SelectedAction`, copies `PlanningResult.Status` to `DecisionResult.PlannerStatus`, and passes the same result to `ExplanationBuilder`; an unknown or empty key leaves the top-ranked action unchanged. Modify `UnifiedRankingPolicy.CreateProduction` in this task so it constructs one `ExpectedShopEstimator` and one monotonic-clock-backed `BeamPlanner`, passes the estimator into both feature extraction and planning, and keeps the injectable fake-clock constructor available only for behavior tests.

- [ ] **Step 5: Run planning, determinism, and latency harnesses**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_beam_planner.ps1`

Expected: `PASS beam planning is deterministic, bounded, and timeout-safe`.

The harness also runs 1,000 synthetic ranking decisions and 500 planned decisions, prints P50/P95/P99, and fails if ranking P95 exceeds 10 ms or planning P95 exceeds 25 ms on the documented validation machine.

- [ ] **Step 6: Commit selective planning**

```powershell
git add src/BobCoach/Core/Decision/ExpectedShopEstimator.cs src/BobCoach/Core/Decision/BeamPlanner.cs src/BobCoach/Core/Decision/ActionFeatureExtractor.cs src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs src/BobCoach/BobCoach.csproj tests/AlgorithmBeamPlannerBehavior.cs tests/test_algorithm_beam_planner.ps1 tests/run_behavior_tests.ps1
git commit -m "feat: add selective deterministic beam planning"
```

---

### Task 11: Generate Contribution Explanations And Integrate Shadow Mode In The Plugin

**Files:**
- Create: `src/BobCoach/Core/Decision/ExplanationBuilder.cs`
- Create: `src/BobCoach/Core/Decision/DecisionPolicyFactory.cs`
- Create: `tests/AlgorithmExplanationBehavior.cs`
- Create: `tests/test_algorithm_explanations.ps1`
- Modify: `src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs`
- Modify: `src/BobCoach/BobCoachPlugin.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/run_behavior_tests.ps1`
- Modify: `tests/test_offline_runtime_contract.js`

**Interfaces:**
- Consumes: top-ranked score contributions, planner result, an optional checksum-valid embedded model stream, and Task 5 orchestrator.
- Produces: `ExplanationBuilder.Build(RankedAction, PlanningResult)`, `DecisionPolicyFactory.Create(DecisionEngine, Stream, ISeasonFeatureExtension)`, `DecisionPolicyBundle`, plugin-owned `DecisionOrchestrator`, a `DecisionLifecycleTracker` bound to `_currentGameRecord`, and shadow-only orchestration that fails closed when no approved model has been embedded yet.

- [ ] **Step 1: Write failing explanation and plugin-wiring tests**

```csharp
var factors = new[] {
    new DecisionFactor("board_power_delta", 1.2),
    new DecisionFactor("remaining_gold", 0.8),
    new DecisionFactor("unknown_effect_count", -0.6),
    new DecisionFactor("turn", 0.01)
};
var text = new ExplanationBuilder().Build(Ranked("buy", factors), PlanningResult.NotRequested());
AssertTrue(text.Contains("board power"), "largest positive factor");
AssertTrue(text.Contains("remaining gold"), "second factor");
AssertTrue(text.Contains("uncertain effect"), "largest risk factor");
AssertTrue(!text.Contains("1.2"), "no raw model number");
```

Add static contract assertions to `test_offline_runtime_contract.js` that `BobCoachPlugin.cs` constructs `DecisionRuntimeMode.Shadow`, treats the embedded model resource as optional, obtains the visible action from `_decisionOrchestrator.Decide(state, context.DecisionId)` when local recording is active, and uses `_decisionOrchestrator.Decide(state)` only with `NoOpShadowComparisonSink.Instance` when recording is inactive, while retaining `_engine` for legacy shop-card and level APIs.

Add factory-boundary behavior tests: a missing stream, malformed JSON, schema/checksum incompatibility, and a deterministic read `IOException` all return `UnavailableDecisionPolicy` with `ModelUnavailable`. A null `DecisionEngine` and a deliberately invalid internal dependency fixture still throw their original programmer-error exceptions; these must not be disguised as model absence. Add lifecycle wiring assertions for both branches: active local recording must follow `AttachObservedActions -> BeginOrReuse/DecisionCreated -> Decide(state, decisionId) -> AttachRecommendation`, while recording-disabled execution must use `Decide(state)` with the no-op sink.

- [ ] **Step 2: Run and verify explanation/wiring tests fail**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_explanations.ps1`

Expected: compiler errors for `ExplanationBuilder`.

Run: `node tests/test_offline_runtime_contract.js`

Expected: FAIL because plugin shadow orchestration is not wired.

- [ ] **Step 3: Implement contribution-derived explanation text**

```csharp
public string Build(RankedAction selected, PlanningResult planning)
{
    if (selected == null) return "";
    var factors = selected.Factors.Where(IsPlayerRelevant)
        .OrderByDescending(x => Math.Abs(x.Contribution)).ThenBy(x => x.FeatureName, StringComparer.Ordinal)
        .Take(3).Select(FormatFactor).ToList();
    if (planning != null && planning.Status == PlannerStatus.Completed) factors.Add("the follow-up path stays flexible");
    return string.Join("; ", factors.Take(3));
}
```

Use a fixed dictionary from feature names to concise player-facing phrases. The builder never changes rank, emits raw weights, exposes confidence formulas, or invents a reason absent from the selected action's contributions.

- [ ] **Step 4: Wire optional-model shadow orchestration**

Make `DecisionPolicyFactory` the only composition root for legacy and unified policy dependencies so the plugin and Task 12 benchmark cannot drift:

```csharp
public sealed class DecisionPolicyBundle
{
    public IDecisionPolicy Legacy { get; private set; }
    public IDecisionPolicy Unified { get; private set; }
    public CandidateGenerator Candidates { get; private set; }
    public string ModelId { get; private set; }
    public string ModelPayloadSha256 { get; private set; }
    public DecisionPolicyBundle(IDecisionPolicy legacy, IDecisionPolicy unified,
        CandidateGenerator candidates,
        string modelId, string modelPayloadSha256)
    {
        Legacy = legacy;
        Unified = unified;
        Candidates = candidates;
        ModelId = modelId ?? "";
        ModelPayloadSha256 = modelPayloadSha256 ?? "";
    }
}

public static class DecisionPolicyFactory
{
    public static DecisionPolicyBundle Create(DecisionEngine engine, Stream optionalModel,
        ISeasonFeatureExtension seasonExtension)
    {
        if (engine == null) throw new ArgumentNullException("engine");
        var legacy = new LegacyDecisionPolicy(engine);
        var candidates = new CandidateGenerator(new ActionEnumerator());
        if (optionalModel == null)
            return Unavailable(legacy, candidates);
        try
        {
            var model = RankingModelLoader.Load(optionalModel);
            return new DecisionPolicyBundle(legacy,
                UnifiedRankingPolicy.CreateProduction(model, candidates,
                    seasonExtension ?? NullSeasonFeatureExtension.Instance),
                candidates, model.ModelId, model.PayloadSha256);
        }
        catch (ModelValidationException)
        {
            return Unavailable(legacy, candidates);
        }
        catch (IOException)
        {
            return Unavailable(legacy, candidates);
        }
    }

    private static DecisionPolicyBundle Unavailable(IDecisionPolicy legacy,
        CandidateGenerator candidates)
    {
        return new DecisionPolicyBundle(legacy,
            UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable),
            candidates, "", "");
    }
}
```

`DecisionPolicyFactory` is the only external entry for plugin and benchmark policy construction. It creates one `CandidateGenerator`, exposes that exact instance through the bundle for lifecycle fingerprints, and passes it into `UnifiedRankingPolicy.CreateProduction`; the policy then constructs the rest of the single production graph: `HardConstraintFilter`, detached `Simulator`, `ActionFeatureExtractor`, `LinearPairwiseRanker`, `DecisionConfidenceEvaluator`, `ExpectedShopEstimator`, `BeamPlanner`, and `ExplanationBuilder`. The plugin must not instantiate a second candidate generator, and there is no benchmark-only ranking implementation. Add a behavior assertion that factory-created policies and directly exercised component fixtures return the same ordered action keys, confidence, planner status, fallback reason, model ID, and model payload checksum.

`RankingModelLoader.Load` is the boundary that translates malformed JSON, missing required model fields, incompatible schema/model versions, checksum mismatch, non-finite values, and other artifact-content failures into `ModelValidationException`. The factory catches only that exception and expected stream `IOException`; it does not catch `ArgumentNullException`, `InvalidOperationException`, `ObjectDisposedException`, or a general `Exception`. Constructor/composition defects must fail their tests visibly instead of being reported as `ModelUnavailable`.

Construct the orchestrator during plugin initialization and attempt to open `BobCoach.DecisionModels.linear-v1.envelope.json` by logical resource name. Pass that stream, or null when absent, through `DecisionPolicyFactory.Create`; do not create or embed a production model in this task. Replace only:

```csharp
var bestAction = SafeCall(() => _engine.GetBestAction(state), "GetBestAction");
```

with the following two-branch lifecycle (the existing action inference block is extracted into a pure helper and runs before the new recommendation):

```csharp
DecisionResult decisionResult;
if (_decisionLifecycle != null)
{
    var observedActionKeys = InferObservedActionKeys(_prevState, state);
    _decisionLifecycle.AttachObservedActions(observedActionKeys, UtcNowText());

    var candidates = _decisionPolicies.Candidates.Generate(state);
    var context = _decisionLifecycle.BeginOrReuse(state, candidates);
    decisionResult = SafeCall(
        () => _decisionOrchestrator.Decide(state, context.DecisionId), "Decide")
        ?? _legacyDecisionPolicy.Decide(state);
    _decisionEventRecorder.AttachRecommendation(
        context.DecisionId, decisionResult, UtcNowText());
}
else
{
    decisionResult = SafeCall(() => _decisionOrchestrator.Decide(state), "Decide")
        ?? _legacyDecisionPolicy.Decide(state);
}
var bestAction = decisionResult.SelectedAction;
```

`InferObservedActionKeys` reuses the current `_prevState` comparison logic and returns ordinal-distinct behavior evidence. It resolves to a Task 3 candidate action key only when the previous candidate set makes that mapping unambiguous; otherwise it emits an explicit unresolved evidence code, never fabricates a training label. Empty batches do nothing. In the recording branch, `BeginOrReuse` synchronously writes `DecisionCreated` before `Decide(state, decisionId)` lets the recorder write the Shadow comparison; `AttachRecommendation` always follows with the same ID. Re-evaluation of an unchanged frame reuses the ID and all three event types deduplicate. An observed action closes the previous decision before the next state is evaluated, even if the old state later reappears.

At `OnGameStart`, immediately after `_currentGameRecord = new GameRecord()`, create the recorder and lifecycle only when the existing local replay-recording option is active. Build the game ID from `_currentGameRecord.Timestamp` plus one process-local GUID, create the decision JSONL path through `BobCoachDataPaths`, and inject that recorder as `IShadowComparisonSink`. At `OnGameEnd`, attach any available combat/game outcome to the last relevant decision, then clear the tracker and recorder references. Do not add a second permanent session object, profile field, setting, or always-on file path. When local recording is inactive, keep both references null and inject `NoOpShadowComparisonSink.Instance`.

Keep mode fixed to `DecisionRuntimeMode.Shadow`; do not add a user-facing activation switch in this task. This adds no upload or network telemetry. The static runtime contract verifies both the recording and no-recording call paths so a future refactor cannot accidentally pass a blank ID to `DecisionEventRecorder`.

- [ ] **Step 5: Run focused behavior, build, and offline runtime checks**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_explanations.ps1`

Expected: `PASS explanations reflect score contributions only`.

Run: `node tests/test_offline_runtime_contract.js`

Expected: PASS with offline and shadow-mode assertions.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/build_release.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory "$env:TEMP\bobcoach-algorithm-shadow-build" -Force`

Expected: Release build succeeds for .NET Framework 4.7.2/x64 without an embedded production model; unified inference reports `ModelUnavailable`, the orchestrator records the shadow diagnostic when local replay recording is active, and the visible recommendation remains `legacy-v1.1.0`.

- [ ] **Step 6: Commit shadow integration**

```powershell
git add src/BobCoach/Core/Decision/ExplanationBuilder.cs src/BobCoach/Core/Decision/DecisionPolicyFactory.cs src/BobCoach/Core/Decision/UnifiedRankingPolicy.cs src/BobCoach/BobCoachPlugin.cs src/BobCoach/BobCoach.csproj tests/AlgorithmExplanationBehavior.cs tests/test_algorithm_explanations.ps1 tests/run_behavior_tests.ps1 tests/test_offline_runtime_contract.js
git commit -m "feat: integrate contribution-based ranking in shadow mode"
```

---

### Task 12: Calibrate 300 Scenarios And Enforce Activation Gates

**Files:**
- Create: `tools/model/PolicyBenchmarkRunner.cs`
- Create: `tools/model/run_policy_benchmark.ps1`
- Create: `tools/model/evaluate_policy.js`
- Create: `tests/fixtures/algorithm/evaluation.synthetic.json`
- Create: `tests/test_algorithm_benchmark_contract.ps1`
- Create: `tests/test_algorithm_evaluation.js`
- Create after all gates pass: `src/BobCoach/Resources/DecisionModels/linear-v1.envelope.json`
- Modify: `tests/run_contract_tests.ps1`
- Modify: `tests/test_offline_runtime_contract.js`
- Modify after all gates pass: `src/BobCoach/BobCoach.csproj`
- Modify: `docs/maintainer/ARCHITECTURE.md`
- Modify: `docs/maintainer/BUILD.md`
- Modify: `docs/README.md`

**Interfaces:**
- Consumes: Task 1 scenario and fingerprint contracts, Task 11 `DecisionPolicyFactory`, a checksum-valid external candidate envelope, frozen Legacy and Unified orchestrator modes, external labels, and explicit fallback probes.
- Produces: `benchmark-evidence-v1` baseline/candidate prediction JSONL, latency JSON, safety-probe JSON, aggregate `acceptance-report.json`, `activationDecision`, every required metric, every new severe error, and documented season-update/rollback procedures.

- [ ] **Step 1: Write failing benchmark-contract and metric-gate tests**

`tests/test_algorithm_benchmark_contract.ps1` invokes `run_policy_benchmark.ps1 -SelfTest` against synthetic states and a temporary directory, then asserts:

```powershell
$first = Join-Path $env:TEMP ("bobcoach-policy-evidence-a-" + [Guid]::NewGuid().ToString("N"))
$second = Join-Path $env:TEMP ("bobcoach-policy-evidence-b-" + [Guid]::NewGuid().ToString("N"))
& $runner -SelfTest -OutputDirectory $first
if ($LASTEXITCODE -ne 0) { throw 'first benchmark self-test failed' }
& $runner -SelfTest -OutputDirectory $second
if ($LASTEXITCODE -ne 0) { throw 'second benchmark self-test failed' }
foreach ($name in @('baseline-predictions.jsonl', 'candidate-predictions.jsonl', 'safety-probes.json')) {
    $a = (Get-FileHash (Join-Path $first $name) -Algorithm SHA256).Hash
    $b = (Get-FileHash (Join-Path $second $name) -Algorithm SHA256).Hash
    if ($a -ne $b) { throw "$name is not deterministic" }
}
if (Select-String -Path 'tools\model\PolicyBenchmarkRunner.cs' -Pattern 'DecisionEventRecorder') {
    throw 'benchmark runner must not instantiate gameplay recording'
}
```

The self-test uses in-memory fake policies only to verify serialization, pairing, fallback mapping, and byte stability. The normal runner path must use `DecisionPolicyFactory` and actual production policies.

```javascript
const report = evaluate(syntheticEvidence());
assert.strictEqual(report.metrics.severeErrorRelativeReduction, 0.25);
assert.strictEqual(report.metrics.top1ImprovementPercentagePoints, 12);
assert.strictEqual(report.activationDecision, 'activate');

for (const mutation of [
  evidence => evidence.candidate.severeErrors = evidence.baseline.severeErrors,
  evidence => evidence.metrics.top1ImprovementPercentagePoints = 9.9,
  evidence => evidence.metrics.top3ImprovementPercentagePoints = 14.9,
  evidence => evidence.categoryMetrics.sell.regressionPercentagePoints = -5.1,
  evidence => evidence.latency.rankingP95Milliseconds = 10.1,
  evidence => evidence.latency.planningP95Milliseconds = 25.1,
  evidence => evidence.fallbackPassRate = 0.999,
  evidence => evidence.newSevereErrors.push({ scenarioId: 'new-severe' })
]) assert.strictEqual(evaluate(mutationClone(mutation)).activationDecision, 'shadow');
```

- [ ] **Step 2: Run and verify benchmark and evaluator tools are absent**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_benchmark_contract.ps1`

Expected: FAIL because `tools/model/run_policy_benchmark.ps1` and `PolicyBenchmarkRunner.cs` do not exist.

Run: `node tests/test_algorithm_evaluation.js`

Expected: FAIL because `tools/model/evaluate_policy.js` does not exist.

- [ ] **Step 3: Implement same-state production-policy evidence generation**

Define one prediction line per policy/scenario with this exact versioned shape:

```json
{
  "evidenceSchemaVersion": 1,
  "benchmarkId": "season-2026-next-v1",
  "scenarioId": "scenario-0001",
  "gameId": "game-0001",
  "stateFingerprint": "sha256-lower-hex",
  "candidateFingerprint": "sha256-lower-hex",
  "policyId": "legacy-v1.1.0",
  "modelId": "",
  "modelPayloadSha256": "",
  "selectedActionKey": "Upgrade|-1||",
  "topActionKeys": ["Upgrade|-1||"],
  "confidence": 1.0,
  "plannerStatus": "NotRequested",
  "fallbackReason": "None"
}
```

`PolicyBenchmarkRunner` must deserialize every labeled state into a fresh detached `GameState`, generate and ordinal-sort candidates with the production `CandidateGenerator`, and reject the scenario unless the resulting state and candidate fingerprints equal the label file. For each accepted scenario it constructs both policies once through `DecisionPolicyFactory`, then:

```csharp
var baseline = new DecisionOrchestrator(bundle.Legacy, bundle.Unified,
    NoOpShadowComparisonSink.Instance, DecisionRuntimeMode.LegacyOnly).Decide(Clone(state));
var candidate = new DecisionOrchestrator(bundle.Legacy, bundle.Unified,
    NoOpShadowComparisonSink.Instance, DecisionRuntimeMode.Unified).Decide(Clone(state));
WritePrediction(baselineWriter, scenario, baseline, "", "");
WritePrediction(candidateWriter, scenario, candidate, bundle.ModelId, bundle.ModelPayloadSha256);
```

The runner obtains both fingerprints through production `DecisionEventRecord.Create(scenarioId, gameId, state, candidates)` and compares its canonical values with the label row; it must not reproduce canonical JSON or SHA-256 logic inside the tool. The candidate line therefore reflects the real orchestration fallback result, not a raw score that the plugin could never display. `topActionKeys` contains at most three keys from the returned result in rank order. When unified inference falls back, candidate Top-1/Top-3 are intentionally computed from the returned legacy result because that is the action surface a visible unified mode would expose; raw unified ranks may appear only in a separate diagnostic field excluded from activation metrics. The runner sorts scenarios by ordinal `scenarioId`, rejects duplicate scenario or fingerprint pairs, rejects mixed schema/season/build identities, and writes UTF-8 without BOM and LF line endings. It never enables `DecisionEventRecorder`, never uploads data, and never writes inside the repository.

After three unmeasured warmups per scenario, measure ten fresh-clone calls per policy with `Stopwatch.GetTimestamp`; store raw samples plus P50/P95/P99 in `latency.json`. Timing values are expected to vary, so determinism checks compare prediction and safety files byte-for-byte and compare only the latency schema, sample counts, scenario identities, and finite/non-negative constraints.

The external fallback-probe JSONL has this exact input contract: `{ "probeSchemaVersion": 1, "probeId": "model-unavailable", "gameId": "synthetic-probe-game", "state": {}, "candidates": [], "stateFingerprint": "sha256-lower-hex", "candidateFingerprint": "sha256-lower-hex", "modelSource": "none", "expectedFallbackReason": "ModelUnavailable", "expectedPlannerStatus": "NotRequested" }`. `modelSource` is exactly `none` or `candidate`; the runner rejects duplicate probe IDs, unknown enum strings, fingerprint mismatch, non-synthetic game IDs, missing required reasons, and any path or payload field. Probe states and candidates are fully materialized rather than expressed as mutation instructions. The required fallback reasons are `ModelUnavailable`, `CriticalStateMissing`, `UnsupportedMechanic`, `OutOfDistribution`, `LowConfidence`, and `NoSafeAction`; model-unavailable passes a null stream through the production factory, while the others use the exact evaluated candidate bytes.

`safety-probes.json` is one canonical object: `{ "safetyEvidenceSchemaVersion": 1, "benchmarkId": "season-2026-next-v1", "probes": [{ "probeId": "model-unavailable", "stateFingerprint": "sha256-lower-hex", "candidateFingerprint": "sha256-lower-hex", "modelSource": "none", "expectedFallbackReason": "ModelUnavailable", "actualFallbackReason": "ModelUnavailable", "expectedPlannerStatus": "NotRequested", "actualPlannerStatus": "NotRequested", "passed": true }] }`. Probe rows are ordinally sorted and `passed` requires both fallback and planner status to match. Planner timeout is tested as an additional row with `expectedFallbackReason: "None"` and `expectedPlannerStatus: "TimedOut"`; it is a safe completed-path gate, not a fallback reason.

- [ ] **Step 4: Implement the external-root runner wrapper**

`run_policy_benchmark.ps1` exposes exactly:

```powershell
param(
    [string]$DataRoot,
    [string]$Scenarios,
    [string]$SafetyProbes,
    [string]$CandidateModel,
    [string]$OutputDirectory,
    [string]$HdtDirectory = $env:BOBCOACH_HDT_DIR,
    [switch]$SelfTest
)
```

In normal mode it resolves `DataRoot`, rejects repository-contained and system-drive roots using the same rules as `assertExternalDataRoot`, and resolves all other paths beneath that root. It fails if a path escapes the root. It builds `BobCoach.csproj` in Release/x64 with the explicit HDT directory, locates Roslyn using `BOBCOACH_CSC` or `vswhere.exe`, compiles `PolicyBenchmarkRunner.cs` against the just-built `BobCoach.dll`, `Newtonsoft.Json.dll`, `HearthstoneDeckTracker.exe`, `HearthDb.dll`, `System.dll`, and `System.Core.dll`, stages only those runtime dependencies in a GUID-named directory under `$env:TEMP`, runs the console, propagates its exit code, and removes that temporary directory in `finally`. It never copies external scenarios, candidate models, or evidence into the repository or temporary build directory.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_benchmark_contract.ps1`

Expected: `PASS benchmark evidence is paired, deterministic, recorder-free, and versioned`.

- [ ] **Step 5: Implement exact metrics and fail-closed activation from evidence**

```javascript
const gates = {
  severeErrorRelativeReduction: metrics.severeErrorRelativeReduction >= 0.20,
  top1: metrics.top1ImprovementPercentagePoints >= 10,
  top3: metrics.top3ImprovementPercentagePoints >= 15,
  categories: Object.values(categoryMetrics).every(x => x.regressionPercentagePoints >= -5),
  rankingLatency: latency.rankingP95Milliseconds <= 10,
  planningLatency: latency.planningP95Milliseconds <= 25,
  fallback: fallbackPassRate === 1,
  severeRegressions: newSevereErrors.length === 0
};
const activationDecision = Object.values(gates).every(Boolean) ? 'activate' : 'shadow';
```

`evaluate_policy.js` accepts only `--data-root`, `--scenarios`, `--baseline-evidence`, `--candidate-evidence`, `--latency-evidence`, `--safety-evidence`, and `--output`. It does not import C# policy logic. It joins baseline, candidate, and labels by `scenarioId`, then requires exact state/candidate fingerprints, one baseline policy ID, one non-empty candidate model ID/checksum, and identical scenario sets before calculating severe-error rate, relative reduction, accepted Top-1, Top-3 coverage, severity-weighted regret, per-category regression, override precision, fallback pass rate, and P50/P95/P99 latency.

It rejects duplicated fingerprints, mixed schema versions, missing identities, games split across partitions, missing required safety reasons, non-finite latency, and fewer than 300 evaluable high-impact scenarios. `activationDecision: "activate"` means only that the exact artifact bytes are eligible to become the embedded shadow candidate; it does not change `DecisionRuntimeMode.Shadow` or authorize visible unified recommendations.

- [ ] **Step 6: Build the external benchmark, candidate model, and comparable evidence**

Create the external layout under an explicit non-repository path:

```text
<BOBCOACH_DATA_ROOT>/
  inputs/decision-events.jsonl
  labels/scenarios-v1.jsonl
  probes/fallback-scenarios-v1.jsonl
  datasets/pairs-v1.jsonl
  splits/train.jsonl
  splits/validation.jsonl
  splits/holdout.jsonl
  models/candidates/
  evidence/linear-v1/
    baseline-predictions.jsonl
    candidate-predictions.jsonl
    latency.json
    safety-probes.json
  reports/linear-v1-acceptance.json
```

Label at least 300 deduplicated, evaluable high-impact scenarios across level, refresh, buy, sell, freeze, play, and transition, plus the six explicit fallback probes. Accepted answers are sets with severity grades; observed player behavior remains a separate field. Keep complete games together, reserve newer games for holdout, and report old/current season separately.

Run:

```powershell
if ([string]::IsNullOrWhiteSpace($env:BOBCOACH_DATA_ROOT)) { throw 'BOBCOACH_DATA_ROOT is required' }
$dataRoot = [System.IO.Path]::GetFullPath($env:BOBCOACH_DATA_ROOT)
$repoRoot = [System.IO.Path]::GetFullPath((git rev-parse --show-toplevel).Trim())
$repoPrefix = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($dataRoot.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    $dataRoot.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'BOBCOACH_DATA_ROOT must be outside the repository' }
if ([System.IO.Path]::GetPathRoot($dataRoot).Equals(($env:SystemDrive + [System.IO.Path]::DirectorySeparatorChar), [System.StringComparison]::OrdinalIgnoreCase)) { throw 'BOBCOACH_DATA_ROOT must not be on the system drive' }
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

node tools/model/build_pairs.js --data-root $dataRoot --scenarios "labels\scenarios-v1.jsonl" --output "datasets\pairs-v1.jsonl"
node tools/model/split_dataset.js --data-root $dataRoot --input "datasets\pairs-v1.jsonl" --output "splits"
node tools/model/train_pairwise.js --data-root $dataRoot --pairs "splits\train.jsonl" --output "models\candidates\linear-v1.envelope.json" --epochs 400 --learning-rate 0.03 --lambda 0.01 --seed 7
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/model/run_policy_benchmark.ps1 `
  -DataRoot $dataRoot `
  -Scenarios "labels\scenarios-v1.jsonl" `
  -SafetyProbes "probes\fallback-scenarios-v1.jsonl" `
  -CandidateModel "models\candidates\linear-v1.envelope.json" `
  -OutputDirectory "evidence\linear-v1" `
  -HdtDirectory $env:BOBCOACH_HDT_DIR
node tools/model/evaluate_policy.js `
  --data-root $dataRoot `
  --scenarios "labels\scenarios-v1.jsonl" `
  --baseline-evidence "evidence\linear-v1\baseline-predictions.jsonl" `
  --candidate-evidence "evidence\linear-v1\candidate-predictions.jsonl" `
  --latency-evidence "evidence\linear-v1\latency.json" `
  --safety-evidence "evidence\linear-v1\safety-probes.json" `
  --output "reports\linear-v1-acceptance.json"
```

Expected: every tool revalidates the explicit root; baseline and candidate evidence contain the exact same ordered scenario/state/candidate identities; the evaluator exits `0` for a structurally valid report even when `activationDecision` is `shadow`. Player-observed actions remain context only and are never converted into accepted-action labels. The split keeps whole games together, places newer whole games in holdout, and reports old-season and current-season results separately.

- [ ] **Step 7: Conditionally embed only the exact accepted candidate**

After, and only after, the report says `activate` and a human review confirms every newly introduced severe-error list is empty, copy the exact evaluated bytes into the repository and embed them:

```powershell
$report = Get-Content (Join-Path $dataRoot 'reports\linear-v1-acceptance.json') -Raw | ConvertFrom-Json
if ($report.activationDecision -ne 'activate' -or $report.newSevereErrors.Count -ne 0) { throw 'candidate is not eligible for embedding' }
$resourceDir = Join-Path $repoRoot 'src\BobCoach\Resources\DecisionModels'
New-Item -ItemType Directory -Force -Path $resourceDir | Out-Null
Copy-Item -LiteralPath (Join-Path $dataRoot 'models\candidates\linear-v1.envelope.json') -Destination (Join-Path $resourceDir 'linear-v1.envelope.json')
```

Add the old-style project resource entry explicitly:

```xml
<EmbeddedResource Include="Resources\DecisionModels\linear-v1.envelope.json">
  <LogicalName>BobCoach.DecisionModels.linear-v1.envelope.json</LogicalName>
</EmbeddedResource>
```

Reload the copied envelope with `RankingModelLoader`, recompute its identity, and fail unless checksum, model ID, feature schema, training-set identity, season range, and metrics equal the acceptance report. The plugin remains hard-coded to `DecisionRuntimeMode.Shadow`; visible activation requires a later, separately approved release change.

If the report says `shadow`, do not create `Resources/DecisionModels`, do not modify `BobCoach.csproj`, and do not suppress the failed gate. The evaluator, runner, tests, and maintenance documentation are still a complete deliverable and are committed so the next candidate can reuse the same acceptance chain.

- [ ] **Step 8: Document maintenance, new-season, and rollback flows**

Add to `ARCHITECTURE.md`: policy boundary, data flow, feature/schema ownership, confidence/fallback order, and the fact that `ProfileEngine` is excluded from quality ranking. Add to `BUILD.md`: explicit external data root, model validation/training/evaluation commands, latency harness, and full repository checks. Add to `docs/README.md`: links to the approved design, this plan, and architecture section.

The new-season checklist must say:

```text
1. Update state extraction and season mechanic semantics.
2. Mark unknown mechanics unsupported until tests define coverage.
3. Keep unified ranking in shadow mode.
4. Label current-season disagreements in high-impact categories.
5. Re-run all activation gates on the current-season holdout.
6. Embed a candidate only when every gate passes; keep it in Shadow until a separate owner-approved visible-activation release.
7. On a failed gate, retain the report externally, commit the reusable tools/docs, and leave the model resource absent.
8. Roll back by selecting LegacyOnly and retaining the last approved model.
```

- [ ] **Step 9: Run all tests and repository validation in both artifact states**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_algorithm_benchmark_contract.ps1`

Expected: `PASS benchmark evidence is paired, deterministic, recorder-free, and versioned`.

Run: `node tests/test_algorithm_evaluation.js`

Expected: `PASS evaluation gates fail closed on quality and latency regressions`.

Run: `npm test`

Expected: all contract, behavior, and package suites pass.

Run: `node tests/test_offline_runtime_contract.js`

Expected without an accepted model: optional resource absence is valid, unified inference resolves to `ModelUnavailable`, the plugin remains fixed to Shadow, and visible output comes from `legacy-v1.1.0`.

Expected after conditional embedding: the approved resource identity matches the acceptance report, the plugin remains fixed to Shadow, and visible output still comes from `legacy-v1.1.0`.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/build_release.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory "$env:TEMP\bobcoach-algorithm-approved-shadow-build" -Force`

Expected without an accepted model: Release build succeeds for .NET Framework 4.7.2/x64 with safe `ModelUnavailable` shadow fallback.

Expected after conditional embedding: Release build succeeds and contains only the checksum-validated approved shadow model.

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/validate_repository.ps1`

Expected: repository content, sensitive-data, personal-path, replay-data, large-file, release identity, and package allowlist checks pass.

Run: `git diff --check`

Expected: no output and exit code `0`.

- [ ] **Step 10: Commit reusable gates regardless of candidate outcome**

```powershell
git add tools/model/PolicyBenchmarkRunner.cs tools/model/run_policy_benchmark.ps1 tools/model/evaluate_policy.js tests/fixtures/algorithm/evaluation.synthetic.json tests/test_algorithm_benchmark_contract.ps1 tests/test_algorithm_evaluation.js tests/run_contract_tests.ps1 tests/test_offline_runtime_contract.js docs/maintainer/ARCHITECTURE.md docs/maintainer/BUILD.md docs/README.md
if (Test-Path 'src\BobCoach\Resources\DecisionModels\linear-v1.envelope.json') {
    git add src/BobCoach/Resources/DecisionModels/linear-v1.envelope.json src/BobCoach/BobCoach.csproj
}
git commit -m "test: enforce algorithm activation gates"
```

## Final Activation Review

- [ ] Confirm the benchmark contains at least 300 deduplicated, evaluable high-impact scenarios and no real gameplay data is staged.
- [ ] Confirm `legacy-v1.1.0` and the candidate run against the exact same scenario and candidate fingerprints.
- [ ] Confirm severe-error relative reduction is at least 20% and review every new severe error individually.
- [ ] Confirm Top-1 improves by at least 10 percentage points and Top-3 by at least 15 percentage points.
- [ ] Confirm no high-impact category regresses by more than 5 percentage points.
- [ ] Confirm ranking P95 is at most 10 ms and planned-decision P95 is at most 25 ms.
- [ ] Confirm `ModelUnavailable` (including corrupt model), `CriticalStateMissing`, `UnsupportedMechanic`, `OutOfDistribution`, `LowConfidence`, and `NoSafeAction` fallback probes pass 100%.
- [ ] Confirm planner timeout returns `PlannerStatus.TimedOut`, retains the best safe completed path, and does not invent a fallback reason.
- [ ] Confirm the candidate model checksum, feature schema, training-set identity, season range, and aggregate metrics match the acceptance report.
- [ ] Confirm the plugin still starts in shadow mode. Switching to visible unified mode is a separate owner-approved release decision.

## Expected Outcome

The minimum activation outcome is at least 20% fewer severe errors, at least +10 percentage points Top-1 accuracy, and at least +15 percentage points Top-3 coverage versus v1.1.0. The stage-one planning target is 25%-35% fewer severe errors and +12 to +18 percentage points Top-1; average placement improvement of 0.15-0.35 and top-four improvement of 3-5 percentage points remain long-term hypotheses, not release claims. An optional LightGBM/LambdaMART shadow experiment is authorized only after this plan is complete, the event-aligned dataset and 300-scenario benchmark are stable, and a tree model adds at least 3 held-out Top-1 percentage points without quality, calibration, latency, offline, or packaging regression.

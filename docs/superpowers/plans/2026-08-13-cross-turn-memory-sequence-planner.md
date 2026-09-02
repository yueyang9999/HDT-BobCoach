# Cross-Turn Strategic Memory and Sequence Planner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic Shadow-only cross-turn memory and bounded action-sequence planner, record it in global replays, and prove its engineering contracts without changing the visible legacy policy.

**Architecture:** A compact `StrategicMemory` retains observed core/pair evidence and the previous plan. A pure beam search expands short action sequences. A game adapter uses the existing `ActionEnumerator`, `Simulator`, `TurnProjector`, and `MainlineStrategyPlanner` to build candidate sequences and score them by board power, core completion, future gold, hand utility, survival risk, and opponent tech pressure.

**Tech Stack:** C# 7.3, .NET Framework 4.7.2, no new NuGet dependencies, Node.js standard library for replay analyzer tests.

## Global Constraints

- Keep the plugin offline and privacy-preserving. No network calls or cross-user data collection.
- Keep all new planning behavior in Shadow. `legacy-v1.1.0` remains the visible policy.
- Player actions remain observations, never correctness labels.
- Synthetic tests may prove engineering determinism and interfaces only, not strategy superiority.
- Do not add mandatory external dependencies.
- Do not stage, commit, push, rebase, or rewrite existing user changes in this repository.
- Run TDD for every new behavior: watch the test fail, implement, watch it pass.
- Every completed task must pass its exact verification command.
- Generated replay/evidence files remain outside the Git repository.

---

## File Map

- Create `src/BobCoach/Core/Decision/StrategicPlan.cs`
- Create `src/BobCoach/Core/Decision/StrategicMemory.cs`
- Create `src/BobCoach/Core/Decision/SequenceSearchPlanner.cs`
- Create `src/BobCoach/Core/Decision/StrategySequencePlanner.cs`
- Modify `src/BobCoach/Core/DecisionEngine.cs`
- Modify `src/BobCoach/BobCoachPlugin.cs`
- Modify `tools/evaluate/global_replay_analyzer.js`
- Modify `tests/test_strategic_plan_contract.ps1`
- Create `tests/StrategicPlanContractBehavior.cs`
- Modify `tests/test_ui_lifecycle.js`
- Modify `tests/test_global_replay_analyzer.js`
- Modify `docs/maintainer/USER_FEEDBACK_ISSUES.md`
- Modify `docs/maintainer/2026-08-12-algorithm-roi-roadmap.md`

## Interfaces

`StrategicPlan.cs` produces:

```csharp
public enum StrategyObjective
{
    Flexible,
    Economy,
    SearchCore,
    Stabilize,
    TechPivot,
    Survival
}

public sealed class PlannedStep
{
    public GameAction Action;
    public double Value;
    public string Reason = "";
}

public sealed class StrategyPlan
{
    public StrategyObjective Objective;
    public string CompDir = "";
    public string GuideId = "";
    public List<PlannedStep> Steps = new List<PlannedStep>();
    public double ExpectedValue;
    public double Risk;
    public string Reason = "";
    public string FallbackActionKey = "";
    public string PlanHash = "";
}
```

`StrategicMemory.cs` produces:

```csharp
public sealed class StrategicMemorySnapshot
{
    public int PlayerId;
    public int LastTurn;
    public string CompDir = "";
    public string GuideId = "";
    public string Objective = "Flexible";
    public HashSet<string> ObservedCoreCardIds =
        new HashSet<string>(StringComparer.Ordinal);
    public HashSet<string> ObservedPairCardIds =
        new HashSet<string>(StringComparer.Ordinal);
    public string LastBoardSignature = "";
    public string LastOpponentProfileKey = "";
    public string LastPlanHash = "";
    public string LastPlanSequence = "";
}
```

`SequenceSearchPlanner.cs` produces:

```csharp
public static List<PlannedStep> Search(
    IReadOnlyList<PlannedStep> roots,
    Func<PlannedStep, IReadOnlyList<PlannedStep>> expand,
    Func<IReadOnlyList<PlannedStep>, double> score,
    int maxDepth,
    int beamWidth)
```

`StrategySequencePlanner.cs` produces:

```csharp
public StrategyPlan Build(
    GameState state,
    StrategicMemorySnapshot memory,
    MainlinePlan mainline)
```

`DecisionEngine` exposes:

```csharp
public StrategyPlan LastStrategyPlan { get; private set; }
public StrategicMemorySnapshot StrategicMemorySnapshot =>
    _strategyMemory.Snapshot;
```

---

### Task 1: Plan and Memory Contracts

**Files:**
- Create: `src/BobCoach/Core/Decision/StrategicPlan.cs`
- Create: `src/BobCoach/Core/Decision/StrategicMemory.cs`
- Create: `tests/StrategicPlanContractBehavior.cs`
- Create: `tests/test_strategic_plan_contract.ps1`

**Consumes:** Existing `GameAction`, `MainlinePlan`, and `GameState` types.

**Produces:** `StrategicPlan`, `PlannedStep`, `StrategyObjective`, `StrategicMemory`, `StrategicMemorySnapshot`.

- [x] **Step 1: Write the failing contract test**

Create `tests/StrategicPlanContractBehavior.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class StrategicPlanContractBehavior
{
    private static int Main()
    {
        try
        {
            PlanHashIsDeterministic();
            MemoryRetainsObservedCoresAcrossTurns();
            MemoryTracksPairsWithoutDuplication();
            MemoryCopyIsDefensive();
            Console.WriteLine("PASS strategic plan contract");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void PlanHashIsDeterministic()
    {
        var first = Plan();
        var second = Plan();
        AssertTrue(StrategyPlanText.BuildPlanHash(first)
            == StrategyPlanText.BuildPlanHash(second), "deterministic hash");
    }

    private static void MemoryRetainsObservedCoresAcrossTurns()
    {
        var memory = new StrategicMemory();
        memory.Observe(State(turn: 1), Mainline("Beast", "core-a"), Plan("Economy"));
        memory.Observe(State(turn: 2), Mainline("Beast", "core-b"), Plan("SearchCore"));
        var snapshot = memory.Snapshot;
        AssertTrue(snapshot.ObservedCoreCardIds.Contains("core-a"), "old core");
        AssertTrue(snapshot.ObservedCoreCardIds.Contains("core-b"), "new core");
        AssertTrue(snapshot.LastTurn == 2, "turn");
        AssertTrue(snapshot.Objective == "SearchCore", "objective");
    }

    private static void MemoryTracksPairsWithoutDuplication()
    {
        var memory = new StrategicMemory();
        memory.Observe(PairState(), Mainline("Pirate"), Plan("Flexible"));
        memory.Observe(PairState(), Mainline("Pirate"), Plan("Flexible"));
        var snapshot = memory.Snapshot;
        AssertTrue(snapshot.ObservedPairCardIds.Count == 1, "pair dedupe");
        AssertTrue(snapshot.ObservedPairCardIds.Contains("busker"), "busker pair");
    }

    private static void MemoryCopyIsDefensive()
    {
        var memory = new StrategicMemory();
        memory.Observe(State(1), Mainline("Beast", "core-a"), Plan("Economy"));
        var snapshot = memory.Snapshot;
        snapshot.ObservedCoreCardIds.Add("external");
        AssertFalse(memory.Snapshot.ObservedCoreCardIds.Contains("external"), "copy");
    }

    private static StrategyPlan Plan(string objective = "Economy")
    {
        return new StrategyPlan
        {
            Objective = (StrategyObjective)Enum.Parse(
                typeof(StrategyObjective), objective),
            CompDir = "Pirate",
            Steps = new List<PlannedStep>
            {
                new PlannedStep
                {
                    Action = new GameAction
                    {
                        Type = ActionType.BuyMinion,
                        TargetIndex = 0,
                        CardId = "busker"
                    },
                    Reason = "economy"
                }
            }
        };
    }

    private static GameState State(int turn)
    {
        return new GameState
        {
            PlayerId = 1,
            Turn = turn,
            BoardMinions = new List<MinionData>
            {
                new MinionData { CardId = "core-a", Tier = 1 }
            }
        };
    }

    private static GameState PairState()
    {
        return new GameState
        {
            PlayerId = 1,
            Turn = 1,
            BoardMinions = new List<MinionData>
            {
                new MinionData { CardId = "busker", Tier = 1 },
                new MinionData { CardId = "busker", Tier = 1 }
            }
        };
    }

    private static MainlinePlan Mainline(
        string compDir, string coreCardId = null)
    {
        var plan = new MainlinePlan { CompDir = compDir };
        if (!string.IsNullOrEmpty(coreCardId))
            plan.CoreCardIds.Add(coreCardId);
        return plan;
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool value, string label)
    {
        if (value) throw new InvalidOperationException(label);
    }
}
```

Create `tests/test_strategic_plan_contract.ps1`:

```powershell
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$candidates = @()
if ($env:BOBCOACH_CSC) { $candidates += $env:BOBCOACH_CSC }
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path -LiteralPath $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe"
    if ($found) { $candidates += $found }
}
$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$csc) { throw "Roslyn csc.exe not found; install Visual Studio Build Tools or set BOBCOACH_CSC" }

$output = Join-Path $env:TEMP ("bobcoach_strategic_plan_{0}.exe" -f [Guid]::NewGuid().ToString("N"))
$sources = @(
    (Join-Path $root "src\BobCoach\Core\Decision\StrategicPlan.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\StrategicMemory.cs"),
    (Join-Path $PSScriptRoot "StrategicPlanContractBehavior.cs")
)
try {
    & $csc /nologo /langversion:7.3 /target:exe "/out:$output" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_strategic_plan_contract.ps1
```

Expected: compile failure because `StrategicPlan.cs` and `StrategicMemory.cs` do not exist.

- [x] **Step 3: Implement minimal contracts**

Create `src/BobCoach/Core/Decision/StrategicPlan.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public enum StrategyObjective
    {
        Flexible,
        Economy,
        SearchCore,
        Stabilize,
        TechPivot,
        Survival
    }

    public sealed class PlannedStep
    {
        public GameAction Action;
        public double Value;
        public string Reason = "";
    }

    public sealed class StrategyPlan
    {
        public StrategyObjective Objective = StrategyObjective.Flexible;
        public string CompDir = "";
        public string GuideId = "";
        public List<PlannedStep> Steps = new List<PlannedStep>();
        public double ExpectedValue;
        public double Risk;
        public string Reason = "";
        public string FallbackActionKey = "";
        public string PlanHash = "";
    }

    public static class StrategyPlanText
    {
        public static string ToCompactString(StrategyPlan plan)
        {
            if (plan == null) return "";
            var actions = plan.Steps == null
                ? ""
                : string.Join(";", plan.Steps
                    .Select(step => ActionKey(step != null ? step.Action : null)));
            return string.Join("|",
                plan.Objective.ToString(),
                plan.CompDir ?? "",
                plan.GuideId ?? "",
                actions,
                plan.Reason ?? "",
                plan.FallbackActionKey ?? "");
        }

        public static string BuildPlanHash(StrategyPlan plan)
        {
            string value = ToCompactString(plan);
            if (string.IsNullOrEmpty(value)) return "none";
            unchecked
            {
                int hash = 17;
                foreach (char ch in value)
                    hash = hash * 31 + ch;
                return hash.ToString("x8");
            }
        }

        private static string ActionKey(GameAction action)
        {
            if (action == null) return "-";
            return action.Type + ":" + action.TargetIndex + ":" + (action.CardId ?? "");
        }
    }
}
```

Create `src/BobCoach/Core/Decision/StrategicMemory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class StrategicMemorySnapshot
    {
        public int PlayerId;
        public int LastTurn;
        public string CompDir = "";
        public string GuideId = "";
        public string Objective = "Flexible";
        public HashSet<string> ObservedCoreCardIds =
            new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> ObservedPairCardIds =
            new HashSet<string>(StringComparer.Ordinal);
        public string LastBoardSignature = "";
        public string LastOpponentProfileKey = "";
        public string LastPlanHash = "";
        public string LastPlanSequence = "";

        public StrategicMemorySnapshot Copy()
        {
            return new StrategicMemorySnapshot
            {
                PlayerId = PlayerId,
                LastTurn = LastTurn,
                CompDir = CompDir ?? "",
                GuideId = GuideId ?? "",
                Objective = Objective ?? "Flexible",
                ObservedCoreCardIds = new HashSet<string>(
                    ObservedCoreCardIds ?? new HashSet<string>(),
                    StringComparer.Ordinal),
                ObservedPairCardIds = new HashSet<string>(
                    ObservedPairCardIds ?? new HashSet<string>(),
                    StringComparer.Ordinal),
                LastBoardSignature = LastBoardSignature ?? "",
                LastOpponentProfileKey = LastOpponentProfileKey ?? "",
                LastPlanHash = LastPlanHash ?? "",
                LastPlanSequence = LastPlanSequence ?? "",
            };
        }
    }

    public sealed class StrategicMemory
    {
        private StrategicMemorySnapshot _current = new StrategicMemorySnapshot();

        public StrategicMemorySnapshot Snapshot
        {
            get { return _current.Copy(); }
        }

        public void Reset()
        {
            _current = new StrategicMemorySnapshot();
        }

        public StrategicMemorySnapshot Observe(
            GameState state,
            MainlinePlan mainline,
            StrategyPlan strategyPlan)
        {
            if (state == null || mainline == null)
                return Snapshot;

            _current.PlayerId = state.PlayerId;
            _current.LastTurn = state.Turn;
            _current.CompDir = mainline.CompDir ?? "";
            _current.GuideId = mainline.GuideId ?? "";
            _current.Objective = strategyPlan != null
                ? strategyPlan.Objective.ToString()
                : "Flexible";
            _current.LastPlanHash = strategyPlan != null
                ? strategyPlan.PlanHash ?? ""
                : "";
            _current.LastPlanSequence = strategyPlan != null
                ? StrategyPlanText.ToCompactString(strategyPlan)
                : "";
            _current.LastBoardSignature = BuildBoardSignature(state);
            _current.LastOpponentProfileKey = BuildOpponentKey(state);

            AddObservedCores(state, mainline.CoreCardIds);
            AddObservedPairs(state);
            return Snapshot;
        }

        private void AddObservedCores(
            GameState state,
            ICollection<string> coreCardIds)
        {
            if (coreCardIds == null || coreCardIds.Count == 0) return;
            foreach (var minion in AllMinions(state))
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId))
                    continue;
                string baseId = NormalizeGolden(minion.CardId);
                if (coreCardIds.Contains(baseId)
                    || coreCardIds.Contains(minion.CardId))
                    _current.ObservedCoreCardIds.Add(baseId);
            }
        }

        private void AddObservedPairs(GameState state)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var minion in AllMinions(state))
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId))
                    continue;
                string baseId = NormalizeGolden(minion.CardId);
                int count;
                counts.TryGetValue(baseId, out count);
                counts[baseId] = count + 1;
            }
            foreach (var pair in counts)
                if (pair.Value >= 2)
                    _current.ObservedPairCardIds.Add(pair.Key);
        }

        private static IEnumerable<MinionData> AllMinions(GameState state)
        {
            if (state.BoardMinions != null)
                foreach (var minion in state.BoardMinions)
                    yield return minion;
            if (state.HandMinions != null)
                foreach (var minion in state.HandMinions)
                    yield return minion;
        }

        private static string NormalizeGolden(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            return cardId.EndsWith("_G", StringComparison.Ordinal)
                ? cardId.Substring(0, cardId.Length - 2)
                : cardId;
        }

        private static string BuildBoardSignature(GameState state)
        {
            var ids = new List<string>();
            if (state.BoardMinions != null)
                ids.AddRange(state.BoardMinions
                    .Where(minion => minion != null)
                    .Select(minion => NormalizeGolden(minion.CardId)
                        + ":" + minion.Position));
            ids.Sort(StringComparer.Ordinal);
            return state.Turn + "|" + state.TavernTier + "|"
                + string.Join(",", ids);
        }

        private static string BuildOpponentKey(GameState state)
        {
            if (state.Opponents == null) return "none";
            var stats = state.Opponents
                .Where(opponent => opponent != null && opponent.Alive)
                .Select(opponent => opponent.Health + "|"
                    + (opponent.BoardMinions != null
                        ? opponent.BoardMinions.Count : 0))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return stats.Length == 0 ? "none" : string.Join(";", stats);
        }
    }
}
```

`tests/StrategicPlanContractBehavior.cs` must define lightweight `GameState`, `MinionData`, `GameAction`, `ActionType`, `MainlinePlan`, `OpponentData`, and `HashSet` already imported. The production files use these types; the test file must provide stubs in namespace `BobCoach.Engine`.

- [x] **Step 4: Run test to verify it passes**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_strategic_plan_contract.ps1
```

Expected: `PASS strategic plan contract`

- [x] **Step 5: Record verification, do not commit**

Run:

```powershell
git diff --check -- src/BobCoach/Core/Decision/StrategicPlan.cs src/BobCoach/Core/Decision/StrategicMemory.cs tests/StrategicPlanContractBehavior.cs tests/test_strategic_plan_contract.ps1
```

Expected: exit 0, only existing line-ending notices.

---

### Task 2: Pure Bounded Sequence Search

**Files:**
- Create: `src/BobCoach/Core/Decision/SequenceSearchPlanner.cs`
- Modify: `tests/StrategicPlanContractBehavior.cs`

**Interfaces:**
- Consumes: `PlannedStep` from Task 1.
- Produces: `SequenceSearchPlanner.Search(...)`.

- [x] **Step 1: Add failing search tests**

Append to `tests/StrategicPlanContractBehavior.cs`:

```csharp
SearchKeepsBestBoundedSequence();
SearchPrunesWorseBeamCandidates();
```

Add:

```csharp
private static void SearchKeepsBestBoundedSequence()
{
    var root = Step("a", 0);
    var result = SequenceSearchPlanner.Search(
        new[] { root },
        step => new[] { Step(step.Action.CardId + "-1", 10), Step(step.Action.CardId + "-2", 1) },
        steps => steps.Sum(step => step.Value),
        maxDepth: 3,
        beamWidth: 2);
    AssertTrue(result.Count == 3, "sequence length");
    AssertTrue(result[1].Action.CardId == "a-1", "best branch");
}

private static void SearchPrunesWorseBeamCandidates()
{
    var result = SequenceSearchPlanner.Search(
        new[] { Step("low", 1), Step("high", 9) },
        step => new PlannedStep[0],
        steps => steps.Sum(step => step.Value),
        maxDepth: 2,
        beamWidth: 1);
    AssertTrue(result.Count == 1, "beam width");
    AssertTrue(result[0].Action.CardId == "high", "best root");
}

private static PlannedStep Step(string cardId, double value)
{
    return new PlannedStep
    {
        Action = new GameAction
        {
            Type = ActionType.BuyMinion,
            CardId = cardId,
            TargetIndex = 0
        },
        Value = value,
        Reason = "test"
    };
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_strategic_plan_contract.ps1
```

Expected: compile failure because `SequenceSearchPlanner` does not exist.

- [x] **Step 3: Implement minimal beam search**

Create `src/BobCoach/Core/Decision/SequenceSearchPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public static class SequenceSearchPlanner
    {
        public static List<PlannedStep> Search(
            IReadOnlyList<PlannedStep> roots,
            Func<PlannedStep, IReadOnlyList<PlannedStep>> expand,
            Func<IReadOnlyList<PlannedStep>, double> score,
            int maxDepth,
            int beamWidth)
        {
            if (roots == null || roots.Count == 0 || score == null)
                return new List<PlannedStep>();
            if (maxDepth < 1) maxDepth = 1;
            if (beamWidth < 1) beamWidth = 1;

            var beam = roots
                .Where(step => step != null)
                .Select(step => new List<PlannedStep> { step })
                .OrderByDescending(sequence => score(sequence))
                .ThenBy(sequence => SequenceKey(sequence), StringComparer.Ordinal)
                .Take(beamWidth)
                .ToList();

            for (int depth = 1; depth < maxDepth; depth++)
            {
                var candidates = new List<List<PlannedStep>>(beam);
                if (expand != null)
                {
                    foreach (var sequence in beam)
                    {
                        var last = sequence[sequence.Count - 1];
                        foreach (var next in expand(last) ?? new PlannedStep[0])
                        {
                            var copy = new List<PlannedStep>(sequence) { next };
                            candidates.Add(copy);
                        }
                    }
                }
                if (candidates.Count == 0) break;
                beam = candidates
                    .OrderByDescending(sequence => score(sequence))
                    .ThenBy(sequence => SequenceKey(sequence), StringComparer.Ordinal)
                    .Take(beamWidth)
                    .ToList();
            }

            return beam.Count == 0
                ? new List<PlannedStep>()
                : beam[0];
        }

        private static string SequenceKey(IEnumerable<PlannedStep> sequence)
        {
            return string.Join(";", sequence.Select(step =>
                step == null || step.Action == null
                    ? "-"
                    : step.Action.Type + ":" + step.Action.TargetIndex
                        + ":" + (step.Action.CardId ?? "")));
        }
    }
}
```

- [x] **Step 4: Add production source to test runner**

Modify `tests/test_strategic_plan_contract.ps1` source list:

```powershell
    (Join-Path $root "src\BobCoach\Core\Decision\SequenceSearchPlanner.cs"),
```

- [x] **Step 5: Run test to verify it passes**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_strategic_plan_contract.ps1
```

Expected: `PASS strategic plan contract`

---

### Task 3: Game Adapter and DecisionEngine Integration

**Files:**
- Create: `src/BobCoach/Core/Decision/StrategySequencePlanner.cs`
- Modify: `src/BobCoach/Core/DecisionEngine.cs`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**
- Consumes: `ActionEnumerator`, `Simulator`, `TurnProjector`, `MainlineStrategyPlanner`, `StrategicMemory`, `StrategyPlan`, `SequenceSearchPlanner`.
- Produces: `StrategySequencePlanner.Build(...)`, `DecisionEngine.LastStrategyPlan`, `DecisionEngine.StrategicMemorySnapshot`.

- [x] **Step 1: Add source-contract failing tests**

Add to `tests/test_ui_lifecycle.js`:

```javascript
test("[StrategicMemory] decision engine owns memory and exposes replay snapshot", () => {
    const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
    if (!engineSrc.includes("_strategyMemory = new StrategicMemory()"))
        return "DecisionEngine must own StrategicMemory";
    if (!engineSrc.includes("StrategicMemorySnapshot =>"))
        return "DecisionEngine must expose defensive memory snapshot";
    if (!engineSrc.includes("_strategySequencePlanner.Build"))
        return "DecisionEngine must invoke sequence planner";
});

test("[StrategicPlan] sequence planner expands simulated action chains", () => {
    const plannerSrc = readFile("src/BobCoach/Core/Decision/StrategySequencePlanner.cs");
    if (!plannerSrc.includes("SequenceSearchPlanner.Search"))
        return "game adapter must use pure sequence search";
    if (!plannerSrc.includes("_simulator.Simulate"))
        return "game adapter must simulate candidate steps";
    if (!plannerSrc.includes("TurnProjector.Advance"))
        return "game adapter must score future gold";
    if (!plannerSrc.includes("OpponentTechAdvisor.AnalyzeOpponentBoard"))
        return "game adapter must account for opponent tech pressure";
});
```

- [x] **Step 2: Run UI lifecycle test to verify it fails**

Run:

```powershell
node tests\test_ui_lifecycle.js
```

Expected: two new failures because the files/tokens do not exist.

- [x] **Step 3: Implement game adapter**

Create `src/BobCoach/Core/Decision/StrategySequencePlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class StrategySequencePlanner
    {
        private const int MaxDepth = 3;
        private const int BeamWidth = 8;
        private readonly ActionEnumerator _actions = new ActionEnumerator();
        private readonly Simulator _simulator = new Simulator();

        public StrategyPlan Build(
            GameState state,
            StrategicMemorySnapshot memory,
            MainlinePlan mainline)
        {
            if (state == null || !state.GameActive
                || mainline == null || state.NormalRecruitAvailable == false)
                return EmptyPlan(mainline);

            var objective = ResolveObjective(state, mainline);
            var roots = EnumerateRoots(state);
            var best = SequenceSearchPlanner.Search(
                roots,
                step => Expand(state, step),
                sequence => ScoreSequence(sequence, state, mainline),
                MaxDepth,
                BeamWidth);

            var plan = new StrategyPlan
            {
                Objective = objective,
                CompDir = mainline.CompDir ?? "",
                GuideId = mainline.GuideId ?? "",
                Steps = best ?? new List<PlannedStep>(),
                ExpectedValue = best != null
                    ? ScoreSequence(best, state, mainline)
                    : double.NegativeInfinity,
                Risk = CalculateRisk(state),
                Reason = DescribeObjective(objective, mainline),
                FallbackActionKey = Fallback(state),
            };
            plan.PlanHash = StrategyPlanText.BuildPlanHash(plan);
            return plan;
        }

        private List<PlannedStep> EnumerateRoots(GameState state)
        {
            return _actions.Enumerate(state, state.HeroCardId, false)
                .Select(action => new PlannedStep
                {
                    Action = action,
                    Reason = "candidate"
                })
                .ToList();
        }

        private IReadOnlyList<PlannedStep> Expand(
            GameState state,
            PlannedStep previous)
        {
            var next = _simulator.Simulate(state, previous.Action);
            if (next == null) return new List<PlannedStep>();
            return _actions.Enumerate(next, next.HeroCardId, false)
                .Select(action => new PlannedStep
                {
                    Action = action,
                    Reason = "follow-up"
                })
                .ToList();
        }

        private double ScoreSequence(
            IReadOnlyList<PlannedStep> sequence,
            GameState root,
            MainlinePlan mainline)
        {
            var current = root;
            double value = 0;
            double discount = 1.0;
            foreach (var step in sequence)
            {
                current = _simulator.Simulate(current, step.Action);
                if (current == null) return double.NegativeInfinity;
                value += discount * ScoreState(current, mainline);
                discount *= 0.82;
            }
            return value;
        }

        private double ScoreState(GameState state, MainlinePlan mainline)
        {
            double board = state.BoardMinions == null
                ? 0
                : state.BoardMinions.Sum(minion =>
                    minion == null
                        ? 0
                        : minion.Attack * 0.65
                            + minion.Health * 0.35
                            + minion.Tier * 0.25);
            double core = mainline.CoreCardIds == null
                ? 0
                : AllMinions(state).Count(minion =>
                    minion != null && IsCore(minion.CardId, mainline.CoreCardIds));
            double hand = state.HandMinions == null
                ? 0
                : state.HandMinions.Sum(minion =>
                    minion != null && IsUtilityText(minion.CardId, minion.CardName)
                        ? 0.6
                        : 0);
            double futureGold = TurnProjector.Advance(state)?.Gold ?? 0;
            double risk = CalculateRisk(state);
            double tech = OpponentTechAdvisor.IsTechCardWorthKeeping(
                "BG23_318", state.Opponents) ? 0.8 : 0;
            double opponentProfile = OpponentTechAdvisor
                .AnalyzeOpponentBoard(state.Opponents).HasBoardData ? 0.2 : 0;
            return board * 0.30
                + core * 1.30
                + hand * 0.35
                + futureGold * 0.12
                - risk * 0.50
                + tech
                + opponentProfile;
        }

        private static double CalculateRisk(GameState state)
        {
            double missingHealth = Math.Max(0, 12 - state.Health - state.Armor);
            double opponent = OpponentTechAdvisor
                .AnalyzeOpponentBoard(state.Opponents).MaxStats;
            double board = state.BoardMinions == null
                ? 0
                : state.BoardMinions.Sum(minion =>
                    minion == null ? 0 : minion.Attack + minion.Health);
            return missingHealth * 0.45
                + (opponent > board * 0.65 ? 0.8 : 0);
        }

        private static StrategyObjective ResolveObjective(
            GameState state, MainlinePlan mainline)
        {
            if (state.Health + state.Armor <= 12) return StrategyObjective.Survival;
            if (state.Turn <= 5) return StrategyObjective.Economy;
            if (mainline.MissingCoreCardNames.Count > 0)
                return StrategyObjective.SearchCore;
            var profile = OpponentTechAdvisor.AnalyzeOpponentBoard(state.Opponents);
            if (profile.BigThreatCount >= 2) return StrategyObjective.TechPivot;
            return StrategyObjective.Flexible;
        }

        private static string DescribeObjective(
            StrategyObjective objective, MainlinePlan mainline)
        {
            switch (objective)
            {
                case StrategyObjective.Survival:
                    return "health-and-board-survival";
                case StrategyObjective.Economy:
                    return "future-gold-and-hand-resource";
                case StrategyObjective.SearchCore:
                    return "find-missing-core";
                case StrategyObjective.TechPivot:
                    return "opponent-tech-pressure";
                case StrategyObjective.Stabilize:
                    return "stabilize-board";
                default:
                    return "flexible-mainline";
            }
        }

        private static string Fallback(GameState state)
        {
            if (state == null) return "-";
            return state.Gold >= 1
                ? "Refresh|-1|"
                : "FreezeShop|0|";
        }

        private static StrategyPlan EmptyPlan(MainlinePlan mainline)
        {
            return new StrategyPlan
            {
                CompDir = mainline != null ? mainline.CompDir ?? "" : "",
                GuideId = mainline != null ? mainline.GuideId ?? "" : "",
                Reason = "state-unavailable",
                PlanHash = "none"
            };
        }

        private static IEnumerable<MinionData> AllMinions(GameState state)
        {
            if (state.BoardMinions != null)
                foreach (var minion in state.BoardMinions)
                    yield return minion;
            if (state.HandMinions != null)
                foreach (var minion in state.HandMinions)
                    yield return minion;
        }

        private static bool IsCore(
            string cardId, ICollection<string> coreCardIds)
        {
            if (string.IsNullOrEmpty(cardId) || coreCardIds == null) return false;
            return coreCardIds.Contains(cardId)
                || (cardId.EndsWith("_G", StringComparison.Ordinal)
                    && coreCardIds.Contains(
                        cardId.Substring(0, cardId.Length - 2)));
        }

        private static bool IsUtilityText(string cardId, string cardName)
        {
            string text = (cardId ?? "") + " " + (cardName ?? "");
            return text.Contains("铸币")
                || text.Contains("金币")
                || text.Contains("刷新")
                || text.Contains("发现")
                || text.Contains("塑造法术");
        }
    }
}
```

- [x] **Step 4: Integrate with DecisionEngine**

Modify field declarations:

```csharp
private readonly StrategicMemory _strategyMemory = new StrategicMemory();
private readonly StrategySequencePlanner _strategySequencePlanner =
    new StrategySequencePlanner();
private StrategyPlan _lastStrategyPlan;
```

After `_lastMainlinePlan = mainlinePlan;`:

```csharp
var memoryBeforePlan = _strategyMemory.Snapshot;
_lastStrategyPlan = _strategySequencePlanner.Build(
    state, memoryBeforePlan, mainlinePlan);
_strategyMemory.Observe(state, mainlinePlan, _lastStrategyPlan);
```

Add public properties:

```csharp
public StrategyPlan LastStrategyPlan
{
    get { return _lastStrategyPlan; }
    private set { _lastStrategyPlan = value; }
}

public StrategicMemorySnapshot StrategicMemorySnapshot
{
    get { return _strategyMemory.Snapshot; }
}
```

In `GetRankedCandidates`, after the candidate scoring loop and before sorting, add:

```csharp
for (int index = 0; index < scored.Count; index++)
{
    var candidate = scored[index];
    if (candidate == null || _lastStrategyPlan == null
        || _lastStrategyPlan.Steps == null
        || _lastStrategyPlan.Steps.Count == 0)
        continue;
    var first = _lastStrategyPlan.Steps[0].Action;
    if (first != null
        && first.Type == candidate.Action.Type
        && first.TargetIndex == candidate.Action.TargetIndex
        && string.Equals(first.CardId ?? "", candidate.Action.CardId ?? "",
            StringComparison.Ordinal))
        scored[index].Score += 0.15;
}
```

This only affects Shadow `GetRankedCandidates`, not visible legacy `GetBestAction`.

- [x] **Step 5: Run UI lifecycle test to verify source contract**

Run:

```powershell
node tests\test_ui_lifecycle.js
```

Expected: new strategic tests pass.

- [x] **Step 6: Run full behavior suite**

Run:

```powershell
$env:BOBCOACH_HDT_DIR='D:\software\game\HDT-V2.7.0\HDT'
npm run test:behavior
```

Expected: exit 0.

---

### Task 4: Replay Serialization and Analysis

**Files:**
- Modify: `src/BobCoach/BobCoachPlugin.cs`
- Modify: `tools/evaluate/global_replay_analyzer.js`
- Modify: `tests/test_global_replay_analyzer.js`

**Interfaces:**
- Consumes: `DecisionEngine.LastStrategyPlan`, `StrategicMemorySnapshot`.
- Produces replay fields: `strategyObjective`, `strategyPlanHash`, `strategyPlanSteps`, `memoryObservedCoreCardIds`, `memoryObservedPairCardIds`.

- [x] **Step 1: Add failing analyzer contract**

Update `tests/test_global_replay_analyzer.js` dynamic fixture mainline plans to include:

```javascript
strategyObjective: "SearchCore",
strategyPlanHash: "abc",
strategyPlanSteps: ["BuyMinion:0:busker"],
memoryObservedCoreCardIds: ["core-a"],
memoryObservedPairCardIds: [],
```

Assert:

```javascript
assert.strictEqual(firstPlan.strategyObjective, "SearchCore", "strategy objective");
assert.deepStrictEqual(firstPlan.strategyPlanSteps, ["BuyMinion:0:busker"], "strategy steps");
```

Run and verify it fails.

- [x] **Step 2: Serialize plan in BobCoachPlugin**

In `SerializeMainlinePlan`, after existing fields:

```csharp
var strategyPlan = _engine != null
    ? _engine.LastStrategyPlan : null;
var strategyMemory = _engine != null
    ? _engine.StrategicMemorySnapshot : null;
```

Add to anonymous result:

```csharp
strategyObjective = strategyPlan != null
    ? strategyPlan.Objective.ToString() : "",
strategyPlanHash = strategyPlan != null
    ? strategyPlan.PlanHash ?? "" : "",
strategyPlanSteps = strategyPlan != null
    ? strategyPlan.Steps
        .Select(step => step.Action.Type + ":"
            + step.Action.TargetIndex + ":"
            + (step.Action.CardId ?? ""))
        .ToList()
    : new List<string>(),
memoryObservedCoreCardIds = strategyMemory != null
    ? strategyMemory.ObservedCoreCardIds.OrderBy(x => x).ToList()
    : new List<string>(),
memoryObservedPairCardIds = strategyMemory != null
    ? strategyMemory.ObservedPairCardIds.OrderBy(x => x).ToList()
    : new List<string>(),
```

- [x] **Step 3: Expose fields in analyzer**

Modify `analyzeDynamicPlans` record:

```javascript
strategyObjective: typeof plan.strategyObjective === "string"
    ? plan.strategyObjective : "",
strategyPlanHash: typeof plan.strategyPlanHash === "string"
    ? plan.strategyPlanHash : "",
strategyPlanSteps: Array.isArray(plan.strategyPlanSteps)
    ? plan.strategyPlanSteps : [],
memoryObservedCoreCardIds: Array.isArray(plan.memoryObservedCoreCardIds)
    ? plan.memoryObservedCoreCardIds : [],
memoryObservedPairCardIds: Array.isArray(plan.memoryObservedPairCardIds)
    ? plan.memoryObservedPairCardIds : [],
```

- [x] **Step 4: Run replay analyzer contract**

Run:

```powershell
node tests\test_global_replay_analyzer.js
```

Expected: PASS.

---

### Task 5: Adversarial Regression Scenarios

**Files:**
- Modify: `tests/test_ui_lifecycle.js`
- Modify: `tests/test_strategic_plan_contract.ps1`
- Modify: `tests/StrategicPlanContractBehavior.cs`

**Goal:** Ensure the planner remains deterministic, bounded, Shadow-only, and cannot produce invalid action chains.

- [x] Add tests:

```csharp
SearchIsBoundedAndDeterministic();
SearchHandlesNoExpansionWithoutInfiniteLoop();
```

Test repeated execution returns the same sequence, and empty expansion returns one-root sequence.

- [x] Add source checks:

```javascript
test("[StrategicPlan][Shadow] planner must not mutate visible legacy decision", () => {
    const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
    const visibleReturn = engineSrc.indexOf("return bestAction;");
    const rankedMethod = engineSrc.indexOf("public List<BobCoach.Engine.Decision.RankedAction> GetRankedCandidates");
    if (visibleReturn < 0 || rankedMethod < 0 || visibleReturn < rankedMethod)
        return "sequence planner must remain isolated to Shadow ranking";
});
```

- [x] Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\test_strategic_plan_contract.ps1
node tests\test_ui_lifecycle.js
```

Expected: PASS.

---

### Task 6: Documentation, Full Validation, and Adversarial Review

- [x] Update `docs/maintainer/USER_FEEDBACK_ISSUES.md` with `ALG-025` for cross-turn planning infrastructure and `TEST-001` tracking status.
- [x] Update `docs/maintainer/2026-08-12-algorithm-roi-roadmap.md` with Shadow sequence planner phase.
- [x] Run full gates:

```powershell
$env:BOBCOACH_HDT_DIR='D:\software\game\HDT-V2.7.0\HDT'
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

- [x] Run new analyzer on the three 2026-08-13 replays and verify output now includes strategy fields.
- [x] Perform adversarial review against:
  - Shadow isolation: visible policy unchanged.
  - Determinism: repeated search output identical.
  - Bound: max depth 3, beam width 8.
  - Privacy: memory is in-process only.
  - Offline: no external runtime.
  - Data boundary: no player action labels.
- [x] Record any Critical/Important findings in `docs/maintainer/USER_FEEDBACK_ISSUES.md`.
- [x] Update `t_session_state.md` and create the next formal handover document.

## Implementation Amendments From Adversarial Review

- `MainlinePlan` remains internal, so `StrategySequencePlanner.Build` and `StrategicMemory.Observe` are internal rather than public. The replay-facing snapshot remains public.
- `SequenceSearchPlanner` gained a sequence-context overload. The game adapter expands each branch from the accumulated simulated state instead of restarting from the original root.
- Search rejects roots and expansion steps with null actions and normalizes NaN and infinities before ranking.
- Strategic memory is capped at 64 observed core IDs and 32 observed pair IDs, then reset on new-game initialization.
- `DecisionEngine` isolates sequence planning and memory failures from `GetBestAction`.
- Replay serialization filters null plan steps and actions instead of letting one malformed entry turn the whole plan into `"null"`.
- `tests/run_behavior_tests.ps1` now includes the strategic plan contract.

## Adversarial Review Checklist

- [x] Visible legacy `GetBestAction` output is unchanged for fixture states.
- [x] Shadow ranked candidates may receive a bounded sequence bonus, but visible policy cannot.
- [x] A turn with no valid action returns an empty deterministic plan.
- [x] A full board cannot create invalid play steps.
- [x] Zero-gold states cannot create paid steps.
- [x] Replanning occurs on every decision state without accumulating stale hand/board signatures.
- [x] Observed core/pair memory is defensive-copied when serialized.
- [x] Replay output remains bounded; it does not store raw logs or personal data.
- [x] No new deployment or effect-acceptance claim can be made from synthetic tests alone.

## Completion Definition

The implementation is complete when:

- `StrategicMemory`, `StrategyPlan`, `SequenceSearchPlanner`, and `StrategySequencePlanner` are implemented.
- `DecisionEngine` keeps all visible output legacy-only and exposes Shadow plan/memory.
- Global replay contains deterministic strategy-plan fields.
- New contract tests and full repository gates pass.
- Adversarial review is recorded and unresolved Critical/Important issues are documented.
- No win-rate, placement, or strategy-quality improvement is claimed.

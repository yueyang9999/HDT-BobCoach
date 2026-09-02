# Dynamic Strategy Planner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把当前“单动作规则推荐”升级为“动态路径选择”，使算法能按当局种族、当前主线、转型机会、对手压力给出更符合真实战棋节奏的指导。

**Architecture:** 在现有 `MainlineStrategyPlanner` 内部增加动态规划字段和逻辑，不新建大而全的系统。该模块输出 `Stage / Mode / PathScore / Pivot / CounterPlan`，并继续通过现有 `Adjust` 影响动作评分，避免 UI 大改。

**Tech Stack:** C# 7.3、.NET Framework 4.7.2、Node.js 行为测试、PowerShell 测试脚本。

## Global Constraints

- 不新增 UI 文本，不改变 `CompGuidance` 的玩家可见字段。
- 不新增全局依赖，不引入 MCTS 每帧调用。
- 保持 `MainlineStrategyPlanner` 的公开方法签名不变：`Analyze`、`Adjust`、`ShouldSkipUpgrade`。
- 回放中 `mainlinePlan` 必须包含新的动态字段，便于离线验收。
- 所有修改通过 `npm run test:behavior`、`npm run test:contracts`、`npm run test:package` 验证。

---

### Task 1: 扩展 MainlinePlan 动态字段

**Files:**
- Modify: `src/BobCoach/Core/MainlineStrategyPlanner.cs`
- Test: `tests/test_ui_lifecycle.js`

**Interfaces:**
- Produces:
  - `MainlinePlan.Stage: string`
  - `MainlinePlan.Mode: string`
  - `MainlinePlan.CurrentPathScore: double`
  - `MainlinePlan.BestAlternativeScore: double`
  - `MainlinePlan.PivotScore: double`
  - `MainlinePlan.ShouldConsiderPivot: bool`
  - `MainlinePlan.ShouldHedge: bool`
  - `MainlinePlan.PivotObservationCount: int`
  - `MainlinePlan.AlternativeCoreCardIds: HashSet<string>`
  - `MainlinePlan.CounterPlan: string`
  - `MainlinePlan.NeedTradeUp: bool`

- [ ] **Step 1: 在 MainlinePlan 中新增字段**

```csharp
internal string Stage = "";
internal string Mode = "";
internal double CurrentPathScore;
internal double BestAlternativeScore;
internal double PivotScore;
internal bool ShouldConsiderPivot;
internal bool ShouldHedge;
internal int PivotObservationCount;
internal HashSet<string> AlternativeCoreCardIds = new HashSet<string>(StringComparer.Ordinal);
internal string CounterPlan = "";
internal bool NeedTradeUp;
```

- [ ] **Step 2: 在 test_ui_lifecycle.js 增加源级契约**

验证 `MainlineStrategyPlanner.cs` 包含以上字段。

---

### Task 2: 动态路径评分与转型记忆

**Files:**
- Modify: `src/BobCoach/Core/MainlineStrategyPlanner.cs`

**Interfaces:**
- Produces:
  - `private void ComputeStage(GameState state, MainlinePlan plan)`
  - `private void ComputePathScoresAndPivot(GameState state, CompGuideCatalog catalog, CompLockResult compLock, MainlinePlan plan)`
  - `private void ObservePivotCandidates(GameState state, CompGuideCatalog catalog, MainlinePlan plan)`
  - `private double GetGuideScore(GameState state, CompGuideCatalog catalog, CompGuide guide, MainlinePlan plan)`
  - `private bool IsGuideAvailable(CompGuide guide, HashSet<string> availableTribes)`

- [ ] **Step 1: 新增转型观察内存**

```csharp
private sealed class PivotObservation
{
    internal string GuideId;
    internal int Turn;
    internal bool Triple;
}

private readonly List<PivotObservation> _pivotObservations = new List<PivotObservation>();
private int _lastObservationTurn = -1;
```

- [ ] **Step 2: 在 Analyze 中加入动态计算**

在 `Analyze` 的 `ScanShop` 之后调用：

```csharp
ComputeStage(state, plan);
ComputePathScoresAndPivot(state, catalog, compLock, plan);
ObservePivotCandidates(state, catalog, plan);
DecideDynamicMode(state, plan);
BuildCounterPlan(state, plan);
```

- [ ] **Step 3: 实现阶段与路径评分**

`ComputeStage` 按回合和酒馆等级输出 `Early / Mid / Late`。

`GetGuideScore` 使用：

```text
metaTierScore * metaWeight
+ ownedCoreCount * coreWeight
+ shopCoreCount * shopWeight
+ tribeCommitment * tribeWeight
+ tripleBonus
```

权重随阶段变化，早期更看重环境强度和酒馆机会，后期更看重当前投入和对手压力。

- [ ] **Step 4: 实现转型阈值**

```text
Hedge: 最佳替代路径分数超过当前路径 0.15，或最近 2 回合见过替代核心。
Pivot: 最佳替代路径分数超过当前路径 0.40，且替代路径有核心/三连，当前主线缺核心或未硬锁。
```

- [ ] **Step 5: 实现转型记忆**

`ObservePivotCandidates` 把替代流派的商店核心和三连写入 `_pivotObservations`，按 `state.Turn < _lastObservationTurn` 清空，避免跨局污染。

---

### Task 3: 动态模式与反制计划

**Files:**
- Modify: `src/BobCoach/Core/MainlineStrategyPlanner.cs`

**Interfaces:**
- Produces:
  - `private void DecideDynamicMode(GameState state, MainlinePlan plan)`
  - `private void BuildCounterPlan(GameState state, MainlinePlan plan)`
  - `private static double EstimateBoardPower(IEnumerable<MinionData> board)`

- [ ] **Step 1: 实现模式判定**

```text
Pivot: ShouldConsiderPivot 且血量/战力允许
Hedge: ShouldHedge
Commit: ShouldBuyCoreFirst
Economy: ShouldHoldForNextTurn
Survival: 血量 <= 12 或战力比 < 0.65
TradeUp: Late 且战力落后且对手大身材/频率威胁高
```

- [ ] **Step 2: 实现对手反制**

使用 `OpponentTechAdvisor.AnalyzeOpponentBoard`，输出：

```text
BigThreatCount >= 2 -> CounterPlan = "big-stats"
WideFrequencyCount >= 3 -> CounterPlan = "frequency"
```

在 Late 且 `boardPower / opponentPower < 0.8` 时设置 `NeedTradeUp = true`。

---

### Task 4: 动态调整动作评分

**Files:**
- Modify: `src/BobCoach/Core/MainlineStrategyPlanner.cs`

- [ ] **Step 1: 扩展 AdjustBuyMinion**

```text
替代核心且 Mode=Hedge/Pivot -> value += 0.25
NeedTradeUp 且商店卡有 Venomous/Poisonous/DivineShield/Reborn -> value += 0.15
```

- [ ] **Step 2: 扩展 AdjustUpgrade**

```text
Mode=Survival/Economy/Pivot -> value *= 0.75
```

- [ ] **Step 3: 扩展 AdjustRefresh**

```text
Mode=Pivot/Hedge 且缺核心 -> value *= 1.15
```

- [ ] **Step 4: 扩展 AdjustFreeze**

```text
替代核心在商店但当前金币不足 -> value *= 1.20
```

---

### Task 5: 回放序列化动态计划

**Files:**
- Modify: `src/BobCoach/BobCoachPlugin.cs`

- [ ] **Step 1: 在 SerializeMainlinePlan 增加动态字段**

新增 `stage / mode / currentPathScore / bestAlternativeScore / pivotScore / shouldConsiderPivot / shouldHedge / pivotObservationCount / counterPlan / needTradeUp / alternativeCoreCardIds`。

- [ ] **Step 2: 更新 test_global_replay_recorder.js 或 test_ui_lifecycle.js**

验证 `SerializeMainlinePlan` 包含动态字段，回放可审计。

---

### Task 6: 测试、构建、部署

**Files:**
- Modify: `tests/test_ui_lifecycle.js`

- [ ] **Step 1: 增加动态规划源级测试**

验证：

```text
ComputePathScoresAndPivot
DecideDynamicMode
BuildCounterPlan
Stage
Mode
AlternativeCoreCardIds
```

- [ ] **Step 2: 运行验证**

```powershell
npm run test:contracts
npm run test:behavior
npm run test:package
```

- [ ] **Step 3: 构建并部署**

```powershell
.\tools\build\build_release.ps1 -HdtDirectory "D:\software\game\HDT-V2.7.0\HDT" -OutputDirectory "E:\HDT-BobCoach-evidence\season14\generated\release-dynamic-planner-20260812" -Force
```

部署到五个插件目录并校验 SHA256。

---

## Self-Review

- 覆盖了“动态路径评分、转型判断、资源分配、对手反制、回放可审计”五项目标。
- 没有引入每帧 MCTS。
- 没有改变玩家可见 UI。
- 所有新字段都能通过 `mainlinePlan` 回放验证。

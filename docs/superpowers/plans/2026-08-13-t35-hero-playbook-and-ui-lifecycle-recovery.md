# T35 Hero Playbook and UI Lifecycle Recovery Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Date: 2026-08-13

Branch: `codex/season14-algorithm-dark-gift-capture`

## Goal

修复塞纳留斯技能不进入可见推荐、错误出售建议、神秘魔方生命周期错误和零金币旧刷新光晕残留，并建立版本化的单人英雄玩法目录，使英雄技能决策由当前牌面状态和英雄资料共同评估，而不是继续散落硬编码。

## Architecture

本轮分成两个边界清楚的阶段：

1. 第一阶段是可见行为回归修复：先让塞纳留斯这类无目标技能进入统一动作枚举和模拟，再把出售、刷新、饰品生命周期做成单一门控。修复后不改变旧策略层的数据接受边界。
2. 第二阶段新增只读 `HeroPlaybookRegistry`，从本地 HearthDb 卡牌数据、官方 36.2 补丁说明和高可信社区攻略生成版本化 profile。运行时先验证赛季、构建版本和卡池身份；缺失或版本不匹配时 fail-closed，继续使用现有 `HeroPowerEngine`，不访问网络。

英雄玩法目录不把“技能是否能用”和“是否值得现在用”混在一起。前者仍由 `ActionEnumerator`/`HeroPowerRuleCatalog` 处理；后者由 profile 中的阶段条件、金币、血量、酒馆等级、板面、商店和当前动作机会成本处理。

## Reproduced Inputs

- Replay:
  `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260813_154954_BG32_HERO_001_d3d78735.jsonl`
- Hearthstone log directory:
  `D:\software\game\Hearthstone\Logs\Hearthstone_2026_08_13_15_48_37`
- Plugin log:
  `E:\claude_project_hdt\local-data\bob-coach\user-data\bob_coach.log`
- HearthDb current facts:
  `BG32_HERO_001` / `BG32_HERO_001p`
  - Hero: Forest Lord Cenarius
  - Hero Power: Wisdom of Ancients
  - Text: Increase your maximum Gold by 1.
  - Cost: 3
- Local registries:
  `E:\HDT-BobCoach-evidence\season14\generated\registries\hero_registry.json`
  `E:\HDT-BobCoach-evidence\season14\generated\registries\hero_skill_registry.json`
  `E:\HDT-BobCoach-evidence\season14\generated\registries\trinket_registry.json`

## Root Causes

### Cenarius Hero Power

- `HeroStrategyRuleEvaluator.ApplySpecialRules` 已把 `铸币上限` 识别为 `CENARIUS`，并进入 `Economy` 用途。
- `HeroSkillDecisionService.Decide` 在金币足够时返回 `ShouldUse=true`，日志也有 `reason=economy use=True`。
- 但 `ActionEnumerator.Enumerate` 在生成动作前调用 `HasValidHeroPowerTarget`。
- 该方法的无目标白名单没有 `CENARIUS`，未知 special 默认返回 `false`，所以 `UseHeroPower` 从不进入候选列表。
- `Simulator.SimulateHeroPower` 还把塞纳留斯的上限错误封顶为 `11`。该卡牌文本没有 11 上限，当前 HDT replay 也观察到 T1 后 `maxGold=4`、T6 后 `maxGold=14`。模拟层至少应与现有的永久铸币上限效果采用同一有界上限，不能把这个特定英雄压成 11。
- 仅仅补白名单只能让技能候选出现；要让塞纳留斯早期技能真正进入可见推荐，还需要 profile 化的阶段机会成本评估，见 Task 6。

### Wrong Sell Suggestion

- Replay T9 出现 `bestAction=SellMinion target=0`，同一帧候选里 `FreezeShop` 分数更高，说明最终可见动作和评分候选不是同一门控链。
- `DecisionEngine.IsShopMinionBetter` 对 `resourceOrCore` 商店牌使用：
  `shopPower >= soldPower * 0.75 || shop.Tier >= sold.Tier`
  高星低身材资源牌仅凭星级就可以触发出售。`DecisionVisualizer.IsVisualShopTargetBetter` 也有相同分支。
- 应该要求明确的替换目标，并区分“牌面更强”和“只是资源价值更高”。资源/核心牌不能直接替代“有更高星级”这一条件。
- 最终选择层还需要再次验证出售动作；不能只依赖候选生成时的宽松门控。

### Mystery Cube Lifecycle

- `BG30_MagicItem_703` 文本：免费从 2 个新的小型饰品中选择一个以替换本饰品，每回合开始时重复。
- `BG30_MagicItem_703t` 是激活后的选择源实体，当前 `TrinketChoiceBatchLifecycle` 把它混入普通 `AnomalyExtra`。
- T9 日志大量出现 `expected greater batch pending T9, suppress mismatched options`，这是标准大饰品等待逻辑把神秘魔方选择当成普通 T9 大饰品轮次处理。
- 在真正 `BG30_MagicItem_703t` 选择到达前，旧的标准大饰品候选或 placeholder 会持续显示；T10 选择完成后又有残留批次参与 suppression。
- 需要为神秘魔方增加独立事件上下文，并在 `GameStateExtractor`、`BobCoachPlugin` 和面板生命周期中统一跳过 T6/T9 标准轮次过滤。

### Zero Gold Refresh UI

- 最新 replay 显示 `bestAction` 已是 `PlayMinion/UseSpell/FreezeShop`，不是 `Refresh`。
- 日志 `DIAG RefreshBlockedZeroGold` 说明 `showRefresh` 已为 false。
- 根因是 `BobCoachPlugin` 只调用 `ShowRefreshGlow`，没有在 `showRefresh=false` 时清理上一帧的 `refresh_glow`。
- `ShowRefreshGlow` 的原因文字 `TextBlock` 也没有打 `refresh_glow` 标签，所以调用 `ClearTaggedElements("refresh_glow")` 时只移除边框，不移除文字，可能造成旧提示残留。

## Data Source Boundary

### 主事实源

- HearthDb / 本地 `cards.enUS.latest.json` + `cards.zhCN.latest.json`：英雄卡、英雄技能卡、当前文本、费用、护甲、buddy、版本字段。
- 官方 36.2 patch notes:
  `https://hearthstone.blizzard.com/en-us/news/24290432/36-2-patch-notes`
- Season 14 reveal:
  `https://hearthstone.blizzard.com/en-us/news/24290433`
- 塞纳留斯高可信攻略:
  `https://blizzardwatch.com/2025/05/05/forest-lord-cenarius-hearthstone-battlegrounds/`

### 辅助事实源

- 当前卡牌库:
  `https://hsbg.cards/card/forest-lord-cenarius`
- 全英雄页面:
  `https://www.hsbattlegrounds.help/en/tavern-pool/heroes`
- 该辅助页面标注为 Patch 35.2，不能单独作为 36.2 技能版本事实；必须先用 HearthDb 和官方 36.2 patch notes 覆盖后再使用其攻略语义。

### 不使用

- 把过去导入的“流派攻略”直接当作英雄玩法。现有 `14赛季流派攻略信息.txt`、35.2.4 流派攻略和 35.4.2 流派攻略是组合流派资料，不是英雄 profile。
- 不把玩家的主观偏好直接写成正确标签；只用于回归场景和可解释的权重范围。

## Global Constraints

- C# 7.3，.NET Framework 4.7.2，Windows x64。
- 插件运行时不新增网络依赖，所有 profile 数据必须本地读取。
- `BOB_COACH_DATA_ROOT` 缺失、版本不匹配、卡池身份不匹配、JSON 校验失败时 fail-closed。
- 生产库不新增散落的 `if (heroId == "...")`；英雄规则进入版本化 profile 或现有 `HeroPowerRuleCatalog`。
- 不 stage、commit、push、rebase、reset；不修改 `.env`、密钥、CI/CD；不安装全局依赖。
- 不声称胜率、名次、玩家策略质量提升。
- 发布前必须按 `docs/maintainer/BUILD.md` 跑 `npm test`、`validate_repository.ps1`、`git diff --check`，并做对抗性审查。

## File Structure

### Create

- `src/BobCoach/Core/HeroPlaybookProfile.cs`
- `src/BobCoach/Core/HeroPlaybookRegistry.cs`
- `src/BobCoach/Core/HeroPlaybookDecisionService.cs`
- `tests/HeroPlaybookRegistryBehavior.cs`
- `tests/T35LiveFeedbackBehavior.cs`
- `tests/test_hero_playbook_registry.ps1`
- `tests/test_t35_live_feedback.ps1`
- `tools/model/generate_hero_playbook.js`

### Modify

- `src/BobCoach/Core/HeroPowerRuleCatalog.cs`
- `src/BobCoach/Core/ActionEnumerator.cs`
- `src/BobCoach/Core/Simulator.cs`
- `src/BobCoach/Core/HeroSkillDecisionService.cs`
- `src/BobCoach/Core/DecisionEngine.cs`
- `src/BobCoach/Core/DecisionVisualizer.cs`
- `src/BobCoach/Core/TrinketChoiceBatchLifecycle.cs`
- `src/BobCoach/Core/TrinketShadowCaptureSession.cs`
- `src/BobCoach/GameStateExtractor.cs`
- `src/BobCoach/BobCoachPlugin.cs`
- `src/BobCoach/OverlayRenderer.cs`
- `src/BobCoach/BobCoach.csproj`
- `tests/test_ui_lifecycle.js`
- `tests/HeroPowerRuleCatalogBehavior.cs`
- `docs/maintainer/USER_FEEDBACK_ISSUES.md`

### External Evidence

Create outside Git under:

- `E:\HDT-BobCoach-evidence\season14\generated\registries\hero_playbook_registry.json`
- `E:\HDT-BobCoach-evidence\season14\generated\registries\hero_playbook_provenance.json`
- `E:\HDT-BobCoach-evidence\season14\guides\heroes\`

## Task Boundaries

Tasks 1-5 are the visible regression line and can be implemented and verified without the full hero playbook catalog.

Task 6-9 build the versioned catalog and integrate it into the existing visible decision path.

Task 10 is the adversarial review and deployment boundary.

---

### Task 1: Make Cenarius a Non-Targeted Hero Power

**Files:**

- Modify: `src/BobCoach/Core/HeroPowerRuleCatalog.cs`
- Modify: `src/BobCoach/Core/ActionEnumerator.cs:352-479`
- Modify: `tests/HeroPowerRuleCatalogBehavior.cs`

**Interfaces:**

- Consumes: `HeroPowerState`, `GameState`, `HeroPowerRuleCatalog.RequiresTarget`
- Produces: `HeroPowerRuleCatalog.IsNoTargetSpecialRule(string specialRule, string heroCardId, string powerCardId)` and `HeroPowerRuleCatalog.AdvancePermanentGoldCap(string powerCardId, int currentMaxGold)`

- [ ] **Step 1: Add the failing catalog tests**

In `tests/HeroPowerRuleCatalogBehavior.cs`, add:

```csharp
CenariusPowerNeedsNoBoardTarget();
CenariusPermanentGoldCapAdvances();
UnknownPowerDoesNotAdvanceGoldCap();
```

Implementation:

```csharp
private static void CenariusPowerNeedsNoBoardTarget()
{
    AssertFalse(HeroPowerRuleCatalog.RequiresTarget(
        "BG32_HERO_001", "CENARIUS", false),
        "Cenarius max-gold power has no board target");
    AssertTrue(HeroPowerRuleCatalog.IsNoTargetSpecialRule(
        "CENARIUS", "BG32_HERO_001", "BG32_HERO_001p"),
        "Cenarius special rule is in the no-target catalog");
}

private static void CenariusPermanentGoldCapAdvances()
{
    AssertEqual(11, HeroPowerRuleCatalog.AdvancePermanentGoldCap(
        "BG32_HERO_001p", 10),
        "Cenarius advances the permanent gold cap");
    AssertEqual(20, HeroPowerRuleCatalog.AdvancePermanentGoldCap(
        "BG32_HERO_001p", 20),
        "Cenarius gold cap remains bounded at 20");
}

private static void UnknownPowerDoesNotAdvanceGoldCap()
{
    AssertEqual(10, HeroPowerRuleCatalog.AdvancePermanentGoldCap(
        "UNKNOWN_POWER", 10),
        "unknown powers do not change the gold cap");
}
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_hero_power_rule_catalog.ps1
```

Expected: FAIL because `IsNoTargetSpecialRule` and `AdvancePermanentGoldCap` are missing.

- [ ] **Step 3: Add the minimal catalog helpers**

In `HeroPowerRuleCatalog.cs`, add:

```csharp
public static bool IsNoTargetSpecialRule(
    string specialRule,
    string heroCardId,
    string powerCardId)
{
    if (string.Equals(powerCardId, "BG32_HERO_001p",
        StringComparison.Ordinal))
        return true;
    if (string.IsNullOrEmpty(specialRule)) return false;
    switch (specialRule)
    {
        case "secret":
        case "bet_on_winner":
        case "choose_element":
        case "dig_golden":
        case "steal_first_kill":
        case "steal_opponent_board":
        case "refresh_to_spells":
        case "blood_gem":
        case "custom_undead":
        case "spell_synergy":
        case "discover_buddy":
        case "magnetic":
        case "armor_based_budget":
        case "free_swap_per_turn":
        case "replace_higher_tier":
        case "PUTRICIDE":
        case "ALEXSTRASZA":
        case "SNAKE_EYES":
        case "FINLEY":
        case "GENN":
        case "SYLVANAS":
        case "DENATHRIUS":
        case "LIFTOFF":
        case "TOCKI":
        case "CHROMIE":
        case "ELISE":
        case "LAKANISHU":
        case "CENARIUS":
            return true;
        default:
            return false;
    }
}

public static int AdvancePermanentGoldCap(
    string powerCardId,
    int currentMaxGold)
{
    if (!string.Equals(powerCardId, "BG32_HERO_001p",
        StringComparison.Ordinal))
        return currentMaxGold;
    return Math.Min(20, currentMaxGold + 1);
}
```

- [ ] **Step 4: Update the enumerator whitelist**

In `ActionEnumerator.HasValidHeroPowerTarget`, replace the existing no-target special block with:

```csharp
if (HeroPowerRuleCatalog.IsNoTargetSpecialRule(
        sp, hid, power != null ? power.CardId : state.HeroPowerCardId))
    return true;
```

Keep the existing hero-specific checks above it so targeted powers still use their exact target rules.

- [ ] **Step 5: Run the catalog test**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_hero_power_rule_catalog.ps1
```

Expected: PASS with the new Cenarius assertions.

---

### Task 2: Bound and Preserve Cenarius Gold Cap in Simulation

**Files:**

- Modify: `src/BobCoach/Core/Simulator.cs:383-385`
- Modify: `tests/T35LiveFeedbackBehavior.cs`

**Interfaces:**

- Consumes: `HeroPowerRuleCatalog.AdvancePermanentGoldCap`
- Produces: no new public API; changes simulated `MaxGold` for `BG32_HERO_001p`

- [ ] **Step 1: Add a failing simulation regression test**

In `tests/T35LiveFeedbackBehavior.cs`, compile a minimal copy of the simulated state and action and assert:

```csharp
state.MaxGold = 12;
state.Gold = 3;
state.HeroPowerCost = 3;
state.HeroPowerCardId = "BG32_HERO_001p";
state.HeroPowerExhausted = false;
var action = new GameAction
{
    Type = ActionType.UseHeroPower,
    CardId = "BG32_HERO_001p",
    Cost = 3,
};
simulator.Simulate(state, action);
AssertEqual(13, state.MaxGold, "Cenarius simulation advances max gold");
AssertEqual(0, state.Gold, "Cenarius simulation consumes the live cost");
```

Because `Simulator` is coupled to production types, the test harness should compile the same production files used by existing behavior tests rather than duplicating the simulation.

- [ ] **Step 2: Run the failing test**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_t35_live_feedback.ps1
```

Expected: FAIL because current code clamps `MaxGold=12` to 11 instead of advancing it to 13.

- [ ] **Step 3: Replace the hard-coded cap**

Replace:

```csharp
if (powerCardId == "BG32_HERO_001p")
    state.MaxGold = Math.Min(11, state.MaxGold + 1);
```

with:

```csharp
state.MaxGold = HeroPowerRuleCatalog.AdvancePermanentGoldCap(
    powerCardId, state.MaxGold);
```

- [ ] **Step 4: Run the regression test**

Expected: PASS. Then run:

```powershell
npm test
```

---

### Task 3: Tighten Sell Qualification and Revalidate Final Sell

**Files:**

- Modify: `src/BobCoach/Core/DecisionEngine.cs:1290-1301`
- Modify: `src/BobCoach/Core/DecisionEngine.cs:2026-2050`
- Modify: `src/BobCoach/Core/DecisionVisualizer.cs:278-337`
- Modify: `src/BobCoach/BobCoachPlugin.cs:1989-2047`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**

- Produces: `DecisionEngine.IsSellActionValid(GameState state, GameAction action)`
- Consumes: `IsSellWorthwhile`, `IsShopMinionBetter`, `IsVisualShopTargetBetter`

- [ ] **Step 1: Add the failing static contract**

In `tests/test_ui_lifecycle.js`, add:

```js
test("[T35][Sell] final sell must be revalidated after ranked selection", () => {
    const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
    if (!plugin.includes("IsSellActionValid")) {
        return "final ranked sell action is not revalidated";
    }
});

test("[T35][Sell] resource/core tier alone cannot justify selling a board minion", () => {
    const engine = readFile("src/BobCoach/Core/DecisionEngine.cs");
    const block = engine.slice(
        engine.indexOf("private bool IsShopMinionBetter"),
        engine.indexOf("private int MaxShopTier")
    );
    if (block.includes("|| shop.Tier >= sold.Tier")) {
        return "resource/core branch still allows tier-only replacement";
    }
});
```

Run:

```powershell
npm run test:behavior
```

Expected: FAIL for both missing T35 assertions.

- [ ] **Step 2: Change the resource/core replacement rule**

In `DecisionEngine.IsShopMinionBetter`, replace:

```csharp
if (_classifier != null
    && (IsResourceCard(shop.CardId)
        || IsCoreComboCard(shop.CardId)))
    return shopPower >= soldPower * 0.75 || shop.Tier >= sold.Tier;
```

with:

```csharp
if (_classifier != null
    && (IsResourceCard(shop.CardId)
        || IsCoreComboCard(shop.CardId)))
{
    if (shopPower >= soldPower * 1.05)
        return true;
    double boardAvg = _fe.ComputeBoardPower(state.BoardMinions)
        / Math.Max(1, state.BoardMinions.Count);
    return soldPower <= boardAvg * 0.65;
}
```

The resource/core branch now requires either a clear board-power improvement, or the sold minion is dead weight relative to the board. Tier alone is no longer enough.

- [ ] **Step 3: Mirror the visual rule**

In `DecisionVisualizer.IsVisualShopTargetBetter`, change the `resourceOrCore` branch to:

```csharp
if (resourceOrCore)
{
    if (shopPower >= soldPower * 1.05)
        return true;
    double boardAvg = state.BoardMinions.Count > 0
        ? state.BoardMinions.Sum(m => m == null ? 0 : VisualPower(m))
            / state.BoardMinions.Count
        : 0;
    return soldPower <= boardAvg * 0.65;
}
```

- [ ] **Step 4: Expose final sell validation**

In `DecisionEngine`, add:

```csharp
public bool IsSellActionValid(GameState state, GameAction action)
{
    if (state == null || action == null
        || action.Type != ActionType.SellMinion)
        return false;
    return IsSellWorthwhile(state, action.TargetIndex);
}
```

At the end of `GetBestAction`, replace the final sell gate with this public method so all callers share one definition.

- [ ] **Step 5: Revalidate before render**

In `BobCoachPlugin.EvaluateAndRender`, after:

```csharp
var rankingDecision = EvaluateRankingDecision(state);
var bestAction = rankingDecision.SelectedAction;
```

add:

```csharp
if (bestAction != null
    && bestAction.Type == Engine.ActionType.SellMinion
    && !_engine.IsSellActionValid(state, bestAction))
{
    bestAction = null;
}
```

This is the final visible gate, independent of ranked candidate scoring.

- [ ] **Step 6: Run behavior and contract suites**

```powershell
npm run test:behavior
npm test
```

Expected: T35 sell assertions PASS; existing behavior suites remain green.

---

### Task 4: Give Mystery Cube Its Own Choice Lifecycle

**Files:**

- Modify: `src/BobCoach/Core/TrinketChoiceBatchLifecycle.cs`
- Modify: `src/BobCoach/Core/TrinketShadowCaptureSession.cs`
- Modify: `src/BobCoach/GameStateExtractor.cs:1478-1700`
- Modify: `src/BobCoach/BobCoachPlugin.cs:4070-4230`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**

- Produces: `TrinketChoiceContext.MysteryCubeActivation`
- Consumes: `BG30_MagicItem_703`, `BG30_MagicItem_703t`, `TrinketChoiceBinding`

- [ ] **Step 1: Add the failing static contract**

In `tests/test_ui_lifecycle.js`:

```js
test("[T35][Trinket] mystery cube activation is not treated as scheduled greater trinket", () => {
    const lifecycle = readFile("src/BobCoach/Core/TrinketChoiceBatchLifecycle.cs");
    if (!lifecycle.includes("MysteryCubeActivation")) {
        return "missing MysteryCubeActivation context";
    }
    if (!lifecycle.includes("BG30_MagicItem_703t")) {
        return "mystery cube source is not explicitly recognized";
    }
});

test("[T35][Trinket] mystery cube suppresses scheduled turn filtering", () => {
    const extractor = readFile("src/BobCoach/GameStateExtractor.cs");
    if (!extractor.includes("hasRepeatingReplacementTrinket")
        || !extractor.includes("scheduledTrinketChoiceTurn")
    ) {
        return "mystery cube does not bypass scheduled trinket filtering";
    }
});
```

Run `npm run test:behavior`. Expected: FAIL until the lifecycle changes are present.

- [ ] **Step 2: Add the lifecycle enum and classification**

In `TrinketChoiceContext`, add:

```csharp
MysteryCubeActivation,
```

In `ObserveInternal`, replace the `anomalyExtra` classification with:

```csharp
bool mysteryCube = string.Equals(
    batch.SourceCardId,
    "BG30_MagicItem_703t",
    StringComparison.Ordinal);
...
else if (mysteryCube)
    context = TrinketChoiceContext.MysteryCubeActivation;
```

Set `EligibleForCalibration` only for `ScheduledLesser` and `ScheduledGreater`, leaving Mystery Cube excluded.

In `TryGetForTurn`, add immediately after the null check and before the
choice-id early return:

```csharp
if (binding.Context == TrinketChoiceContext.MysteryCubeActivation
    && turn > binding.ObservedTurn)
{
    Current = null;
    binding = null;
    return false;
}
```

In `TrinketShadowCaptureSession.ToSchemaContext`, map `MysteryCubeActivation` to `"mystery_cube_activation"`.

- [ ] **Step 3: Bypass scheduled T9 filtering while the cube is active**

Add an internal read/write property to `GameStateExtractor`:

```csharp
internal TrinketChoiceContext CurrentTrinketChoiceContext { get; set; }
```

Before extraction, `BobCoachPlugin` sets:

```csharp
_extractor.CurrentTrinketChoiceContext =
    _trinketChoiceLifecycle.Current == null
        ? TrinketChoiceContext.Unknown
        : _trinketChoiceLifecycle.Current.Context;
```

In `GameStateExtractor.ExtractTrinketOffer`, calculate:

```csharp
bool mysteryCubeActivation = _equippedTrinketIds != null
    && _equippedTrinketIds.Contains("BG30_MagicItem_703")
    && CurrentTrinketChoiceContext
        == TrinketChoiceContext.MysteryCubeActivation;
```

Wrap the `if (scheduledTrinketChoiceTurn && result.Count >= 2)` type filtering with:

```csharp
if (scheduledTrinketChoiceTurn && result.Count >= 2
    && !mysteryCubeActivation)
```

Do not inject `__TRINKET_PENDING_GREATER_*` placeholders while
`mysteryCubeActivation` is true. A standard greater-trinket batch from
`BG30_Trinket_2nd` keeps a non-mystery lifecycle context and can still use
the standard T9 filter.

- [ ] **Step 4: Use the authoritative Power.log batch for the panel**

In `BobCoachPlugin`, when the current `TrinketChoiceBinding.Context == MysteryCubeActivation`, require the `PowerLog` candidate source:

```csharp
if (binding != null
    && binding.Context == TrinketChoiceContext.MysteryCubeActivation
    && trinketSource != Engine.UiTargetSource.PowerLog)
{
    trinketSource = Engine.UiTargetSource.None;
}
```

This prevents entity-extracted stale greater-trinket candidates from taking over the panel before `BG30_MagicItem_703t` arrives.

- [ ] **Step 5: Clear the panel on completion and turn change**

After completion, reset the panel state explicitly:

```csharp
_renderer.ClearTrinketHints();
_trinketPanelState = default(Engine.PanelState);
_lastTrinketHideTurn = state.Turn;
```

Do not call `ShowTrinketLoading` for a new turn while `_lastTrinketHideTurn == state.Turn`.

Add a static lifecycle assertion that the completion path clears `trinket_panel` rather than only suppressing `state.TrinketOffer`.

- [ ] **Step 6: Run tests**

```powershell
npm run test:behavior
npm test
```

Expected: T35 mystery-cube assertions PASS.

---

### Task 5: Clear Stale Refresh and Freeze Glow When Gate Becomes False

**Files:**

- Modify: `src/BobCoach/OverlayRenderer.cs:818-925`
- Modify: `src/BobCoach/BobCoachPlugin.cs:3868-3910`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**

- Produces: `OverlayRenderer.ClearRefreshGlow()` and `OverlayRenderer.ClearFreezeGlow()`
- Consumes: `ClearTaggedElements`

- [ ] **Step 1: Add the failing static contract**

In `tests/test_ui_lifecycle.js`:

```js
test("[T35][Refresh] disabled refresh gate clears the previous glow", () => {
    const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
    const block = plugin.slice(
        plugin.indexOf("bool showRefresh ="),
        plugin.indexOf("// 冻结提示")
    );
    if (!block.includes("ClearRefreshGlow")) {
        return "showRefresh=false path does not clear refresh glow";
    }
});

test("[T35][Refresh] refresh reason text shares the refresh tag", () => {
    const overlay = readFile("src/BobCoach/OverlayRenderer.cs");
    const block = overlay.slice(
        overlay.indexOf("public void ShowRefreshGlow"),
        overlay.indexOf("public void ShowFreezeGlow")
    );
    const reasonIndex = block.indexOf("reasonLabel");
    if (reasonIndex < 0) return "reason label missing";
    const reasonBlock = block.slice(reasonIndex, reasonIndex + 700);
    if (!reasonBlock.includes('Tag = "refresh_glow"')) {
        return "refresh reason label is not cleared with the glow";
    }
});
```

Run `npm run test:behavior`. Expected: FAIL.

- [ ] **Step 2: Add renderer clear methods**

In `OverlayRenderer`, add:

```csharp
public void ClearRefreshGlow()
{
    ClearTaggedElements("refresh_glow");
}

public void ClearFreezeGlow()
{
    ClearTaggedElements("freeze_glow");
}
```

Add `Tag = "refresh_glow"` to the refresh reason `TextBlock`, and `Tag = "freeze_glow"` to the freeze reason `TextBlock`.

- [ ] **Step 3: Clear stale glow in the plugin**

In `BobCoachPlugin`, change:

```csharp
if (showRefresh)
{
    ...
}
else if ((needRefresh || badShop)
    && !refreshAffordableByState
    && state.Gold < 1
    && state.FreeRefreshCount == 0)
{
    Log(...);
}
```

to:

```csharp
if (showRefresh)
{
    ...
}
else
{
    _renderer.ClearRefreshGlow();
    if ((needRefresh || badShop)
        && !refreshAffordableByState
        && state.Gold < 1
        && state.FreeRefreshCount == 0)
    {
        Log(...);
    }
}
```

Do the same for `showFreeze`:

```csharp
if (showFreeze)
{
    ...
}
else
{
    _renderer.ClearFreezeGlow();
}
```

The existing `showFreeze` timeout logic remains unchanged when the freeze hint is active.

- [ ] **Step 4: Run behavior and package tests**

```powershell
npm run test:behavior
npm test
```

Expected: T35 refresh assertions PASS; package tests remain green.

---

### Task 6: Generate the Versioned Hero Playbook Registry

**Files:**

- Create: `tools/model/generate_hero_playbook.js`
- Create: `tests/HeroPlaybookRegistryBehavior.cs`
- Create: `tests/test_hero_playbook_registry.ps1`
- Modify: `src/BobCoach/BobCoach.csproj`

**Interfaces:**

- Produces external `hero_playbook_registry.json`
- Produces `HeroPlaybookProfile`, `HeroPlaybookRegistry`, `HeroPlaybookDecisionService`

The generator is outside the production dependency graph and only runs during evidence preparation. It reads the configured season root, not the repository.

- [ ] **Step 1: Define the profile schema**

Use this schema in the generated registry:

```json
{
  "_meta": {
    "schemaVersion": 1,
    "season": "season14",
    "gameBuild": "36.2.248348",
    "cardPoolIdentity": "0bcc979ae21df891",
    "generatedAt": "...",
    "source": "generate_hero_playbook"
  },
  "heroes": [
    {
      "heroCardId": "BG32_HERO_001",
      "heroNameCn": "森林之王塞纳留斯",
      "heroNameEn": "Forest Lord Cenarius",
      "heroPowerCardIds": ["BG32_HERO_001p"],
      "powerType": "Active",
      "powerCost": 3,
      "armor": 16,
      "unlockTurn": 1,
      "unlockTier": 1,
      "usePurpose": "Economy",
      "specialRule": "CENARIUS",
      "requiresTarget": false,
      "strategyCoverage": "curated",
      "phases": [
        {
          "minTurn": 1,
          "maxTurn": 5,
          "priority": "Critical",
          "when": {
            "goldAtLeast": 3,
            "healthAtLeast": 8,
            "targetRequired": false,
            "reason": "early-economy"
          }
        }
      ],
      "sources": [
        "https://hearthstone.blizzard.com/en-us/news/24290432/36-2-patch-notes",
        "https://blizzardwatch.com/2025/05/05/forest-lord-cenarius-hearthstone-battlegrounds/"
      ]
    }
  ]
}
```

The schema must reject `BGDUO_` card IDs and any `heroCardId` whose power mapping does not exist in `hero_skill_registry.json`.

- [ ] **Step 2: Generate the full 116-hero base catalog**

In `tools/model/generate_hero_playbook.js`:

1. Read `hero_skill_registry.json`.
2. Read HearthDb current card text for each hero power.
3. For each hero, create a generic profile with:
   - hero and power IDs,
   - power type, cost, armor, unlock turn/tier,
   - inferred `usePurpose` from card text,
   - `requiresTarget` from `HeroPowerRuleCatalog`,
   - one generic phase that only recommends active powers when gold, unlock, exhaustion and target checks pass.
4. Add explicit curated overlays for the seven official 36.2 changed heroes:
   - Cariel Roame
   - Enhance-o Mechano
   - Edwin VanCleef
   - Overlord Saurfang
   - Ragnaros
   - Rakanishu
   - Tae'thelan Bloodwatcher
5. Add the Cenarius early-economy profile with the `Critical` phase shown above, while retaining low-health and empty-board exceptions.
6. Write the registry and provenance manifest outside Git.

The generic base profile is not a “300-game learned strategy”; it is a version-matched fact table. Curated strategy overlays must carry source URLs.

- [ ] **Step 2a: Inventory every remaining single-player hero**

Do not stop at the seven changed heroes. Run `agent-reach` searches for the
remaining heroes in batches, using queries that combine:

```text
Hearthstone Battlegrounds <hero name> guide current
HSReplay battlegrounds <hero name> hero power guide
<hero name> Battlegrounds hero power strategy
```

Prefer these high-confidence destinations:

- official Blizzard patch/reveal pages,
- `blizzardwatch.com`,
- `hsreplay.net/battlegrounds/heroes/...`,
- `amalgadon.com/hero/...`,
- the New Hearthstone Wiki.

Write the raw source inventory under:

`E:\HDT-BobCoach-evidence\season14\guides\heroes\`

Do not put downloaded web pages in Git. Each generated profile must list
only source URLs, not the raw pages.

- [ ] **Step 2b: Add curated overlays with explicit coverage status**

For every hero that has a source-backed phase/playstyle claim, add a curated
overlay. For heroes with only fact-level coverage, set:

```json
"strategyCoverage": "facts-only"
```

For source-backed overlays, set:

```json
"strategyCoverage": "curated"
```

The loader accepts either value but must fail when both values are absent or
when the value is unrecognized. This prevents a generic profile from being
reported later as a complete hand-authored guide.

- [ ] **Step 3: Add loader fail-closed tests**

In `tests/HeroPlaybookRegistryBehavior.cs`, assert:

```csharp
RegistryLoadsFromSeason14EvidenceRoot();
CenariusProfileHasEarlyEconomyPhase();
AllHeroRegistryEntriesHaveMatchingPowerFacts();
Official36_2HeroChangesArePresent();
BgDuoProfilesAreRejected();
MissingRegistryFailsClosed();
VersionMismatchFailsClosed();
```

The loader must not fall back to `APPDATA\bob-coach` for missing season-specific profiles; it must return `IsLoaded=false`.

- [ ] **Step 4: Run the registry test**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_hero_playbook_registry.ps1
```

Expected: PASS with all 116 hero mappings and the seven official changes covered.

---

### Task 7: Add the Runtime Hero Playbook Service

**Files:**

- Create: `src/BobCoach/Core/HeroPlaybookProfile.cs`
- Create: `src/BobCoach/Core/HeroPlaybookRegistry.cs`
- Create: `src/BobCoach/Core/HeroPlaybookDecisionService.cs`
- Modify: `src/BobCoach/BobCoach.csproj`

**Interfaces:**

- Produces: `HeroPlaybookRegistry.TryGet(string heroCardId, out HeroPlaybookProfile)`
- Produces:
  `HeroPlaybookDecisionService.Decide(GameState state, HeroPowerState power, HeroPlaybookProfile profile)`
- Produces:
  `HeroPlaybookDecisionService.Decide(GameState state, GameAction action, HeroPlaybookProfile profile)`
- Consumes: `GameState`, `HeroPowerState`, `HeroSkillDecisionService`

- [ ] **Step 1: Add `HeroPlaybookProfile`**

Fields:

```csharp
public sealed class HeroPlaybookProfile
{
    public string HeroCardId = "";
    public List<string> HeroPowerCardIds = new List<string>();
    public string PowerType = "unknown";
    public int PowerCost;
    public int UnlockTurn = 1;
    public int UnlockTier = 1;
    public string UsePurpose = "Generic";
    public string SpecialRule = "";
    public bool RequiresTarget;
    public string StrategyCoverage = "facts-only";
    public List<HeroPlaybookPhase> Phases = new List<HeroPlaybookPhase>();
    public List<string> Sources = new List<string>();
}

public sealed class HeroPlaybookPhase
{
    public int MinTurn;
    public int MaxTurn = 99;
    public string Priority = "Normal";
    public int GoldAtLeast;
    public int HealthAtLeast;
    public bool TargetRequired;
    public string Reason = "";
}
```

- [ ] **Step 2: Add `HeroPlaybookRegistry`**

The loader follows `SeasonDataSelector` and `Season14RegistryJsonParser`; it verifies:

- `_meta.schemaVersion`
- `_meta.season`
- `_meta.gameBuild`
- `_meta.cardPoolIdentity`
- `heroes.Count == 116`
- each `heroPowerCardIds` entry exists in `hero_skill_registry`
- no `BGDUO_` strings

Store profiles in an ordinal dictionary. Expose `IsLoaded`, `Count`,
`ConfiguredRoot`, `TryGet`, and:

```csharp
public HeroPlaybookProfile GetProfileForPower(
    string heroCardId,
    string heroPowerCardId)
```

`GetProfileForPower` first resolves by `heroPowerCardId`, then by
`heroCardId`; it returns `null` for unknown IDs.

- [ ] **Step 3: Add `HeroPlaybookDecisionService`**

Define a `HeroPlaybookDecision` result:

```csharp
public sealed class HeroPlaybookDecision
{
    public bool ShouldUse;
    public int Cost;
    public double ActionValueBias;
    public string Reason = "";
}
```

Implement:

```csharp
public static HeroPlaybookDecision Decide(
    GameState state,
    HeroPowerState power,
    HeroPlaybookProfile profile)
```

and:

```csharp
public static HeroPlaybookDecision Decide(
    GameState state,
    GameAction action,
    HeroPlaybookProfile profile)
```

The `GameAction` overload resolves the matching `HeroPowerState` by
`action.CardId`, then delegates to the `HeroPowerState` overload.

Evaluation order:

```csharp
1. reject passive/aura/unknown power types;
2. reject exhausted powers;
3. reject insufficient live gold;
4. reject locked turn/tier;
5. reject invalid target requirements;
6. find the highest-priority matching phase;
7. reject phase health/gold gates;
8. compute a bounded `ActionValueBias` from phase priority:
   Critical = 1.20
   High = 0.80
   Normal = 0.35
   Low = 0.00;
9. for `CENARIUS`, add a discounted future-gold term:
   `min(1.20, 0.18 * max(0, 10 - turn))`;
10. return `ShouldUse`, `Reason`, and `ActionValueBias`.
```

This service does not bypass target validation or gold validation. It only replaces the generic `HeroSkillDecisionService` early return for profiles that exist.

- [ ] **Step 4: Wire the service into `DecisionEngine`**

Add a `HeroPlaybookRegistry` field, load it in `LoadResources`, and leave it
`null` when loading fails. In the main action loop, after the existing
`UseHeroPower` power bias, add:

```csharp
var playbookProfile = _heroPlaybook?.GetProfileForPower(
    state.HeroCardId, action.CardId);
if (playbookProfile != null)
{
    var playbook = HeroPlaybookDecisionService.Decide(
        state, action, playbookProfile);
    if (playbook.ShouldUse)
        value += playbook.ActionValueBias;
    else
        value *= 0.0;
}
```

The existing `HeroSkillDecisionService` remains the fallback when the playbook registry is not loaded.

---

### Task 8: Verify All Single-Player Hero Mappings

**Files:**

- Modify: `tests/HeroPlaybookRegistryBehavior.cs`
- Modify: `tools/model/generate_hero_playbook.js`
- Modify: `docs/maintainer/USER_FEEDBACK_ISSUES.md`

- [ ] **Step 1: Compare HearthDb and hero skill registry**

Assert for every `heroCardId`:

```text
heroCardId exists
powerCardId exists
powerCardId is HERO_POWER type
name/cost/text match current HearthDb
no stale pre-36.2 text is used for the seven changed heroes
```

Add a generator failure if any mapping is missing.

- [ ] **Step 2: Record the seven changed heroes**

Store old/new text and source URL in `hero_playbook_provenance.json`.

- [ ] **Step 3: Add a complete coverage assertion**

```csharp
AssertEqual(116, registry.Count,
    "single-player hero playbook covers all registry heroes");
AssertEqual(0, missingHeroCardIds.Count,
    "every playbook hero has a hero power fact");
AssertEqual(0, staleChangedHeroTextCount,
    "official 36.2 changed heroes use current text");
```

- [ ] **Step 4: Run full contract and behavior suites**

```powershell
npm test
```

---

### Task 9: Integrate Cenarius Early-Economy Strategy Into Visible Guidance

**Files:**

- Modify: `src/BobCoach/BobCoachPlugin.cs`
- Modify: `src/BobCoach/Core/HeroSkillDecisionService.cs`
- Modify: `tests/T35LiveFeedbackBehavior.cs`

**Interfaces:**

- Consumes: `HeroPlaybookRegistry`, `HeroPlaybookDecisionService`
- Produces: visible `UseHeroPower` candidate and hero glow for Cenarius T1-T5

- [ ] **Step 1: Add a failing live-scenario contract**

Create a synthetic Cenarius state:

```csharp
turn = 1
gold = 3
maxGold = 4
health = 30
tier = 1
board = 0
shop = 3
heroPower = BG32_HERO_001p
heroPowerCost = 3
exhausted = false
```

Assert:

```csharp
var actions = enumerator.Enumerate(state, "BG32_HERO_001");
var heroPower = actions.First(a =>
    a.Type == ActionType.UseHeroPower
    && a.CardId == "BG32_HERO_001p");
AssertEqual(false, heroPower.RequiresTarget,
    "Cenarius power has no target");

var ranked = engine.GetRankedCandidates(state, 8);
AssertEqual("UseHeroPower", ranked[0].Action.Type.ToString(),
    "Cenarius early economy power is the primary action");
```

- [ ] **Step 2: Run the failing test**

Expected: FAIL if `UseHeroPower` is missing or does not rank first.

- [ ] **Step 3: Make the hero glow consume the same playbook verdict**

In `BobCoachPlugin`, replace the current `HeroSkillDecisionService.Decide` call with:

```csharp
HeroPowerState currentPower = state.HeroPowers != null
    ? state.HeroPowers.FirstOrDefault(power =>
        power != null
        && power.CardId == state.HeroPowerCardId)
    : null;
HeroPlaybookProfile profile = null;
bool hasProfile = false;
if (_heroPlaybook != null)
{
    hasProfile = _heroPlaybook.TryGet(
        state.HeroPowerCardId, out profile);
    if (!hasProfile)
    {
        hasProfile = _heroPlaybook.TryGet(
            state.HeroCardId, out profile);
    }
}
var heroSkillDecision = hasProfile && currentPower != null
    ? HeroPlaybookDecisionService.Decide(
        state, currentPower, profile)
    : HeroSkillDecisionService.Decide(...);
```

The glow uses `ShouldUse`, while `ActionEnumerator` and `DecisionEngine` still use the same profile for candidate generation and score.

- [ ] **Step 4: Preserve low-health and target exceptions**

Add assertions:

```csharp
CenariusDoesNotOverrideCriticalHealth();
CenariusDoesNotRequireBoardOrShopTargets();
CenariusDoesNotUseWhenExhausted();
```

This prevents the profile from becoming an unconditional “first five turns always button” rule.

- [ ] **Step 5: Run the full test matrix**

```powershell
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

---

### Task 10: Adversarial Review, Build, Deploy and Archive

**Files:**

- Create: `docs/maintainer/2026-08-13-t35-live-feedback-adversarial-review.md`
- Modify: `docs/maintainer/t_session_state.md`
- Modify: `docs/maintainer/USER_FEEDBACK_ISSUES.md`

## Adversarial Review Matrix

Before deployment, review each changed path with a deliberately hostile scenario:

### Cenarius

- T1 empty board and 3 gold: must show `UseHeroPower`.
- T1 with a forced critical health state: must not override survival.
- T2-T5 with enough gold: profile phase may prioritize economy, but a lethal upgrade or protected core purchase remains the primary visible action.
- T6 and later: no unconditional use; the normal opportunity-cost evaluator is authoritative.
- `maxGold=20`: simulation must not exceed 20 or silently clamp to 11.

### Sell

- Full board, no shop target, no hand replacement: must not show sell.
- Full board with a high-tier low-stat resource shop minion: must not sell a useful board minion just because shop tier is higher.
- Full board with an affordable clear upgrade target: may sell only when the target improves board power or the sold card is dead weight.
- Final ranked candidate says sell but `IsSellActionValid` says false: final visible action must be null or another valid non-sell action.

### Mystery Cube

- Equipped `BG30_MagicItem_703` before `BG30_MagicItem_703t`: no standard T9 greater placeholder.
- `BG30_MagicItem_703t` has exactly two new Lesser Trinket candidates: show those two candidates.
- Completion at T9: panel clears immediately.
- T10 without a new activation: old candidates do not reappear.
- Duplicate `BG30_MagicItem_703` entity and stale standard greater entities coexist: entity path must defer to authoritative Power.log candidates.

### Refresh

- Frame A: affordable refresh shows glow.
- Frame B: live gold changes to 0 and free refresh is 0: glow and reason label disappear in the same frame.
- Freeze hint disappears: freeze glow and reason label disappear.
- Combat starts: `ClearNonPanel` still removes non-panel glow elements.

## Verification Commands

```powershell
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

For the release candidate:

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\software\game\HDT-V2.7.0\HDT'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release\deploy_local_release.ps1
```

Only deploy after the five HDT plugin directories have been identified and their existing DLL snapshots have been archived.

## Acceptance Gates

- Cenarius regression: `UseHeroPower` appears in candidate list and can be ranked as primary in the synthetic T1 state.
- Sell regression: no tier-only resource/core replacement; final sell is revalidated.
- Mystery cube regression: independent lifecycle and no T9/T10 stale panel.
- Zero-gold refresh regression: stale glow is cleared.
- 116-hero mapping coverage: PASS.
- Official 36.2 hero changes: PASS.
- Full `npm test`: PASS.
- Release build: PASS.
- Repository validation: PASS.
- `git diff --check`: PASS.
- Adversarial review: PASS with no open unresolved blockers.
- No win-rate, placement, or strategy-quality claim is made.

## Open Assumptions

- The bounded simulation cap for permanent max-gold effects remains 20. This is a simulator safety bound consistent with the existing `BG28_805` handling; it is not a claimed game-rule limit unless HearthDb text explicitly states one.
- Generic hero profiles are generated from current HearthDb text. They do not by themselves provide a full hand-authored guide for all 116 heroes; curated overlays are added only where source citations exist.
- `hsbattlegrounds.help` is used only as a secondary source for strategy semantics, not as the version authority for 36.2 hero power text.
- The current external season evidence root is available on this machine; the plugin falls back to the existing `HeroPowerEngine` if the playbook registry is absent or invalid.

# Season14 实时决策优化 Phase 1 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用版本化卡牌对象库、统一铸币快照、Activate/英雄技能最小可执行链路和运维门禁，把当前“规则 early-return + 多层 UI 改写”的链路收敛为可审计、可回放、可季度更新的一阶段基线。

**Architecture:** 先补齐数据事实层 `CardObjectDatabase` 和 `HeroSkillCatalog`，再把 `ActionEnumerator`、`DecisionVisualizer`、`BobCoachPlugin` 的铸币判断统一到 `EconomySnapshot`。最后新增 `ActionType.Activate` 和发布前注册表审计，legacy 保持可见和回退，不接管玩家可见策略。

**Tech Stack:** .NET Framework 4.7.2、C# 7.3、old-style MSBuild、Newtonsoft.Json、Node.js standard library、PowerShell test harness、JSON registry。

## Global Constraints

- 不 push、不 rebase、不 reset --hard、不 force push、不删历史。
- 不改 `.env`/密钥/CI/CD/database schema；不读写 `%APPDATA%\bob-coach\data`；不动 BGDUO。
- 不把原始第三方响应、用户日志、回收数据写入 Git/DLL/ZIP。
- 不编造赛季卡牌、机制、权重、tier 或效果；缺失数据显式 `unknown`，fail closed。
- 不宣称胜率、名次、Top4 或策略质量提升。
- 暗赐真实 UI、`BG36_MidGameEffect_000`、`BG36_HERO_101p`、“配置 RG” 保持 `unknown`。
- 本计划不一次性重构 legacy；每阶段结束必须可独立测试、可回退、可发布。
- 所有新数据工具必须要求显式 `--data-root`，禁止默认读取 `%APPDATA%\bob-coach`。

---

## 文件结构

| 文件 | 责任 |
| --- | --- |
| `tools/build/generate_card_object_catalog.js` | 从 raw cards + 现有 registry 生成 `card_object_catalog.json` |
| `tools/build/generate_hero_skill_registry.js` | 从 hero registry + HearthDb 文本事实生成 `hero_skill_registry.json` |
| `tools/build/audit_card_object_catalog.js` | 校验 `_meta`、CardId 唯一、中英文名、机制、版本字段 |
| `src/BobCoach/Core/CardObjectDatabase.cs` | 运行时只读对象库，所有 CardId 规则查询的公共入口 |
| `src/BobCoach/Core/HeroSkillCatalog.cs` | 主动/被动英雄技能规则库 |
| `src/BobCoach/Core/EconomySnapshot.cs` | 唯一铸币快照，统一 `liveGold`、已用铸币、可支付动作 |
| `src/BobCoach/Core/LiveSceneAnalyzer.cs` | 将 GameState 转为板面、经济、生存、未知覆盖快照 |
| `src/BobCoach/Core/RecommendationPlan.cs` | 后续视觉/文字唯一输出模型；本阶段先定义不接 UI |
| `src/BobCoach/Core/ActionEnumerator.cs` | 加入 `Activate`，并用 `EconomySnapshot` 做费用判断 |
| `src/BobCoach/Core/VisualPlan.cs` | 加入 `ActivateHint`，为发动机制提供稳定视觉契约 |
| `tests/Phase1CardObjectDatabaseBehavior.cs` | 对象库加载、未知 fail closed、中文名、机制 |
| `tests/Phase1EconomySnapshotBehavior.cs` | 0/1/2/3 金币、免费刷新、升本后可支付空间 |
| `tests/Phase1ActivateActionBehavior.cs` | Activate 候选生成、费用、每回合一次 |
| `tests/test_phase1_season_data.ps1` | 发布前注册表覆盖、diff、未知项审计 |

## Task 1: 卡牌对象库数据契约和生成器

**Files:**
- Create: `tools/build/generate_card_object_catalog.js`
- Create: `tools/build/generate_hero_skill_registry.js`
- Create: `tools/build/audit_card_object_catalog.js`
- Test: `tests/test_phase1_season_data.ps1`

**Interfaces:**
- Consumes: `--data-root <evidence-root>`；`raw/cards.enUS.latest.json`、`raw/cards.zhCN.latest.json`、`generated/registries/*.json`
- Produces: `generated/registries/card_object_catalog.json`、`generated/registries/hero_skill_registry.json`

- [ ] **Step 1: 写失败契约测试**

```powershell
$script:Errors = @()
$catalog = Get-Content -Raw "$env:TEMP\card_object_catalog.missing.json"
if (-not $catalog) { throw "card_object_catalog.json must be generated before runtime" }
```

Expected: FAIL because catalog is absent。

- [ ] **Step 2: 实现生成器**

生成器必须读取原始 `cards.*.json` 和现有 registry，按 `CardId` 合并：

```json
{
  "_meta": {
    "schemaVersion": 1,
    "season": "season14",
    "gameBuild": "36.2.248348",
    "cardPoolIdentity": "0bcc979ae21df891",
    "source": "raw-hearthstone-json-2026-08-05",
    "capturedAt": "2026-08-05T01:52:16.883Z",
    "sha256": "<content-sha256>"
  },
  "kind": "cardObjectCatalog",
  "cards": [
    {
      "cardId": "BG36_180",
      "dbfId": 133453,
      "nameCn": "活体监牢",
      "nameEn": "Living Prison",
      "type": "MINION",
      "techLevel": 4,
      "cost": 0,
      "attack": 4,
      "health": 5,
      "races": ["ELEMENTAL"],
      "mechanics": [],
      "textCn": "发动（1）： 获得你在本回合中购买的下一个随从的属性值。",
      "canActivate": true,
      "activateCost": 1
    }
  ]
}
```

`canActivate` 只能来自 `activate_registry.json` 的精确 `CardId`，不能从文本猜测。

- [ ] **Step 3: 运行生成器和审计**

```powershell
node tools/build/generate_card_object_catalog.js --data-root E:\HDT-BobCoach-evidence\season14
node tools/build/generate_hero_skill_registry.js --data-root E:\HDT-BobCoach-evidence\season14
node tools/build/audit_card_object_catalog.js --data-root E:\HDT-BobCoach-evidence\season14
```

Expected: PASS，所有必填 `_meta` 字段存在，CardId 不重复。

- [ ] **Step 4: 提交**

```powershell
git add tools/build/generate_card_object_catalog.js tools/build/generate_hero_skill_registry.js tools/build/audit_card_object_catalog.js tests/test_phase1_season_data.ps1
git commit -m "build(season14): add card object and hero skill registry generation"
```

## Task 2: 运行时 CardObjectDatabase 与 HeroSkillCatalog

**Files:**
- Create: `src/BobCoach/Core/CardObjectDatabase.cs`
- Create: `src/BobCoach/Core/HeroSkillCatalog.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Test: `tests/Phase1CardObjectDatabaseBehavior.cs`

**Interfaces:**
- Consumes: `generated/registries/card_object_catalog.json`、`generated/registries/hero_skill_registry.json`
- Produces:

```csharp
internal sealed class CardObjectFact
{
    internal string CardId;
    internal string NameCn;
    internal string NameEn;
    internal string Type;
    internal int TechLevel;
    internal int Cost;
    internal int Attack;
    internal int Health;
    internal List<string> Races;
    internal List<string> Mechanics;
    internal string TextCn;
    internal bool CanActivate;
    internal int ActivateCost;
}

internal sealed class HeroSkillFact
{
    internal string HeroCardId;
    internal string HeroPowerCardId;
    internal string BuddyCardId;
    internal string PowerType;
    internal int Cost;
    internal int UnlockTurn;
    internal int UnlockTier;
    internal string TextCn;
    internal string UsePurpose;
    internal string TargetRule;
}
```

- [ ] **Step 1: 写失败测试**

```csharp
var db = new CardObjectDatabase();
AssertFalse(db.TryGet("missing-card-id", out fact), "unknown must fail closed");
AssertEqual("unknown", db.ResolveName("missing-card-id"), "unknown must not leak raw CardId");
```

- [ ] **Step 2: 实现加载**

`CardObjectDatabase` 复用 `Season14RegistryJsonParser` 和 `BobCoachDataPaths`，加载失败时 `IsLoaded=false`，所有查询返回 `false`。

- [ ] **Step 3: 接入 CardNameService**

`CardNameService` 先查 HearthDb，再查 `CardObjectDatabase`；两者都缺失时返回 `unknown`，不再返回裸 CardId。

- [ ] **Step 4: 跑测试并提交**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\run_behavior_tests.ps1
```

Expected: PASS。

## Task 3: EconomySnapshot 和 liveGold 统一

**Files:**
- Create: `src/BobCoach/Core/EconomySnapshot.cs`
- Create: `src/BobCoach/Core/LiveSceneAnalyzer.cs`
- Modify: `src/BobCoach/Core/GameState.cs`
- Modify: `src/BobCoach/GameStateExtractor.cs`
- Modify: `src/BobCoach/Core/ActionEnumerator.cs`
- Modify: `src/BobCoach/Core/DecisionVisualizer.cs`
- Test: `tests/Phase1EconomySnapshotBehavior.cs`

**Interfaces:**

```csharp
internal sealed class EconomySnapshot
{
    internal int LiveGold;
    internal int MaxGold;
    internal int SpentGold;
    internal bool CanRefresh;
    internal bool CanUpgrade;
    internal bool CanBuyAny;
    internal int GoldAfterUpgrade;
    internal int GoldAfterRefresh;
    internal IList<GameAction> AffordableActions;
}
```

- [ ] **Step 1: 写失败测试**

```csharp
var economy = EconomySnapshot.Build(state);
AssertEqual(3, economy.LiveGold, "turn 1 baseline");
AssertTrue(economy.CanUpgrade, "affordable upgrade");
AssertEqual(0, economy.GoldAfterUpgrade, "upgrade consumes all gold");
```

- [ ] **Step 2: 实现快照**

`GameState.LiveGold` 由 `GameStateExtractor` 写入；`ActionEnumerator` 和 `DecisionVisualizer` 只读 `LiveGold >= 0 ? LiveGold : Gold`。

- [ ] **Step 3: 修 UI 双重状态**

`BobCoachPlugin.DispatchRender` 不再在渲染阶段删除不可支付标记；不可支付动作应在 `CreateVisualPlan` 阶段就不过滤或标记为不可支付。

- [ ] **Step 4: 跑测试并提交**

```powershell
node tests\test_gold_tracker.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\run_behavior_tests.ps1
```

Expected: PASS。

## Task 4: Activate 动作接入

**Files:**
- Modify: `src/BobCoach/Core/ActionEnumerator.cs`
- Modify: `src/BobCoach/Core/VisualPlan.cs`
- Modify: `src/BobCoach/Core/DecisionVisualizer.cs`
- Test: `tests/Phase1ActivateActionBehavior.cs`

**Interfaces:**

```csharp
public enum ActionType
{
    BuyMinion,
    BuySpell,
    Refresh,
    Upgrade,
    SellMinion,
    FreezeShop,
    UseHeroPower,
    PickTrinket,
    PickDiscover,
    SendGoldToTeammate,
    Activate
}
```

`VisualPlan` 新增：

```csharp
public class ActivateHint
{
    public int BoardIndex;
    public string CardId = "";
    public string CardName = "";
    public int Cost;
    public string Reason = "";
}
```

- [ ] **Step 1: 写失败测试**

```csharp
var actions = enumerator.Enumerate(state);
AssertTrue(actions.Any(a => a.Type == ActionType.Activate && a.CardId == "BG36_180"));
```

- [ ] **Step 2: 实现候选生成**

遍历 `state.BoardMinions`，用 `Season14RulesEvaluator.TryGetActivate` 获取 `ActivateFact`；只有 `state.LiveGold >= fact.Cost` 且该随从本回合未发动时生成候选。

- [ ] **Step 3: 接入视觉计划**

`DecisionVisualizer.CreateVisualPlan` 从 `bestAction` 生成 `ActivateHint`，渲染层只消费该对象。

- [ ] **Step 4: 跑测试并提交**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\run_behavior_tests.ps1
```

Expected: PASS。

## Task 5: 运维门禁和发布检查

**Files:**
- Modify: `tools/build/validate_repository.ps1`
- Create: `tests/test_phase1_release_contract.ps1`
- Create: `tools/build/audit_unknown_card_ids.ps1`

**Interfaces:**
- Consumes: `generated/registries/card_object_catalog.json`、`generated/registries/hero_skill_registry.json`
- Produces: 发布前报告，列出未知 CardId、缺失中文名、版本不匹配项。

- [ ] **Step 1: 写失败测试**

```powershell
if (-not (Test-Path $catalog)) { throw "release must require card_object_catalog.json" }
```

- [ ] **Step 2: 实现未知卡审计**

扫描源码和测试中的可见卡名路径，确认不存在 `CardName ?? CardId` 或 `CardName ?? "随从"` 类裸 ID 兜底。

- [ ] **Step 3: 跑完整门禁**

```powershell
$env:BOBCOACH_HDT_DIR='D:\software\game\HDT-V2.7.0\HDT'
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\build\validate_repository.ps1
git diff --check
```

Expected: PASS。

- [ ] **Step 4: 提交并记录证据**

```powershell
git add tools/build/validate_repository.ps1 tools/build/audit_unknown_card_ids.ps1 tests/test_phase1_release_contract.ps1
git commit -m "ops(season14): add phase1 data and release audit gates"
```

## 后续阶段

本计划只执行 Phase 1。后续阶段不放入同一提交，避免单次改动过大：

| 阶段 | 内容 |
| --- | --- |
| Phase 2 | `UnifiedActionScorer`、英雄技能完整时机模型、暗赐资格/评分服务 |
| Phase 3 | `RecommendationPlan` 接管文字和 UI，legacy 只做 Shadow 对比 |
| Phase 4 | 真实 Power.log 回放、严重错误人工复核、Release 验收 |

## 成功标准

1. `CardObjectDatabase` 能精确解析已生成对象库，缺失 CardId 返回 `unknown`，不泄漏裸 ID。
2. `EconomySnapshot.LiveGold` 成为 `ActionEnumerator`、`DecisionVisualizer` 和 UI 支付判断的唯一输入。
3. 有 Activate registry 的随从可生成 `Activate` 候选，无 registry 时 fail closed。
4. 发布前审计能发现未知中文名、缺失对象、版本不匹配。
5. 全部测试仍跑在 legacy 可见和 Shadow 对比边界内，不接管玩家可见策略。

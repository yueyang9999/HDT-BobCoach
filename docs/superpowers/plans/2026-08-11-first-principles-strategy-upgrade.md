# BobCoach 第一性原理策略升级计划

日期：2026-08-11  
目标：把可见指导从“单动作规则推荐”升级为“多步效用规划”，让策略参与统一比较，同时保持 UI 最少信息。

## 现状结论

- 当前可见输出仍来自 `LegacyDecisionPolicy`，只消费 `DecisionEngine.GetBestAction()` 的单一 `GameAction`。
- `DecisionEngine.GetBestAction()` 存在大量 mid-game early-return；先命中的规则直接返回，后续候选没有参与比较。
- 二步前瞻已存在，但只覆盖当前回合内连续动作，不能模拟“本回合买、下回合打、再下回合双买”这类跨回合策略。
- `LiveGold` 已在最近版本贯通模拟器，但多步规划仍依赖规则层顺序。
- 已有 `RecommendationPlan`、`VisualPlan`、`DecisionOrchestrator`、`UnifiedRankingPolicy` 等骨架，但未形成单一可见决策链。

## 第一性原理

酒馆战棋决策的本质是：

> 在当前生命、当前铸币、未来铸币、场面战力、对手压力、卡池机会和未知信息下，选择一组能最大化长期存活与成长收益的动作序列。

因此算法链应统一为：

```text
GameState
  -> LiveScene
  -> ActionSpace
  -> Candidate Scoring
  -> MultiStep Planning
  -> RecommendationPlan
  -> Minimal VisualPlan
```

## 阶段拆解

### Phase 1：统一候选评分骨架

任务：

1. 新增 `DecisionEngine.GetRankedCandidates(GameState state, int topN)`。
2. 所有动作经过 `ActionEnumerator` 后，用 `Simulator` 模拟、`FeatureExtractor` 提取、`ValueFunction` 评分。
3. 安全规则作为硬约束：无金币、无目标、血太低、刚买随从保护等继续禁止。
4. mid-game 策略规则保留为偏置，不作为 early-return 的唯一来源。
5. `RecommendationPlan` 增加内部 `RankedActionKeys`，只用于验证与日志，不显示到 UI。

涉及文件：

- `src/BobCoach/Core/DecisionEngine.cs`
- `src/BobCoach/Core/RecommendationPlan.cs`
- `src/BobCoach/Core/VisualPlan.cs`
- `src/BobCoach/BobCoachPlugin.cs`
- `tests/test_ui_lifecycle.js`

验证：

- `GetRankedCandidates` 返回稳定顺序。
- 所有可见动作仍满足 live gold 门禁。
- `npm run test:behavior`、`npm run test:contracts` 通过。

### Phase 2：跨回合模拟

任务：

1. 新增 `TurnProjector`，模拟“结束当前招募回合 -> 下一回合金币初始化 -> 刷新/保留商店规则”。
2. 对南海卖艺者、理财随从、经济法术等机制，在计划中考虑下回合可执行动作。
3. 将二步前瞻升级为“当前回合 1-2 步 + 下一回合 1-2 步”的有限搜索。
4. `BestTwoStepAction` 只作为 UI 的动作序列来源，不把完整规划文本堆到面板。

涉及文件：

- `src/BobCoach/Core/TurnProjector.cs`
- `src/BobCoach/Core/DecisionEngine.cs`
- `src/BobCoach/Core/Simulator.cs`
- `tests/AlgorithmFeatureBehavior.cs`

验证：

- 能模拟“T1 拿南海卖艺者、T2 打出、T3 6 币双买”的资源链。
- 跨回合计划不会在当前铸币不足时推荐当前回合无法执行的动作。
- 相关行为测试通过。

### Phase 3：回放评估闭环

任务：

1. 使用现有 HDT 对战记录/插件决策快照，生成“推荐 vs 玩家选择”数据集。
2. 记录每回合推荐动作、实际动作、最终排名、血量变化、铸币使用。
3. 输出高频不一致项，供人工校准。
4. 不自动上线未验证权重。

涉及文件：

- `tools/evaluate/replay_decision_evaluator.js`
- `tests/test_replay_decision_evaluator.js`
- `docs/maintainer/REPLAY_EVALUATION.md`

验证：

- 对空输入 fail-closed。
- 对同一输入可复现结果。
- 输出文件不会写入仓库实时数据。

### Phase 4：规则数据化收敛

任务：

1. 将仍在 `DecisionEngine` 中硬编码的机制规则迁移到本地注册表。
2. 统一卡牌对象模型：版本、费用、星级、种族、关键词、机制、中文映射、关联英雄/伙伴/饰品/暗赐。
3. 新赛季更新只改数据，不改策略代码。

涉及文件：

- `src/BobCoach/Core/CardObjectDatabase.cs`
- `src/BobCoach/Core/Season14RulesEvaluator.cs`
- `docs/maintainer/CARD_OBJECT_DATABASE.md`

验证：

- 注册表校验、加载失败 fail-closed。
- 数据变更后无需重写 `DecisionEngine`。
- 所有用户可见卡名继续走统一中文映射。

## 最终验收

1. `npm run test:behavior` 通过。
2. `npm run test:contracts` 通过。
3. `npm run test:package` 通过。
4. UI 仍保持最小信息，不展示内部评分。
5. 多步链和跨回合链有自动化回归测试。

## 2026-08-12 进展

Phase 1 已落地环境基线层：

- 新增 `SeasonMetaBaseline`，让 `CompGuideCatalog` 可用当前环境覆盖 guide tier 和核心 CardId。
- 新增 `season14_meta_baseline.json`，将野兽龙虾流、亡灵魅影宰割列为 T0，并补上 `BG36_201` 深潜狮子鱼。
- 同步 36.2.1 黑暗之赐热修：Charisma 6-9、Steady Growth max 5、Battle Scars / Death's Embrace / Spell Siphon max 10。
- `DarkGiftDecisionService` 增加前期持有和最后一次机会规则。
- `GlobalReplayRecorder.recommend` 增加 `mainlinePlan`，用于离线回放校正。

下一步仍是 Phase 3 回放评估闭环：用 `mainlinePlan` 对比玩家实际买核心、升本、出售和最终名次，再调整策略权重。

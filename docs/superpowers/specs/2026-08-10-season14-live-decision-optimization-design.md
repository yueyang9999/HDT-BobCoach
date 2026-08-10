# BobCoach Season14 实时指导算法优化设计

Date: 2026-08-10
Status: Draft for user review
Scope: Season14 单人模式可见指导链路；不包含 BGDUO、不承诺胜率或名次提升。

## 1. 背景

用户在实际 Season14 对局中反馈以下 6 类高频问题：

1. 升本节奏不对：没有综合自身阵容战力和血量，仍强行提示升本。
2. 随从推荐呆板：不能理解卡牌内容和铸币资源分配；有“发动”机制的卡牌没有提示使用发动。
3. 出售/手牌建议异常：手上没有可替代牌也会提示出售；手上有不能提升战力的牌仍提示打出。
4. 文字指导面板与实际 UI 不一致：状态栏文字、商店高亮、按钮光晕可能来自不同/过期状态。
5. 发现提示时机错误：非触发发现机制时误捕获酒馆招募面板目标；部分卡牌显示 ID 而不是中文名。
6. 动态铸币数未正确进入逻辑：铸币不足仍提示购买/刷新，且该提示出售时没有出售建议。

本设计的目标是把可见指导链路从“规则早退 + 分散文本”重构为“同一份实时局面分析 + 同一份可解释推荐计划”，并先把黑暗之赐/发动/新赛季数据规则纳入统一决策服务。

## 2. 现状诊断

### 2.1 用户看到的是 legacy 决策

`DecisionOrchestrator` 固定使用 `DecisionRuntimeMode.Shadow`，没有已批准模型资源；玩家可见输出来自 `LegacyDecisionPolicy`，其内部是 `DecisionEngine.GetBestAction()`。

- `DecisionEngine.cs` 的 `GetBestAction` 由大量 early-return 规则组成：R0 升本、R1 保命、R2-R9 曲线、R-spell、R-triple、刷新审核、卖牌审核、二步前瞻、MCTS。
- 很多动作在统一评分前已被规则直接返回，买/卖/升/刷/冻/技能没有稳定在同一评分面比较。
- 新 `ActionFeatureExtractor` 虽有 45 个特征，但当前 44 个 `Set` 调用中有 24 个写死 `0d, 0d`，且 `ProductionSeasonFeatureExtension` 返回 `unknown`，因此统一排名在真实生产链路中不可用。

### 2.2 黑暗之赐尚未进入决策层

- `DarkGiftRegistry` 已加载 43 个官方 Gift、8 行 offering 表、105 条原始效果、40 个唯一效果名、10 张关联卡。
- `Season14RulesEvaluator` 只提供 `TryGetDarkGift`、`TryGetOfferingTier`、`TryGetEligibleGifts`，没有候选过滤、候选评分、效果价值或机制交互。
- `DarkGiftTipAdvisor` 只做状态门控和文本 `暗赐可发现 T.. 本.. 金.. 场.. 店.. 手..`。
- `OverlayRenderer.ShowDarkGiftCandidates` 只按 `1. CardName` 显示候选，不显示星级、效果、评分、推荐原因。

### 2.3 “发动”机制没有候选动作

- `ActionType` 没有 `Activate`。
- `ActionEnumerator` 不枚举发动类动作。
- `ActivateRegistry`/`ActivateFactSource` 可读取 `activate_registry.json`，但生产决策、视觉计划、UI 均未消费。

### 2.4 动态铸币与 UI 使用不一致

- `GameStateExtractor` 已有 `GoldTracker` 和 `liveGold` 相关修复，但 `ActionEnumerator`、`DecisionEngine`、`DecisionVisualizer`、`TurnPhaseEngine` 仍多处直接读 `state.Gold`。
- UI 渲染阶段重新计算 `liveGold`，但计划在 `CreateVisualPlan` 阶段已经基于旧 `state.Gold` 生成，存在“计划来自旧金币、渲染时再过滤”的双状态问题。
- `planHash` 已加入金币边界，但 `FilterAffordableShopScores` 和 `IsShopMarkerAffordable` 仍依赖 `state.Gold` 传入值；若调用侧使用旧值，UI 仍可能显示不可支付目标。

### 2.5 文字与 UI 没有单一计划源

- `DecisionVisualizer.CreateVisualPlan` 生成 `VisualPlan`。
- `BobCoachPlugin.DispatchRender` 随后大量直接改写 `plan.Status.HintLine`、`plan.ShopMarkers`、`plan.UpgradeHint`、`plan.RecommendedActionType`。
- 渲染时再读 `_lastCombatPrediction`、`_cachedState` 等插件字段，造成“决策计划、状态栏文字、商店高亮、按钮光晕”来自不同状态版本。

### 2.6 发现来源仍有非权威路径

- 现有 T21-T26 已把普通发现门控收敛到 `WaitThenShowChoices`，但 `GameStateExtractor` 仍保留 zone6 兜底、HDT 实体扫描和 `ScoreDiscoverOptions` 的简单文本匹配。
- `ScoreDiscoverOptions` 使用卡名包含“铜须/瑞文/达瑞尔”等文本判断引擎，不是精确 CardId/机制事实。
- `CardNameService` 已作为统一中文名入口，但 `DecisionVisualizer`、PowerLog 候选、暗赐候选仍存在 `candidate.CardName ?? candidate.CardId` 兜底，缺失映射时会显示裸 ID。

## 3. 目标

1. 让用户看到的指导来自同一份实时局面分析结果。
2. 买、卖、升、刷、冻、技能、发动、发现、饰品、黑暗之赐进入统一候选/评分边界。
3. 升本建议必须考虑自身阵容战力、血量、对手战力、预期伤害、剩余铸币和替代动作。
4. 动态铸币数成为所有费用合法性、评分、UI 展示的唯一事实源。
5. 黑暗之赐的官方规则、候选效果、随从条件、回合/本数、费用和机制搭配进入可计算服务。
6. 所有用户可见卡名必须通过中文映射；无法映射时显示 `unknown`，不显示裸 CardId。

## 4. 非目标

- 不在此设计中训练或嵌入生产模型。
- 不激活 unified 模型到玩家可见链路。
- 不宣称胜率、名次、Top4 或策略质量提升。
- 不做完整战斗模拟器或深度强化学习。
- 不覆盖 BGDUO。
- 不重写全部历史规则；legacy 保留为回退和 Shadow 对比。

## 5. 目标架构

```text
GameStateExtractor
    -> LiveSceneAnalyzer
        -> BoardCompositionProfile
        -> EconomySnapshot
        -> SurvivalRisk
        -> ActionSpace
        -> UnknownCoverage
    -> CandidateGenerator
        -> UnifiedActionScorer
        -> DarkGiftDecisionService
        -> RecommendationPlan
    -> OverlayRenderer
```

### 5.1 LiveSceneAnalyzer

新增核心服务，职责是把 `GameState` 转成稳定的实时局面摘要：

- `BoardCompositionProfile`：当前种族计数、引擎卡、三连进度、核心组件、可出售保护、当前战力。
- `EconomySnapshot`：`liveGold`、`maxGold`、已用铸币、可支付动作集合、升本后剩余铸币、刷新后剩余铸币。
- `SurvivalRisk`：我方战力/对手平均战力比、血量/护甲、预期伤害、低血量危险等级。
- `ActionSpace`：合法候选集合、可支付候选集合、后续动作空间。
- `UnknownCoverage`：未知 CardId、未知机制、缺失数据数量。

该服务必须被决策、视觉、UI 共同复用，不能每层各自计算。

### 5.2 RecommendationPlan 单一事实源

新增/收敛 `RecommendationPlan` 为唯一输出：

- 一个 `DecisionAction`（动作类型、目标索引、CardId、费用、原因、风险等级）。
- 商店标记、卖牌标记、手牌标记、升本提示、刷新提示、冻结提示、发动提示、暗赐提示全部来自同一份计划。
- 文本和 UI 元素都从该计划生成，禁止渲染阶段再次猜测动作语义。
- `planHash` 必须包含 `liveGold`、动作类型、目标 CardId、文本摘要、风险等级、面板签名。

### 5.3 UnifiedActionScorer

第一版不依赖模型，采用确定性软评分：

- 先使用 `HardConstraintFilter` 过滤非法、不可支付、无效目标动作。
- 对每个候选计算一组可解释因子：立即战力、阵容兼容、铸币效率、三连进度、生存风险、转换成本、未知惩罚。
- 禁止在评分前用策略规则直接 early-return；legacy 只作为 Shadow 对比和回退。
- `ActionType` 新增 `Activate`，`ActionEnumerator` 从 `ActivateRegistry` 读取可发动随从并生成候选。

### 5.4 DarkGiftDecisionService

新增服务，包含：

1. 资格规则化：开放回合、offering tier、候选随从限制、候选 Gift 限制、稀有 Gift 权重、玩家剩余次数。
2. 时机建议：当前是否值得花 3 金；综合 `liveGold`、阵容战力、剩余铸币、对手压力、当前本数、候选期望。
3. 候选评分：对每个“随从 + 黑暗之赐”组合计算即时战力、成长价值、机制协同、阵容兼容、三连/核心配合、风险。
4. 交互规则：如 Echoing Voice 只适合战吼随从、Jaws of Death 只适合亡语随从、Amalgamation 只适合无种族随从等。
5. 失败关闭：注册表缺失、候选无效果映射、随从无种族/机制事实时，候选标 `unknown`，不给出高置信推荐。

## 6. 问题到修复映射

| 用户问题 | 根因 | 设计修复 |
|---|---|---|
| 升本不看阵容战力和血量 | R0/R2/R-time 等规则只检查回合/板面数/金币，未统一评估对手、血量、伤害、替代动作 | `UpgradeSafetyGate` + `UnifiedActionScorer`：只有升本综合收益显著且生存风险可接受才进入主推荐 |
| 不提示发动机制 | `ActionType` 缺 `Activate`，注册表未接入决策 | 新增 `ActionType.Activate`，`ActivateRegistry` 进候选生成和 UI |
| 出售/手牌建议异常 | 卖牌允许条件只看 `Gold+1>=3` 或手牌有无随从，未验证替换后战力提升；手牌“打”只看简单优先级 | 卖牌必须计算“出售后可用动作空间 + 替换后战力净提升 + 动态铸币”，手牌“打”必须与当前最弱随从净收益比较 |
| 文字与实际 UI 不一致 | `VisualPlan` 生成后被多处改写，渲染层再读旧缓存 | `RecommendationPlan` 单一事实源；状态文本、商店高亮、按钮光晕只消费同一对象 |
| 发现时机错误/裸 ID | 仍有 zone6 和文本匹配路径；候选名兜底为 CardId | 发现只接受权威 Power.log `WaitThenShowChoices` 触发；所有候选走 `CardNameService`/赛季注册表；未知显示 `unknown` |
| 动态铸币未进入逻辑 | `state.Gold` 在决策、视觉、渲染多个阶段被重复读取 | `EconomySnapshot.liveGold` 作为唯一输入；所有费用检查、planHash、UI 过滤使用同一值 |

## 7. 数据与中文映射

### 7.1 注册表校验

- 新增覆盖审计：minion、tavern spell、trinket、activate、dark gift 注册表必须包含 `_meta` 的 season/build/cardPoolIdentity/sha256。
- 所有用户可见候选卡名先查 `CardNameService`，再查 Season14 本地注册表，最后查 HearthDb zhCN；全部失败显示 `unknown`。
- PowerLog 候选只携带 CardId/CardName 时，渲染前必须重新解析 CardId；不允许把 `CardName` 原样当可信中文名。

### 7.2 黑暗之赐规则化

- 将 `dark_gift_registry.json` 的规则文本拆成结构化谓词：
  - `CanBeOffered(candidateMinion, gift, turn, tier, lobbyTribes)`
  - `IsEligibleGift(gift, turn, minion, state)`
  - `EstimateGiftValue(gift, minion, state)`
- 未知谓词返回 `false` 或 `unknown`，不猜测。

## 8. 验证策略

### 8.1 自动化测试

- 新增 `LiveSceneAnalyzerBehavior`：阵容、经济、生存、未知覆盖。
- 新增 `EconomySnapshotBehavior`：0/1/2/3 金币、免费刷新、首购免费、升本后剩余铸币。
- 新增 `ActivateActionBehavior`：注册表已知卡可生成发动候选，未知卡 fail closed。
- 新增 `SellAndHandRecommendationBehavior`：无替换、替换无净提升、手牌弱于最弱随从均不推荐。
- 新增 `DarkGiftDecisionServiceBehavior`：offering 规则、候选限制、费用、组合评分、未知 fail closed。
- 新增 `RecommendationPlanConsistencyBehavior`：状态文本、ShopMarkers、UpgradeHint、SellMarkers 来自同一计划。
- 新增发现来源测试：非权威 entity/zone6 路径不得打开发现面板。

### 8.2 真实日志回放

- 使用现有真实 Power.log 构造决策场景，验证：
  - 发现面板只在权威触发时出现；
  - 暗赐候选显示中文名和评分；
  - 金币不足时无购买/刷新/升本 UI；
  - 升本建议与局面风险一致。
- 日志回放只作为工程验证，不生成效果标签，不宣称策略质量提升。

## 9. 实施阶段

### P0：数据与中文映射审计

- 校验 minion/tavern spell/trinket/activate/dark gift 注册表完整性。
- 建立 `unknown` 卡名审计日志和覆盖报告。
- 新增注册表/中文映射测试。

### P1：实时局面分析 + 动态铸币

- 实现 `LiveSceneAnalyzer` 和 `EconomySnapshot`。
- 把所有决策入口改为消费 `liveGold`。
- 修 `planHash`、`FilterAffordableShopScores`、UI 过滤的一致性问题。

### P2：统一动作评分

- 新增 `ActionType.Activate`。
- 将升本、买、卖、刷、冻、技能、发动纳入 `UnifiedActionScorer`。
- 保留 legacy 为回退和 Shadow 对比。

### P3：黑暗之赐决策服务

- 把官方 Gift/offering/限制规则结构化为可计算谓词。
- 实现“是否花 3 金现在点”和“候选随从+赐福评分”。
- 接入 `RecommendationPlan` 和暗赐 UI。

### P4：真实日志验证与 UI 一致性

- 用真实 Power.log 回放 6 类问题场景。
- 修复发现误触发、中文映射、文字/UI 不一致。
- 全量跑仓库门禁后进入 Release/部署。

## 10. 约束

- 不 push、不 rebase、不 reset --hard、不 force push、不删历史。
- 不改 `.env`/密钥/CI/CD/database schema；不读写 `%APPDATA%\bob-coach\data`；不动 BGDUO。
- 不编造赛季卡牌、机制、权重、tier 或效果；缺数据显式 `unknown`。
- 不宣称胜率、名次或策略质量提升。
- 暗赐真实 UI、`BG36_MidGameEffect_000`、`BG36_HERO_101p`、“配置 RG”继续保持 `unknown`。
- 没有新 HDT/Power.log/截图/录像证据时，源码测试通过不得标为真实 UI PASS。

## 11. 成功标准

1. 金币不足时不显示不可支付购买/刷新/升本/卖牌 UI。
2. 升本建议不再只看回合和板面数，必须体现战力、血量、对手和替代动作。
3. 发动机制卡能生成可执行提示。
4. 出售/手牌建议必须通过“战力净提升”门控。
5. 发现面板只在权威触发时显示，非权威来源不打开。
6. 用户可见卡名不再出现裸 CardId。
7. 暗赐候选显示中文名、费用、评分和推荐原因；未知数据不假装有把握。
8. 状态栏文字和实际 UI 元素来自同一 `RecommendationPlan`。

## 12. 后续门禁

- 设计评审通过后，使用 `writing-plans` 生成分阶段实现计划。
- 实现仍按 RED -> 最小实现 -> GREEN 推进。
- 完成 P0-P4 后跑完整仓库门禁，再决定是否部署。
- 所有策略改善仍保持 legacy 可见、新逻辑 Shadow 对比，直到另有批准。

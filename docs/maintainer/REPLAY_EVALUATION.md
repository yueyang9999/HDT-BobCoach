# 回放差异评估

## 数据源

- HDT/BobCoach 插件回放：
  - `%APPDATA%\bob-coach\replays\replay_full_*.json`
  - `%APPDATA%\bob-coach\replays\snapshot_*.json`
- Firestone 历史回放：
  - 旧 Claude Code 项目：`E:\claude_project_hdt\bob-coach\data\replay_jsons`
  - 旧 XML：`E:\claude_project_hdt\bob-coach\data\replay_xmls`
- Firestone 当前回放元数据：
  - Overwolf IndexedDB WAL 可读近期 `reviewId`
  - Lambda API 仍可返回 `replayKey`
  - `xml.firestoneapp.com` 可直接下载匿名 XML，30 场近期回放已保存到证据目录

## 分析工具

`tools/evaluate/replay_snapshot_analyzer.js` 是离线回放差异分析器：

```powershell
node tools/evaluate/replay_snapshot_analyzer.js `
  --replay-dir "%APPDATA%\bob-coach\replays" `
  --prefix "snapshot_202608" `
  --cards "%APPDATA%\Overwolf\lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob\cards_zhCN.gz.json" `
  --output "E:\HDT-BobCoach-evidence\season14\generated\replay-snapshot-diff-202608-v1.json" `
  --decisions "E:\HDT-BobCoach-evidence\season14\generated\replay-snapshot-decisions-202608-v1.jsonl"
```

`tools/evaluate/firestone_xml_to_snapshot.js` 可把 Firestone XML 转为标准化回合快照：
```powershell
node tools/evaluate/firestone_xml_to_snapshot.js `
  --xml "E:\HDT-BobCoach-evidence\season14\raw\firestone-replays\xml" `
  --metadata "E:\HDT-BobCoach-evidence\season14\generated\firestone-review-index-20260811.json" `
  --output-dir "E:\HDT-BobCoach-evidence\season14\generated\firestone-turn-snapshots-20260811"
```

输出包含：

- 推荐动作与玩家实际动作的类型匹配率
- 高频不匹配模式
- 硬约束问题：购买费用、升级费用、刷新、空场出售、槽位数不一致
- 候选评分问题：推荐动作不是最高分、评分列表残留不可支付动作

## 2026-08 近期发现

- `recommended-not-top-score` 出现 401 次。`ActionScoresJson` 当前用原始 `ValueFunction` 重算，和 `DecisionEngine.GetRankedCandidates()` 的统一评分不一致，导致回放评分审计看到推荐动作不是最高分。
- `scored-multiple-recommended` 出现 29 次，主要是多个英雄技能候选同时被标记为推荐，需要改为单一 `bestAction` 匹配。
- 近期快照没有发现 0 铸币购买随从；0 铸币刷新存在，但对应局有 `BACON_FREE_REFRESH_COUNT` 标签，属于免费刷新，不应直接视为缺陷。
- 快照时间线没有记录刷新/冻结操作，导致 `Refresh/FreezeShop -> 后续动作` 的差异统计偏高，需要后续从商店变化或 Power.log 推断。

## 本轮修复

- 快照新增 `LiveGold`、`FreeRefreshCount`、`TavernUpgradeCost`，后续审计可以直接使用真实动态铸币和真实费用。
- 快照新增 `FrozenShop`、`ShadowRecommendedActionJson`、`ShadowActionScoresJson`。
- Shadow 评分复用 `DecisionEngine.GetRankedCandidates()`，推荐标记只给 Shadow 第一名，并按 `ActionKey` 去重。
- 英雄技能候选按 `CardId` 去重，修复多个 `UseHeroPower` 同时标记推荐的问题。
- 回放时间线补录刷新/冻结推断，分析器也支持离线推断。
- 新增 `firestone_review_extract.js`，并从 Firestone XML 完成 30 场卡牌覆盖审计。
- 新增 `PlayMinion`、`UseSpell` 候选动作，枚举、模拟器、硬约束、统一评分和 UI 文案均已接线。
- 新增回放分析器及其合成快照测试。

## 后续方向

1. 继续完善 Firestone XML 的回合级动作适配器，把 30 场真实回放纳入策略差异比对。
2. 新 Shadow 快照字段需要至少一局新实战，验证评分问题在实际回放中归零。
3. 在效果验收数据足够后，再决定是否将 unified policy 从 Shadow 切为可见策略。

## 全局事件记录

`GlobalReplayRecorder` 以事件增量和 JSONL 流式落盘，替代“每帧全量快照”的内存模型：

- `meta`：对局元信息（英雄、玩家、build、season）。
- `state`：仅在有状态变化时记录，包含金币、血量、星级、商店/板面/手牌、对手、发现/饰品候选等。
- `action`：购买、出售、打出、法术、升本、刷新、冻结、英雄技能、队友铸币等动作。
- `choice` / `choiceCompleted`：Power.log 的发现、饰品、黑暗之赐、被动选择等候选与选择结果。
- `recommend`：当前帧推荐动作、legacy/Shadow 评分、特征向量。
- `end`：结算名次、回合数、三连数、板面峰值。

落盘路径为 `%APPDATA%\bob-coach\replays\global_*.jsonl`。该格式与 Firestone XML 一样保存事件顺序，但只记录增量，内存占用由 `256KB / 2s` 的流式 flush 约束，不做整局内存缓存。

离线汇总工具：

```powershell
node tools/evaluate/global_replay_analyzer.js --dir "%APPDATA%\bob-coach\replays"
node tools/evaluate/global_replay_analyzer.js --file "%APPDATA%\bob-coach\replays\global_xxx.jsonl"
```

输出事件类型、动作类型、选择类型、状态事件和推荐事件统计，用于快速检查“全局记录是否完整”以及是否有事件断层。

`2026-08-13` 起，分析器同时输出 `dynamicPlans` 和 `anomalies`：

- `dynamicPlans`：每个 `recommend` 的 `mainlinePlan` 回合字段，包括 `turn`、`mode`、`pivotScore`、`shouldHoldForNextTurn`、`shouldBuyCoreFirst`、`hasTripleInShop` 和 `alternativeCoreCardIds`。
- `anomalies`：回合级硬约束和动态模式异常，包含 `code`、`turn`、`mode`、动作类型、动态金币和泛用替代核心。
- `totals.anomaliesByCode`：对整局异常代码计数，便于新回放到达时先看零异常，再逐回合复核。

当前异常代码：

- `zero-gold-paid-action`：推荐动作有费用，但该动作前最近一次 `state` 的 `liveGold <= 0`，且不是免费刷新。
- `negative-score-pivot-or-hedge`：Pivot/Hedge 的 `pivotScore < 0`。
- `pivot-with-insufficient-alternative-cores`：Pivot 只有一个非泛用替代核心，且没有三连证据。
- `generic-core-evidence-in-pivot`：替代核心集合中仍有泛用引擎卡，例如 `BG36_764`、`BG_TTN_401` 或 `BG25_354`。
- `economy-mode-overridden`：`shouldHoldForNextTurn=true`，但最终模式不是 `Economy`。
- `commit-mode-overridden`：`shouldBuyCoreFirst=true`，但最终模式不是 `Commit`。

状态匹配按事件顺序进行，只使用推荐事件之前的最近状态，避免回合结束后的 0 金币状态倒推污染早期推荐。泛用卡列表与 `MainlineStrategyPlanner.IsGenericPathEvidenceCard` 同步维护；新增泛用引擎卡时两处需要一起更新。

## 事件级回放 v3

在原有 `state/action/choice/recommend` 之上，新增两类紧凑原始事件：

- `powerAction`：Power.log 直接识别的玩家动作证据，包括 `buy`、`sell`、`play`、`spell`、`refresh`、`level`、`freeze`、`heroPower`、`darkGift`、`triple`。记录 `cardId`、`targetId`、`targetCardId`、`turn`、`phase`，不写原始日志行。
- `combat`：战斗阶段关键事件，`kind=attack/damage/death`，记录攻击方/防守方实体、卡牌、玩家、伤害、生命值和回合/阶段，用于还原攻击顺序与结算路径。

实现链路：

- `PowerLogParser` 为 `PLEvent` 补充 `PlayerId/Zone/TargetCardId/TargetPlayerId/BeforeHealth/AfterHealth`。
- `PowerLogWatcher` 暴露 `PowerLogEventDetected`，增量读取时逐事件推送给回放记录器。
- `BobCoachPlugin` 设置 `PowerLogWatcher.LocalControllerId`，只记录本地玩家动作；战斗事件保留双方实体身份。
- `GlobalReplayRecorder` 使用锁保护流式写入，`powerAction/combat` 均按签名去重。
- `tools/evaluate/global_replay_analyzer.js` 输出 `powerActionsByKind` 和 `combatsByKind`。
- `tools/evaluate/replay_sequence_compare.js` 优先采用 Power.log 动作证据，并跳过同键的 state 推断重复项。

体积策略：不复制 Power.log 原文，不保存 `RawLine`，事件行只保留实体/卡牌/玩家/阶段等必要字段。预计一整局约 15 回合的全局 JSONL 从当前 1MB 级增至 2-5MB 级，仍远小于原始 Power.log。

## 同局双插件对齐案例

最新双插件同局：

- 我们：`%APPDATA%\bob-coach\replays\global_20260812_151639_BG30_HERO_304_792926b3.jsonl`
- Firestone：`a69b4eee-6f35-430d-95ba-1f77d791340e`
- 英雄 `BG30_HERO_304`，13 回合，第 4 名，双方英雄/回合数已对齐。

覆盖对比：

- 我们：3503 个事件，其中 `powerAction=261`、`combat=2097`、`choice/choiceCompleted=35`。
- Firestone XML：4738 个 `Block`，但适配器目前只还原 121 个动作、36 个选择，没有战斗攻击顺序/伤害结算。
- `replay_sequence_compare.js` 的 `coverage` 字段可直接输出上述覆盖。

当前动作级 `strictComparison` 仍有 94 处差异，主要不是“我们漏动作”，而是两类噪音：

- Firestone 适配器会把部分发现/黑暗之赐/奖励选牌误判为 `buy/play`。
- 我们的区域推断会捕获 token/自动入场，不能直接等同于玩家手动打出。

下一步对齐路径：以 Power.log `BLOCK_START BlockType=PLAY Entity=... zone=HAND` 作为“玩家手动打出”的权威证据，购买/出售/刷新/升本直接使用 `TB_BaconShop_*` action block；Firestone XML 的 `Block` 继续作为交叉校验而不是动作主源。

# 回放差异与评分一致性收尾

Date: 2026-08-11
Status: In progress

## 结论

- 已把回放快照拆成可见 legacy 推荐和 Shadow 统一评分两层，消除评分审计与 `GetRankedCandidates` 的评分面不一致。
- 已修复多个英雄技能候选同时标记推荐的问题。
- 已补充刷新/冻结动作推断与 `FrozenShop` 快照字段。
- Firestone 已提取近期 30 个 reviewId 和元数据；XML 下载仍因登录 Cookie 被 Overwolf 独占而阻塞。

## 代码改动

- `TurnSnapshot` 新增 `FrozenShop`、`ShadowRecommendedActionJson`、`ShadowActionScoresJson`。
- `BobCoachPlugin` 的 `SerializeShadowActionScores` 复用 `DecisionEngine.GetRankedCandidates()`，只给 Shadow 第一名打推荐标记，并按 `ActionKey` 去重。
- `ActionEnumerator` 对相同 `HeroPower.CardId` 去重，避免同一个英雄技能生成多个候选。
- 回放时间线在商店整体更换且金币或免费刷新次数下降时补录 `refresh`，在冻结状态翻转时补录 `freeze`。
- 新增 `tools/evaluate/firestone_review_extract.js`，从 Overwolf IndexedDB WAL 提取去重 reviewId。
- 新增 `src/BobCoach/Core/ShadowScoreSerializer.cs`，Shadow 评分序列化抽成纯组件并可直接单测。
- 新增 `tools/evaluate/firestone_xml_to_snapshot.js`，把 Firestone XML 转成标准化回合快照；首批 30 场生成 349 个回合。

## 对抗审查修复

- 修复 `ActionEnumerator` 在 `HeroPowers` 含 null 元素时先访问 `power.CardId` 导致 NRE 的问题。
- 回放分析器不再把 legacy 饰品推荐误报为 Shadow 评分缺失。
- Firestone reviewId 提取支持大写 UUID，并补了对应回归测试。
- 修复 `_SKIN` 英雄被误判为不可用、导致 snapshot 英雄全部变成克尔苏加德的问题。
- 修复最终名次提取误取其他玩家第 8 名的问题。
- 新增 `HandCombatCardPolicy`，好斗的斥候 `BG32_330/BG32_330_G` 不再被推荐“打出/上阵”。
- 暗赐点击提示在动态金币 `<3` 时直接 fail closed，不再显示“建议点击黑暗发现”。

## Firestone 证据

- `E:\HDT-BobCoach-evidence\season14\generated\firestone-review-ids-20260811.json`
- `E:\HDT-BobCoach-evidence\season14\generated\firestone-review-index-20260811.json`
- 30 场全部为 build 248348，日期 2026-08-05 到 2026-08-11。
- 使用 Firestone 扩展实际使用的 `xml.firestoneapp.com` 后，30 场 ZIP 全部下载成功：
  `E:\HDT-BobCoach-evidence\season14\raw\firestone-replays`
- 30 场 XML 中提取 1516 个唯一 CardId，本机 `cards_zhCN.gz.json` 覆盖 0 未知。

## 验证

- `node tests/test_firestone_review_extract.js` PASS
- `node tests/test_replay_snapshot_analyzer.js` PASS
- `node tests/test_structural_upgrade_contract.js` PASS
- Release build PASS，`BobCoach.dll` bytes=901632
- 审查后 Release：bytes=901632，SHA256=`4522C8A76E224306BE7503818CD7AD4829BB09A46583B4A92CCE3610F3D038F7`
- 最终 Release：bytes=902144，SHA256=`6B0EEF92E5B796910C53E8D25B400AAB55BDBCD1946C3A927FD1E785BE50EE36`
- 英雄/名次修复 Release：bytes=902144，SHA256=`1E1550DD614B5C92DE79CBB07D56725ACD9F695EB394DA852029CE398FBF2CE1`
- 手牌/暗赐修复 Release：bytes=902656，SHA256=`01D6D78430E57070C476836DD0F652679ECC578CAEDEA0EA1FBDFFA02AD4249F`

## 剩余风险

- Firestone XML 的回合级动作适配器仍需完善；当前 HDT 快照仍是策略差异比对的主数据源。
- 新 Shadow 快照字段需要再打一局新回放后才能在实际回放语料中验证归零。
- 刷新/冻结推断目前基于状态变化，Power.log 直接动作事件仍可进一步校准。
- 效果验收仍保持 `blocked-by-season-data`，未切换统一策略为玩家可见。

## T29：10 局实机问题收敛

Date: 2026-08-12
Status: Deployed

### 本轮回放暴露

- 尤格萨隆：回合 2 重复“打-南海卖艺者”，回合 3 锁定手牌仍被打出，回合 7 动态铸币不足仍推购买。
- 钩牙：变废为宝是“移除友方随从 + 2 选 1 发现”，技能目标与发现面板都没有进入指导链路。
- 塞纳留斯：动态铸币 0 仍推非 0 费法术；好斗的斥候由效果抓回手牌后仍被打出。
- 米罗克：时空酒馆标签被遮挡、长链文字被单行覆盖、双时光币只给一个推荐。
- 牛头人酋长：1 星纳迦因 Spellcraft 附加资源压制 2 星进击野猪人。
- “能使”系列没有被规则化，生成属性值法术的卡与累积增强卡没有被识别。

### 本轮修复

- `ActionEnumerator` 对 `IsFrozen` 手牌 fail closed，锁定牌不再生成打出/使用候选。
- `HandCombatCardPolicy` 兼容 `BG32_330_G/BG32_330t/BG32_330_Ge` 等派生 ID。
- `HeroPowerRuleCatalog.RequiresTarget` 对钩牙 `TB_BaconShop_HERO_67` 保持目标要求，不再因 `HasDiscover` 跳过“先移除友方随从”。
- `PowerLogParser` 显式信任 `TB_BaconShop_HP_075`，2 选 1 英雄技能发现可进入发现面板。
- `ActionEnumerator` 出售候选跳过 `RecentlyBoughtEntityIds`；出售门控统一到第 4 回合后，且要求出售后真的能支付目标。
- `GetShopCardScores` 的星级质量惩罚不再依赖板面数量，T3 起商店存在高星卡时低星卡会降权。
- `AttachedResourceBonus` 的 Spellcraft 奖励按星级缩放，避免 1 星纳迦无条件压制 2 星随从。
- 新增 `StatGiftRuleEvaluator` 并接入 `DecisionEngine`：生成“能使随从获得属性值的酒馆法术/元素”的卡和“额外获得”增强卡会互相加权。
- 状态条动作链保留多步 `\n`，不再被 `拿→` 单行动作覆盖；时空酒馆标签移入候选卡底部内侧并提高 ZIndex。

### T29 验证

- `npm run test:behavior` PASS
- `npm run test:contracts` PASS
- 发布构建 PASS，`BobCoach.dll` bytes=913408
- 发布构建 SHA256=`5B81A14B245B1F42A11CB41C7EB0D145C1796E9A55D10841FD169075359CC309`
- 离线 ZIP 确定性校验 PASS
- 可部署产物：`E:\HDT-BobCoach-evidence\season14\build\20260811-t29\BobCoach.dll`
- 离线 ZIP：`E:\HDT-BobCoach-evidence\season14\build\20260811-t29\package\BobCoach-1.1.0-win-x64.zip`
- 已部署 4 个 HDT 插件目录，全部校验为 `5B81A14B245B1F42A11CB41C7EB0D145C1796E9A55D10841FD169075359CC309`。
- 部署归档：`E:\HDT-BobCoach-evidence\dll-archive\20260812T012945427Z-e8ea5fa4`
- `npm run test:package` PASS

## T30：全局事件记录

Date: 2026-08-12
Status: Deployed

### 设计

- 新增 `GlobalReplayRecorder`，以事件增量 + JSONL 流式落盘，不再依赖整局内存缓存。
- 记录 `meta/state/action/phase/choice/choiceCompleted/recommend/end` 八类事件。
- 插件状态帧、动作推断、Power.log 选择与结算都会写入 `global_*.jsonl`。
- 新增 `tools/evaluate/global_replay_analyzer.js` 离线汇总，快速核对事件覆盖。

### 验证

- `tests/test_global_replay_recorder.js` PASS
- `tests/test_global_replay_analyzer.js` PASS
- `npm run test:behavior` PASS
- `npm run test:contracts` PASS
- `npm run test:package` PASS
- Release：`BobCoach.dll` bytes=925696
- SHA256：`67D48F95F73FE09A7171FF44A69BBAC9F2980BDA86695E78AFD826163BCCFB0D`
- 已部署 4 个 HDT 插件目录
- 部署归档：`E:\HDT-BobCoach-evidence\dll-archive\20260812T015157371Z-e8ea5fa4`
- 构建产物：`E:\HDT-BobCoach-evidence\season14\build\20260812-t30-global-replay\BobCoach.dll`
- 离线 ZIP：`E:\HDT-BobCoach-evidence\season14\build\20260812-t30-global-replay\package\BobCoach-1.1.0-win-x64.zip`

## T31：事件级回放 v3

Date: 2026-08-12
Status: Deployed

### 本次改动

- `PLEvent` 增加 `PlayerId/Zone/TargetCardId/TargetPlayerId/BeforeHealth/AfterHealth`，可还原玩家归属、区域迁移、攻击目标与生命结算。
- `PowerLogWatcher` 新增 `PowerLogEventDetected`，增量读取时把解析事件推送给 `GlobalReplayRecorder`，并清空事件对象上的 `RawLine` 降低内存占用。
- `GlobalReplayRecorder` 新增 `powerAction` 与 `combat` 紧凑事件：
  - `powerAction`：Power.log 直接识别的 buy/sell/play/spell/refresh/level/freeze/heroPower/darkGift/triple。
  - `combat`：attack/damage/death，包含实体、卡牌、玩家、伤害、生命、回合和阶段。
- `BobCoachPlugin` 设置 `PowerLogWatcher.LocalControllerId`，本地动作只记录本玩家，战斗事件保留双方身份。
- 回放分析器和 Firestone 对比脚本支持新事件，并让 Power.log 动作证据优先于 state 推断，避免重复计数。

### 验证

- `node tests/test_global_replay_recorder.js` PASS
- `node tests/test_global_replay_analyzer.js` PASS
- `node tests/test_replay_sequence_compare.js` PASS
- `node tests/test_powerlog_choice_batch_behavior.js` PASS
- `npm run test:behavior` PASS（UI 生命周期 237/237）
- Release 构建 PASS：`BobCoach.dll` bytes=936448，SHA256=`6C540A612FC1D2EC8BCD724108DF99D26EEC1F633ED16BBA2F35210EF38A4D4A`
- 已部署三处 HDT 插件目录，并核对 SHA256。
- 部署归档：`E:\HDT-BobCoach-evidence\dll-archive\20260812T045848815Z-replay-event-v3-final2-pre`

### 残余风险

- 真实对局尚未用新 DLL 采集全局 JSONL，需要下一局验证 `powerAction/combat` 与 Firestone XML 的覆盖差异。
- 战斗 `attack` 事件在部分 Power.log 记录里可能只有攻击方、目标为 `0`，需要继续用真实对局样本补齐目标推导规则。
- Firestone XML 适配器仍可继续补充 sell/refresh/freeze/heroPower 等动作，让两套记录直接对齐。

## T32：0 金刷新提示硬门控

Date: 2026-08-12
Status: Fixed, build pending deploy

### 回放证据

- 双插件同局记录：`global_20260812_151639_BG30_HERO_304_792926b3.jsonl`
- 全局限中 `state` 记录显示多次 `gold=0, liveGold=0, freeRefreshCount=0` 且酒馆仍有随从。
- `recommend` 未在 `gold=0/free=0` 状态下把 `Refresh` 选为主推荐，说明问题主要在刷新 UI 光晕的 `badShop` 兜底路径，而不是决策引擎候选缺失。

### 根因与修复

- `showRefresh` 原先只信任 `liveGold`，缺少对当前捕获状态 `state.Gold` 的二次硬校验。
- 当 Dispatcher 回调携带旧 plan 状态时，`badShop` 路径仍可能把“刷”光晕画到 0 金币画面上。
- 新增 `renderGold = min(liveGold, state.Gold)` 硬门控，`refreshAffordableByState` 强制要求：
  - `renderGold >= refreshCost`；
  - 无免费刷新时 `renderGold >= 1`；
  - `LiveSafetyGate.SuppressRefresh` 为 true 时彻底禁止刷新光晕。

### 验证

- `tests/test_ui_lifecycle.js` 新增 `[ZeroGold][Refresh] refresh glow must fail closed at 0 gold without free refresh`。
- UI 生命周期 `238/238`。
- `npm run test:behavior` PASS。
- Release 构建 PASS：`BobCoach.dll` bytes=`936960`，SHA256=`697F68E48B881B4150603A4F6ED959222CD9271CC421E9448BD48D76874A71D6`。
- HDT 当前仍运行，待手动关闭后部署到三处插件目录。

## T33：环境基线与主线计划回放

Date: 2026-08-12
Status: Deployed

### 本轮改动

- 新增 `SeasonMetaBaseline`，让 `CompGuideCatalog` 读取当前环境基线，覆盖流派 T 级和核心 CardId。
- 新增 `season14_meta_baseline.json`，使用官方 36.2/36.2.1 规则和 2026-08-11 社区 meta 快照。
- 野兽龙虾流核心列表补上 `BG36_201` 深潜狮子鱼。
- 黑暗之赐注册表同步 36.2.1：Charisma 6-9、Steady Growth max 5、Battle Scars / Death's Embrace / Spell Siphon max 10。
- `DarkGiftDecisionService` 增加前期持有和最后一次机会规则。
- `GlobalReplayRecorder.recommend` 新增 `mainlinePlan`，离线分析可直接看到主线、T 级、缺卡和节点优先级。

### 验证

- `test_season14_registries.ps1` PASS，断言 375 项。
- `test_phase2_decision_services.ps1` PASS。
- `test_global_replay_recorder.js` PASS。
- `test_release_build_contract.js` PASS，compile=238。
- `test_ui_lifecycle.js` PASS，243 项。
- Release `BobCoach.dll` bytes=`954880`，SHA256=`5AED8FF44AF957CBA82B1F99FCBFF8A9F01A3731FE6F0EA3148442F2AE3F904F`，已部署五个插件目录。

### 残余风险

- 社区 meta 快照来自单一公开来源，后续需要 Firestone/更多统计源交叉验证。
- `mainlinePlan` 需要真实对局验证 JSONL 内容和动作链是否一致。
- 环境基线目前只影响主线识别和核心节点，尚未完全驱动升本/出售/黑暗之赐的长期资源链。

# 用户反馈问题清单

Date: 2026-08-12
Status: Active

这份清单用于统一登记用户反馈、首次反馈时间、触发 DLL 版本、修复 DLL 版本、复验证据和回归防线。以前问题散落在设计、计划和交接文档中，导致部署后容易遗漏或回退；从本文件开始，所有反馈先落档，再修复、再部署、再关闭。

当前部署版本：

- `BobCoach.dll` bytes=`1028608`
- SHA256=`2D41E2743EAEE4CB6AE8A00D281418D29788D69A56259965AFF5DE05C14D8A1B`
- 部署时间：2026-08-13 UTC `10:18:35`
- 已部署并核对：`%APPDATA%\HearthstoneDeckTracker\Plugins` + `%LOCALAPPDATA%\HearthstoneDeckTracker\app-1.54.0\Plugins` + `app-1.54.1\Plugins` + `D:\software\game\HDT-V2.7.0\HDT\Plugins` + `D:\software\game\HDT团子版V2.5.9\HDT\Plugins`
- 部署归档：`E:\HDT-BobCoach-evidence\dll-archive\20260813T101835338Z-e8ea5fa4`
- 团子版 pre-anomaly-gate 归档：`E:\HDT-BobCoach-evidence\dll-archive\20260813T101859215Z-tuannzi-pre-anomaly-gate`

## 状态定义

- `Open`：已确认问题，未修复。
- `Partial`：已有部分修复或防回归测试，但真实对局仍需复验。
- `Deployed`：已构建并部署，等待下一局实战复验。
- `Closed`：已有自动化测试 + 真实对局/回放证据，暂未复现。

## 清单

| ID | 首次反馈时间 | 触发版本线索 | 修复/当前版本线索 | 状态 | 问题 | 回归防线/证据 |
|---|---|---|---|---|---|---|
| | ALG-066 | 2026-08-22 | 插件侧饰品理解无血量/协同(目标⑤) | R214+R217+R224 已实施并部署 | Deployed | 插件TrinketRuleEvaluator仅种族评分, 无血量情境/板面关键词协同(模拟器R204/R205已实现) | R214: 血量情境+板面关键词协同+CardText回填+引擎协同; R217: 跨回合价值; R224: 每个回合正则修复; 4harness PASS; 3目录部署; 实战复验待用户对局 |
| UI-001 | 2026-08-10 起多次 | 已定位：HDT 实际加载 `%LOCALAPPDATA%\HearthstoneDeckTracker\app-*\Plugins` 旧 DLL `67D48F...` | `B6B323AA6D3FF28A...` | Deployed | 实战中插件 UI 多次不显示或消失，根因之一是 HDT 实际加载目录未同步 | `2026-08-10-season14-live-decision-implementation-log.md`、本地 app-1.54.0/1.54.1 已部署并校验；仍需实战确认 |
| UI-002 | 2026-08-10 起多次 | 待追溯；时空酒馆相关反馈在 08-12 前 | 当前 `697F...` | Partial | 时空酒馆提示被游戏图层遮挡、位置不对齐；饰品/发现面板重叠 | `test_layout_resolution_matrix.js`、ZIndex/面板位置测试 |
| UI-003 | 2026-08-10 起多次 | 待追溯 | 当前 `697F...` | Partial | 文字指导面板与实际 UI 动作不一致、重复“打”同一对象、英文动作 | `DecisionVisualizer` 动作链测试；需回放逐条对齐 |
| UI-004 | 2026-08-12 | `6C540A612FC1D2EC...` | `697F68E48B881B41...` | Deployed | 0 铸币仍显示刷新；购买/刷新/升本/技能提示互相冲突 | `[ZeroGold][Refresh]` 回归测试；待实战复验 |
| UI-005 | 2026-08-10 | 待追溯；`bb84f71e`/`52ef64a4` 部署期 | 当前 `697F...` | Partial | 黑暗发现 UI 显示内部参数、0 金/战斗阶段常驻、无侧重推荐、无高亮 | `test_ui_lifecycle.js` 黑暗之赐测试；需实战复验 |
| ALG-001 | 2026-08-10 | 待追溯 | 当前 `697F...` | Partial | 不考虑自身战力、血量、对手和剩余铸币强行升本 | `UpgradeRisk`、`LiveSafetyGate`、MCTS 测试；需回放差异分析 |
| ALG-002 | 2026-08-10 | 待追溯 | 当前 `697F...` | Partial | 推荐呆板，不理解“发动/Spellcraft/附加资源/种族协同/南海卖艺者等跨回合价值” | `CardEffectRuleEvaluator`、`StatGiftRuleEvaluator`、语义评分测试 |
| ALG-003 | 2026-08-10 起多次 | 待追溯；08-12 最新反馈在 `6C540A...` 部署期 | 当前 `697F...` | Partial | 空手没替换也推荐出售；刚买随从就推荐卖；后期卖火车王等关键怪 | `SellGold`、`shouldSell`、出售后目标可支付测试 |
| ALG-004 | 2026-08-10 起多次 | 最近一次触发 `6C540A612FC1D2EC...` | `697F68E48B881B41...` | Deployed | 动态铸币数没有正确进入购买、刷新、升本、出售、技能门控 | `GoldTracker` 22 项测试、`ZeroGold` 刷新测试；待实战复验 |
| ALG-005 | 2026-08-10 起多次 | 待追溯 | 当前 `697F...` | Partial | 法术/技能触发发现无面板；非发现机制误捕获招募面板；发现侧重不合理 | `PowerLogChoiceBatchHarness`、`CombatChoiceRenderPolicy` |
| ALG-006 | 2026-08-10 起多次 | 待追溯 | 当前 `697F...` | Partial | 主动/被动技能未统一管理；被动也提示；主动技能时机、费用、性价比不准 | `HeroPowerRuleCatalog`、`HeroSkillCatalog`、`HeroSkillDecisionService` |
| ALG-007 | 2026-08-10 | 待追溯 | 当前 `697F...` | Open | 新赛季卡牌、饰品、黑暗之赐中文映射不全，UI 仍可能显示裸 CardId | `DisplayNameResolver` 测试；需完整版本化数据源 |
| ALG-008 | 2026-08-12 | 待追溯；酒杯/卡槽反馈在 08-12 回放期 | 当前 `697F...` | Partial | 酒杯刷新错位、冰冷静滞少槽位、饲料吞食补位、固定等级卡槽导致 UI 错位 | `ShopLayoutSlotPolicy`、`ReplenishingShop` 测试 |
| ALG-009 | 2026-08-12 | 待追溯 | 当前 `697F...` | Partial | 免费刷新未保存、免费刷新/冻结策略、第一回合拿牌第二回合使用等跨回合策略不足 | `TurnProjector`、`FreeRefresh`、`Chromie` 测试 |
| ALG-010 | 2026-08-12 | `697F68E48B881B41...` | `B6B323AA6D3FF28A...` | Deployed | 回合3等三连时文字指导提示卖牌/升本但出售UI未出现；T3“卖弱拿三连”被 `Turn < 4` 门控误杀；状态条在裁剪前渲染导致文字与UI不一致 | `[SellGold][EarlyTriple]`、`[SellUI][StatusConsistency]`；回放 `global_20260812_162446_BG24_HERO_100_7eec7ee6.jsonl`；待实战复验 |
| ALG-011 | 2026-08-12 | `B6B323AA6D3FF28A...` | `CC3E9FB628D9FC3C...` | Deployed | 算法缺少主线思考：没有围绕主要流派、核心组件缺口和每回合资源分配做长期判断 | `MainlineStrategyPlanner` 已接入买/升/卖/刷/冻评分；`R2/R-time/R-triple` 可被核心节点覆盖；待实战复验 |
| ALG-012 | 2026-08-12 | 不适用 | `5AED8FF44AF957CB...` | Deployed | 环境基线未数据化：主线策略仍依赖旧攻略 T 级，野兽龙虾流缺少 `BG36_201`，黑暗之赐 36.2.1 热修未同步 | `SeasonMetaBaseline`、`season14_meta_baseline.json`、dark gift hotfix 测试；待真实对局复验 |
| ALG-013 | 2026-08-12 | 不适用 | `561523B0972E45B2...` | Deployed | 算法缺少动态路径规划：没有阶段、转型、Hedge/Pivot、对手反制、TradeUp 的统一中间表示 | `MainlineStrategyPlanner` 动态字段、`[Dynamic]` 回归测试、回放 `mainlinePlan` 动态字段；待真实对局复验 |
| ALG-014 | 2026-08-12 | `32784E1FD3BC06C5...` | `561523B0972E45B2...` | Deployed | 实战暴露：T1 夺金健将评分低于逃窜的纳迦；出售后购买UI滞后；建议卖5星纳迦资源牌；早期误报Pivot/反制；星元自动机过渡战力不足 | 始终金色卡加分、动态无方向不Pivot、前期反制关闭、核心/资源卡出售保护、Power.log动作即时请求UI刷新、wherever-scaling过渡节奏加分；已部署待实战复验 |
| ALG-015 | 2026-08-12 | `561523B0972E45B2...` | `5A52E32E814120FB...` | Deployed | 最新回放 `global_20260812_195219_...` 暴露：T1-T6 无替代核心证据仍持续 Pivot/Hedge；ShouldHoldForNextTurn 被 Pivot 覆盖；0金时“卖后买”候选仍可能放行 | 替代流派必须 `HasGuideEvidence` 才进入 Hedge/Pivot；Commit/Economy 优先于 Pivot/Hedge；购买过滤要求 `IsShopScoreWorthAfterSell`；`[Dynamic]` 与 `[SellGold]` 回归测试；待实战复验 |
| ALG-016 | 2026-08-12 | `5A52E32E814120FB...` | `2C45B9FE8881DF18...` | Deployed | 两局新回放仍暴露：当前主线的共享核心被误当成替代证据；替代分差为负仍靠观察记忆触发 Hedge；`DecidePriority` 在模式判定后执行，导致 Economy 被 Pivot 覆盖 | `GetAlternativeCoreIds` 排除当前主线核心；负分差不触发记忆 Hedge；资源优先判定移到路径评分前；已部署 `2C45...`，待实战复验 |
| ALG-017 | 2026-08-12 | `2C45B9FE8881DF18...` | `0365AFC33E6604A9...` | Deployed | 最新一局 `global_20260812_215254_...` 的 T6-T8 仍因单张泛用成长卡（机鳍鱼人、星元自动机等）进入 Pivot，单卡无法证明替代流派方向 | 过滤通用引擎卡 `IsGenericPathEvidenceCard`；替代证据必须至少 2 个不同非泛用核心或三连；已部署 `0365AFC...`，待实战复验 |
| ALG-018 | 2026-08-13 | `0365AFC33E6604A9...` | `F02AEFF01265BC2E69...` | Deployed | 阿凯局后期主线已明确为野兽，仍推荐买遗骸看管者等无流派关联卡；可见推荐没有充分按主线方向收敛 | `MainlineStrategyPlanner` 增加后期主线偏离惩罚；已部署 `F02A...`，待实战复验 |
| ALG-019 | 2026-08-13 | `0365AFC33E6604A9...` | `F02AEFF01265BC2E69...` | Deployed | 芬利选择“好运当投”后，插件仍把原“冒险出发”识别为被动主技能，不提示使用；后续金币分配和南海卖艺者打法被错误提前持有策略影响 | 增加芬利种子技能降权和替代技能优选；幸运骰下允许 T1 打出南海卖艺者；已部署 `F02A...`，待实战复验 |
| ALG-020 | 2026-08-13 | `0365AFC33E6604A9...` | `F02AEFF01265BC2E69...` | Deployed | 芬利局有板面目标时，赢家的面包未被作为前期法术动作优先推荐，持续战力滚雪球收益未被建模 | `BG36_883` 增加前中期购买优先级；已部署 `F02A...`，待实战复验 |
| ALG-021 | 2026-08-13 | `0365AFC33E6604A9...` | `F02AEFF01265BC2E69...` | Deployed | 芬利绝境局 T14 建议出售火车王；对手有多张中等大身材随从时，机制交换卡价值判断阈值不足 | `OpponentTechAdvisor` 增加多中等威胁保护规则；已部署 `F02A...`，待实战复验 |
| ALG-022 | 2026-08-13 | `0365AFC33E6604A9...` | `F02AEFF01265BC2E69...` | Deployed | 寄生之魂局三连奖励发现候选已从 Power.log 读取，但 UI 未显示提示面板 | 发现渲染增加权威 Power.log 批次直渲染路径；已部署 `F02A...`，待实战复验 |
| ALG-023 | 2026-08-13 | `0365AFC33E6604A9...` | 未开始 | Open | 寄生之魂局黑暗之赐侧重与玩家偏好不一致；用户更看重上限、海盗经济和稳定场面词条 | 需要更完整的暗赐词条事实和可复盘选择偏好，不能只靠高星/当前流派判断；当前回放仅能确认选择对象，无法证明偏好权重 |
| ALG-024 | 2026-08-13 | `0365AFC33E6604A9...` | 未开始 | Open | 小饰品黄金残片需要计算下轮铸币和提前购买规划；场上发动机制和资源分配未进入跨回合路径 | `TurnProjector` 已支持部分下轮铸币卡，但尚未覆盖黄金残片触发模型和发动机制收益；需版本化饰品数据 |
| ALG-025 | 2026-08-13 | 不适用 | 工作树 `codex/season14-algorithm-dark-gift-capture` / `68EACDF4...` | Deployed | 跨回合决策缺少统一有界记忆和动作序列规划；旧建议按单帧独立判断，不能串联搜核心、囤牌、经济、战力与反制 | `StrategicMemory` + `SequenceSearchPlanner` + `StrategySequencePlanner` 已接入 Shadow 路径，回放新增策略/记忆字段；候选 DLL 已部署到本机 HDT，尚未获得真实对局复验 |
| UI-006 | 2026-08-13 | `0365AFC33E6604A9...` | 未修复 | Open | 寄生之魂局回合4操作后购买标签未主动跟随位置刷新，购买 UI 看起来混乱、变化过多 | 需要进一步定位商店标签跟随、planHash 和渲染批次时序；回放 `global_20260813_094944_...` |
| UI-007 | 2026-08-13 | `68EACDF4...` | `DF8D48B5...` | Deployed | 回合1文字指导和招募面板高亮优先级不一致，文字推荐一张牌而面板主推另一张 | `DecisionVisualizer.PromoteBestShopAction`；T33 live feedback contract；待实战复验 |
| ALG-026 | 2026-08-13 | `68EACDF4...` | `DF8D48B5...` | Deployed | 回合2状态写为0金，不提示升本、买牌或技能；新回合金币基准被非招募/滞后战斗相位污染 | `GoldTracker` 保留 unknown/known；HDT combat + 酒馆实体联合证据；回放 `global_20260813_121637_...`；待实战复验 |
| ALG-027 | 2026-08-13 | `68EACDF4...` | `DF8D48B5...` | Deployed | 回合3铸币分配不合理；打出 `BG28_805` 后技能费用为1且金币2，仍未提示使用技能 | 技能决策改用 `state.HeroPowerCost`，目录费用仅 fallback；HeroSkill 日志增加实时费用；待实战复验 |
| ALG-028 | 2026-08-13 | `68EACDF4...` | `DF8D48B5...` | Deployed | `BG28_805` 永久铸币上限只有购买评分权重，没有跨回合状态、模拟和投影事实 | HDT `RESOURCES` 上限进入 `state.MaxGold`；`Simulator.UseSpell` + `TurnProjector` 保留永久上限；待实战复验 |
| ALG-029 | 2026-08-13 | `DF8D48B5...` | `67B155A30D1F459...` | Deployed | 费林“远行计划”开局发现 2/4/6 随从时 Power.log 已有完整候选，但插件没有发现提示 | `OnPowerLogPassiveChoiceActive` 注入权威 discover 批次；T34 live feedback contract；回放 `global_20260813_143438_...`；待实战复验 |
| ALG-030 | 2026-08-13 | `DF8D48B5...` | `67B155A30D1F459...` | Deployed | 回合8已有恶魔核心和可买恶魔牌时，主线仍判为野猪人；满场时仍优先升本，不生成卖弱换同流派路线 | `MainlineStrategyPlanner` 分离核心/普通部落信号；`TryPickSynergyTribeBuy` 增加满场保护式 sell-to-buy；T34 contract；待实战复验 |
| ALG-031 | 2026-08-13 | `DF8D48B5...` | `67B155A30D1F459...` | Deployed | 手牌酒馆法术继承商店 3 费并被当成冻结卡，`BG36_880` 理性癫狂未枚举为 0 费动作，也没有友方恶魔目标文字 | 手牌法术费用改为 0；法术不再因无攻血冻结；`BG36_880` 走安全收尾回退并限定恶魔目标；T34 contract；待实战复验 |
| DATA-001 | 2026-08-10 | 不适用 | 未开始 | Open | 需要卡牌对象库/英雄技能库/版本规则库，统一管理版本、规则、费用、伙伴、中文映射 | `docs/maintainer/CARD_OBJECT_DATABASE.md` 仅设计稿 |
| REPLAY-001 | 2026-08-12 | `88B90A203FE4B93A...` | `6C540A612FC1D2EC...` / `697F68E48B881B41...` | Partial | HDT 插件回放与 Firestone 动作覆盖不一致，无法稳定定位全局动作 | `replay_sequence_compare.js`、最新同局 `a69b4eee...` |
| REPLAY-002 | 2026-08-12 | `88B90A203FE4B93A...` | `6C540A612FC1D2EC...` | Deployed | 需要记录发现内容、战斗攻击顺序、战斗结算方式，且文件不能过大 | `GlobalReplayRecorder` 已记录 `powerAction/combat/choice`；3503 事件/2097 战斗事件 |
| REPLAY-003 | 2026-08-12 | 不适用 | 工具更新，不影响 DLL | Partial | Firestone XML 适配器缺少 sell/refresh/heroPower/战斗事件；误把选牌奖励判为购买 | `firestone_xml_to_snapshot.js` 已新增 Block 解析；动作语义仍需过滤 |
| DEPLOY-001 | 2026-08-10 起持续 | 多个版本 | 当前 `561523B0972E45B2...` | Open | 已反馈问题在后续部署 DLL 后重复出现 | 本清单 + 部署前检查表；后续每轮部署必须逐项核对 |
| TEST-001 | 2026-08-12 | 不适用 | 工作树测试闭环 | Partial | 缺少统一“用户反馈 -> 修复 -> 测试 -> 部署 -> 实战复验”闭环 | 本次新增策略契约和对抗回归已进入行为测试套件；部署与真实对局复验尚未完成，不能关闭 |
| TEST-002 | 2026-08-13 | 不适用 | `2D41E2743EAEE4CB6A...` | Deployed | 单靠人工实测成本高，需要离线批量回放回归环境 | `GlobalReplayState` 可重放 + `run_replay_regression.ps1`；新格式回放尚未生成，待实战录制验证 |
| ALG-032 | 2026-08-13 | `49C362E568B07E4700...` | `BB65DDA49D4B27A65ECC...` | Deployed | 塞纳留斯 Critical 早期经济阶段会在 T2-T5 无条件压过可支付升本、三连和主线核心购买 | `ShouldDeferHeroPlaybookForVisiblePriority` + `CenariusDoesNotOverrideAffordableUpgrade`；已部署待实战 |
| UI-008 | 2026-08-13 | `49C362E568B07E4700...` | `BB65DDA49D4B27A65ECC...` | Deployed | 插件层两条卖后买路径仍允许资源/核心牌按星级替换场牌，绕过引擎和视觉层收紧 | `IsShopScoreWorthAfterSell` / `IsShopMarkerWorthAfterSell` 已统一严格分支；已部署待实战 |
| ALG-033 | 2026-08-21 | R234 部署后 (BA08EE62E7C0314D2A9DE644B9F07A8776C1213EC7FA3203B7DE72D092DF83A7) | R235 (待部署) | Deployed | 牛头人酋长(BG25_HERO_105, 技能=发现一个伙伴, 等级2时解锁) 用户反馈第一回合就推荐使用技能 | 根因: UI 提示层 heroHint/heroUseSuggestion 未检查解锁等级(动作层 ActionEnumerator 已检查); 修复 R235: ①heroHint 未解锁时标注"酒馆等级N解锁" ②heroShouldUse 未解锁强制 false(防 playbook 绕过); 反查: DeriveUnlockTier 仅匹配中文"等级N时解锁", 英文格式需补; 回放 global_20260821_154101_BG25_HERO_105_aa25c3e8.jsonl |

| ALG-034 | 2026-08-21 | R235 部署后 | R236final (R238: 584197896CC72515C62E4E6C4573D01C9456C5215B75A8F5AB282BED43986937) | Deployed | 全量技能审计(116个)发现: cost=0 的描述式被动技能(辛达苟萨/米尔豪斯/欧穆/加里维克斯等)被 PassiveTrigger 误判为 Active → 被动当主动提示使用技能 | PassiveTrigger 增加描述式被动识别(随从消耗/购买随从/出售一个随从/升级酒馆后/获得/铸币/在你等标记); 解锁条件(等级N/第N回合)推导全部正确; 审计脚本 _audit_skills.js; 验证 6/6 通过; 待实战复验 |
| ALG-035 | 2026-08-21 | R238 对抗性审查 | R238 | Deployed | boardRate 实机bug: 无条件覆盖 _lastTurnBoardPower, 一回合多次调用 GetBestAction 后速率豁免恒失效 → 低板面高动能成功局被误伤 | 回合门控更新(仅回合变化时记录); 同修趋势队列污染; 部署 5841...6937 |
| ALG-036 | 2026-08-21 | R238 对抗性审查 | R238 | Deployed | 升级出口覆盖不足: R0落后≥2/R1绝境/R4.5/R8/R9 无板面安全线(8个出口仅5个有) | R4.5最低翻盘线(升5本≥4.0/6本≥6.0); R8/R9套R227; R0落后≥2安全线50%; R1板面≥1.0; 部署 5841...6937 |
| ALG-037 | 2026-08-21 | 巴罗夫R234局(2f47a289) | R238 | Deployed | 发现面板复发: 真实发现完成后同choiceId空来源复发3次(seq53/905/1147, 间隔2.7-13秒), 候选被商店卡污染 | R237同choiceId 10秒冷却; 部署 5841...6937 |
| ALG-040 | 2026-08-21 | 终局反制统计(213局解压 finalComp) | R244 统计口径修正 | Deployed-tooling | 运行器 counters 把 12-seed 累加进每局 → 误报"模拟器反制失真×10-37"; 修正(÷12)后: 圣盾×1.15/亡语×1.08/剧毒×1.49/复生×1.37 与真实基本一致; 风怒×3.12/嘲讽×1.91 仍偏高(模拟器 buff 叠加); 反制横向对比恢复有效 | _counter_stats.js(真实终局机制统计); _run_r240.js 已修复 avgCounters=counters/seedsPer; 真实数据: 前四局反制密度显著高于出局(剧毒0.55vs0.18/圣盾1.95vs1.01/复生1.23vs0.58) |
| ALG-041 | 2026-08-21 | R245 实验(T7提前升5本) | R245 (已回滚) | Deployed-rolled-back | 真实回放 T7升5本52% 但模拟器 R242b 全量否决: T7升5本平均名次5.32 vs 未升4.14(升5本11-12金花光→空板崩盘); R0 期望曲线 T7→5本 回滚维持 T8→5本 | 保留 R243 首升豁免(真实 T2首升64%); StandardLevelTurn 表维持旧值(激进分位 T3升3本20%/T5升4本36% 真实存在) |
| ALG-039 | 2026-08-21 | 升级节奏全量对比(R240/R242/R242b/R243base 四轮拟真) | R243 (已部署 45BE853B) | Deployed | 插件 canUpgrade 要求"升完留≥1金" → T2 有4金升2本4费=0金 → 恒 false, T2 结构性无法首升(真实玩家 T2 首升率 64%, 回放统计) → 全链路升级滞后 3-4 回合(模拟器全量量化: 升2本 Bob T5 vs 真实 T2; 升5本 Bob T11 vs 真实 T7) → 后期战力天花板锁死(真实 T16 bp 26.5 vs Bob 13.4) | R243 首升豁免(TavernTier==1 时豁免留金要求); 数据链: R241 中血保护负收益已回滚(ALG-038), R242 单边拉节奏崩盘(对手不同步), R243base 修复对手静态cost bug 后基线 3.565, R242b 验证节奏对齐后后期战力 +6.5(T16); 模拟器验证至此, 插件侧以真实回放数据为准 |
| ALG-042 | 2026-08-21 | 辛达苟萨 R247 部署后实测 | R247 (已部署 573204C7) | Deployed | 辛达苟萨(TB_BaconShop_HERO_27)第一回合 UI 错位——商店随从少1个但UI按完整槽位布局 | 根因: GetShopSlotCountForTier 的 Math.Max(4, slots-1) 在T1钳位到4槽, 但真实机制是基础4槽-1=3槽 → UI按4槽布局、商店仅3随从→错位; R247: 钳位下限改3; 编译通过; 实测对局确认(20:30 H27_SKIN_D rank3, BgsLastGames.xml); 待部署复验 |
| ALG-043 | 2026-08-21 | 厨师曲奇 R247 部署后实测 | R250 (已部署 573204C7) | Deployed | 曲奇(BG21_HERO_020)主动技能"搅动汤锅"只提示使用技能, 文字指导没有指向类型随从(技能=投入随从集齐3个同类型发现一个, 应指明当前投哪个种族) | R250: heroUseSuggestion 追加种族定向(板面+商店多数族, PickCookieTargetTribe); 训练数据确认(16局, 元素9/海盗7/巨龙7偏好); 编译+64断言PASS; 待部署复验 |
| ALG-044 | 2026-08-21 | 劫匪之王托瓦格尔 R247 部署后实测 | R251 (API+测试就绪, 待回放接线) | In-progress | 劫匪之王(BG23_HERO_305)回合2: 已按提示升本, 动态铸币数为0, 购买建议UI仍弹出(金币0还弹购买=铸币跟踪与UI联动错误); 回合3: 买法术拼命挖掘扣血得币后动态铸币为6, 可买2随从或花6升级3本均无提示, 动态计算策略失灵 | ①gold=0清secondStep/bestAction防护已存在(2183/2192行)但用户仍见弹购买→疑似LiveGold时序(升本瞬间未更新) ②拼命挖掘(扣血得币法术)GoldTracker无加币处理(注释"操作扣除覆盖"不适用于法术给币)——需回放证据精确定位; 已确认中途退出HDT导致BgsLastGames未落盘; 回放SOP: Firestone通道仅覆盖历史ID, 新对局需HDT侧; 待代码增强+复验 |
| ALG-045 | 2026-08-21 | 劫匪之王托瓦格尔 R247 部署后实测 | R248 (已部署 573204C7) | Deployed | 劫匪之王回合5: 主动技能费用减至7铸币, 不提示使用——英雄专用玩法(每回合技能费用-1递减)没有学习, 动态策略失灵 | 根因: playbook FindMatchingPhase 用静态 GoldAtLeast=PowerCost=11(注册表 mana_cost=11), T5金币上限8-9 → 永不匹配; R248: FindMatchingPhase 改运行时 cost(递减值)作下限; 测试+3断言(10总); 英雄专项训练确认覆盖(29局, levelAgg 0.87/upPerGame 4.76/中位名次2); 待部署复验 |
| ALG-046 | 2026-08-21 | R253 费用递减激励 | R253 (已部署 3CAD7606) | Deployed | 劫匪之王技能费用递减但无行为激励——费用越低越值得用(真实玩家抓折扣窗口) | R253: COST_DECAY 标记(文本铸币减少/英雄技能减少) + Decide 中 cost<PowerCost 时 bias 提升(节省比例x0.5封顶0.35); 测试+2断言(12总); 编译通过 |
| ALG-047 | 2026-08-21 | 模拟器法术槽缺失 | R261 (已修) | Deployed-tooling | 模拟器商店无法术槽(T1=0法术位), 真实每回合1法术位(用户实测+回放00257a71) | R257发现→R260改spellShop→R261修正slotsByTier T1=0→1; 机制对齐; 全量名次中性(3.418→3.508)但机制正确性提升 |
| ALG-048 | 2026-08-21 | 卡牌机制标记缺失 | R263 (已修, 验证中) | In-progress | 98张卡文本含机制(嘲讽26/复生22/风怒10/圣盾18等)但mechanics字段缺失→模拟器战斗结算错误 | R263: 文本关键词补齐mechanics; 数据修正无风险; 全量验证中 |
| ALG-061 | 2026-08-22 | 核心卡低估(8张) | R316+R317 (已修) | Deployed-sim | 巴琳达/瑞文/陆行鸟等成型阵容核心权重低于真实出现率 | 按649个6本阵容校准权重+协同; 测试终局战力翻倍, 全量中性 |
| ALG-060 | 2026-08-22 | 铜须等战吼流核心低估 | R313+R314 (已修) | Deployed-sim | 铜须真实443局前四率98%但权重3.8, Bob购买率4.6%; 战吼卡占购买10.5% vs 真实23.7% | 铜须权重6.0+S级+协同; 板面有战吼引擎时商店战吼卡+3; 60局A/B名次4.50→4.05 吃鸡+5pp |
| ALG-059 | 2026-08-22 | 战棋热修(灯神禁用/土狼回归) | 已对齐 (R311) | Deployed-sim | 36.2.2平衡后热修: 温和的灯神BGS_121回归又被禁用, 囤食土狼BG36_210回归 | BGS_121 disabled=true+权重移除; BG36_210 disabled=false; 池验证通过 |
| ALG-058 | 2026-08-22 | 暗赐机制偏差(上限/词条数值/回合窗口/多数类型) | R308+R309 (已修) | Deployed-sim | ①maxPerGame=3未落R165预期(实际2.33次/局vs真实1.4) ②词条数值错误(坚韧刚毅+5应为+4等4处) ③回合窗口偏差8处 ④T6起多数类型规则缺失 | ①上限3→2(暗赐1.30-1.75次/局对齐真实) ②词条数值/窗口按官方开发者日志修正 ③补多数类型规则; 13项单测通过 |
| ALG-057 | 2026-08-22 | 反制卡数据过时(11映射旧赛季卡) | R303 (已修) | Deployed-sim | counter_tags引用旧池卡, 当前仅2张在池; 反制决策0次/240局 | 补充10张当前池机制卡(烈毒→anti_big_stats); 效果受商店出现率限制 |
| ALG-056 | 2026-08-22 | 克罗米技能误用通用刷新 | R301 (已修) | Deployed-sim | 刷新应变为酒馆法术, 原走通用随从刷新 | 专属chromie_spell_refresh; 60局A/B吃鸡6.7%→10% 血量+0.8 |
| ALG-055 | 2026-08-22 | 海盗经济优先级bug+托瓦格尔费用静态 | R297修/R298否决 | Deployed-sim | pirate_buy_gold买任何卡+1金(应限海盗); 托瓦格尔mana_cost=11使决策层从不建议技能 | R297加括号修复(全量历史最佳3.205/吃鸡24.7%); R298费用修复A/B负收益回滚 |
| ALG-054 | 2026-08-22 | Bob 从不买时空牌(机制缺口) | R294 (已修) | Deployed-sim | timewarp_buy 决策只有对手HeuristicAI生成, Bob模块链缺失→0次vs对手2852次/40局 | 引擎注入Bob决策; 60局A/B名次4.00→3.73 吃鸡16.7%→21.7% |
| ALG-053 | 2026-08-22 | 三连率鸿沟(0.13 vs 真实11.66/局) | 已定位未解决 | In-progress | Bob三连0.13/局 vs 真实11.66, 刷新1.22 vs 5.35, 买0.95 vs 4+ | 购买/刷新/权重三类提升均验证无效(R197/R274/R275/R293); 需购买-卖出循环整体执行改进 |
| ALG-052 | 2026-08-22 | 钩牙技能乱用换低卡 | R286 (已修) | Deployed-sim | 高本后把强卡换低一级卡=净亏, 技能13-14.5/局vs真实10 | canUse加最低评分>=6拒绝; 60局A/B名次4.45→3.75 吃鸡8.3%→26.7% |
| ALG-051 | 2026-08-22 | 托奇技能无脑高频烧钱 | R285 (已修) | Deployed-sim | highFreq分支每回合用1费刷新, 升级仅1.7次/局终局2-3本; 真实T10+ 42次 vs T1-9 13次 | 前期(<3本且<T7)门控, 60局A/B名次6.92→4.55(-2.37) 前四16.7%→46.7% |
| ALG-050 | 2026-08-22 | 欧穆机制重复实现 | R283 (已修) | Deployed-sim | SimulationEngine 旧硬编码欧穆块(2026-08-15)与新 PlayerAgent levelUp 钩子叠加 → 双倍+4金 | 删除引擎旧块, 统一 heroModel.kind=omu_gold 钩子; 单测6项过; 60局A/B名次4.05→3.87 前四51.7%→61.7% |
| ALG-049 | 2026-08-21 | R262 路线记忆(否决) | R262 (已回滚) | Deployed-rolled-back | 路线记忆同族x1.3加权30局A/B变差(名次5.40): 与R66变阵回滚一致, 模拟器锁流无收益 | 已回滚; 路线记忆需板面核心+对手配合的精细触发, 暂缓 |
| ALG-038 | 2026-08-21 | R241 实验(中血保护) | R241 (已回滚) | Deployed-rolled-back | R241 中血保护全量A/B为负收益: 名次3.333→3.370, 吃鸡23.7%→23.0%, 血量4.60→4.41; 中血时升本后置拖慢滚雪球节奏, 中后期战力不足血量反而更低; 所有子群无一受益 | 结论: 保守保血方向错误(反驳"血量主因→主动保血"假设); 已干净回滚模拟器(LevelingModule.js)与插件(DecisionEngine.cs); 下一步应强化领先时滚雪球而非保血; 实验数据 reconstruct-r241/r241_report.json |
## 已登记的真实对局证据

- `%APPDATA%\bob-coach\replays\global_20260812_151639_BG30_HERO_304_792926b3.jsonl`
  - 13 回合、第 4 名、英雄 `BG30_HERO_304`。
  - 3503 个事件：`powerAction=261`、`combat=2097`、`choice/choiceCompleted=35`。
- Firestone review：`a69b4eee-6f35-430d-95ba-1f77d791340e`
  - 13 回合、第 4 名、英雄 `BG30_HERO_304`。
  - XML Block 数 4738，当前适配器动作 121、选择 36。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260812_162446_BG24_HERO_100_7eec7ee6.jsonl`
  - 实战英雄 `BG24_HERO_100`，用于定位回合3卖牌UI缺失根因。
  - 第3回合回放显示：算法 `act=SellMinion`、状态栏可产生“卖”文字，但调度层因 `Turn < 4` 清空出售标记；本次 `B6B3...` 已修复该链路。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260812_195219_TB_BaconShop_HERO_28_7c00a577.jsonl`
  - 英雄 `TB_BaconShop_HERO_28`（永恒者托奇），13 回合、第 3 名、`boardPowerPeak=21.47`。
  - 2792 事件：`state=206`、`recommend=196`、`powerAction=183`、`combat=1685`。
  - 定位到动态路径层早期误报 `Pivot/Hedge`，并确认 `ShouldHoldForNextTurn` 被 Pivot 覆盖；本次 `5A52E32E...` 已加证据门控与优先级修复。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260812_204944_BG36_HERO_101_36cbd91f.jsonl`
  - 英雄 `BG36_HERO_101`（特莱斯塔斯，寄生之魂），7 回合结束。
  - 851 事件：`state=53`、`recommend=53`、`powerAction=30`、`combat=485`。
  - 0 金时 `ZeroGoldClear` 正常清 UI，未发现买不起的推荐；但 T5 起仍出现 `Pivot`，根因是共享核心被误当替代证据。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260812_210204_TB_BaconShop_HERO_76_0553a0ac.jsonl`
  - 英雄 `TB_BaconShop_HERO_76`（奥拉基尔），9 回合结束。
  - 1616 事件：`state=128`、`recommend=125`、`powerAction=108`、`combat=903`。
  - 零金购买/刷新未复现，但替代分差为负时仍因观察记忆进入 Hedge，T8-T9 继续大量 Pivot。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260812_215254_BG36_HERO_101_4894f643.jsonl`
  - 英雄 `BG36_HERO_101`（特莱斯塔斯，寄生之魂），12 回合结束。
  - 2358 事件：`state=231`、`recommend=219`、`powerAction=186`、`combat=1237`。
  - 硬门控未复现买不起/空手打牌/0金刷新；但 T6-T8 Pivot 主要来自单张 `BG36_764` 机鳍鱼人，已通过泛用核心过滤修复。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260813_121637_BG31_HERO_003_9c2fdae2.jsonl`
  - 英雄 `BG31_HERO_003`（预言者努波顿），用于定位 T33 实测问题。
  - 4247 个事件，覆盖回合1至后续对局。
  - 关键事实：回合1文字购买 `BG36_345` 与面板高亮 `BG33_140` 不一致；回合2状态为0金但实际已招募/升本；回合3打出 `BG28_805` 后 legacy 生成 `UseHeroPower`，静态费用门控却压制提示。
  - 对应 Power.log：`D:\software\game\Hearthstone\Logs\Hearthstone_2026_08_13_12_13_10\Power.log`。
- `E:\claude_project_hdt\local-data\bob-coach\user-data\replays\global_20260813_143438_BG22_HERO_201_3d476ea3.jsonl`
  - 英雄 `BG22_HERO_201`（费林大使），用于定位 T34 费林发现、恶魔主线和理性癫狂反馈。
  - 2737 个事件，包含回合8板面恶魔核心与商店恶魔关联、回合10后手牌 `BG36_880` 快照。
  - 对应 Power.log：`D:\software\game\Hearthstone\Logs\Hearthstone_2026_08_13_14_32_59`。

## 已知 DLL 版本表

下表按备份时间记录“该备份中旧 DLL 的 SHA256”，用于反推某个反馈发生时正在使用的版本。完整 SHA256 可从 `E:\HDT-BobCoach-evidence\dll-archive` 和插件目录备份核对。

| UTC 备份时间 | 旧版本 SHA256（前 16 位） | bytes | 说明 |
|---|---|---|---|
| 20260812T044742955Z | `FFD6DF7686A17C60` | 928256 | 全局回放 v2 部署前的旧版 |
| 20260812T045702738Z | `88B90A203FE4B93A` | 930304 | 事件级回放 v3 部署前的旧版 |
| 20260812T045805893Z | `BAC06679933CC81F` | 935936 | 回放 v3 final 部署前的旧版 |
| 20260812T045848815Z | `948890675AA8F471` | 935936 | 回放 v3 final2 部署前的旧版 |
| 20260812T075649568Z | `6C540A612FC1D2EC` | 936448 | 0 金刷新修复部署前的旧版 |
| 20260812T170145142Z | `697F68E48B881B41` | 936960 | 回合3卖牌UI修复部署前的旧版 |
| 20260812T170454120Z | `67D48F95F73FE09A` | 925696 | HDT 本地 app 目录残留旧版，UI-001 部署前归档 |
| 20260812T172836085Z | `B6B323AA6D3FF28A` | 939008 | 主线策略层部署前的旧版 |
| 20260812T175451094Z | `CC3E9FB628D9FC3C` | 947200 | 环境基线部署前的旧版 |
| 20260812T183838730Z | `5AED8FF44AF957CB` | 954880 | 动态路径规划部署前的旧版 |
| 20260812T194117922Z | `32784E1FD3BC06C5` | 965120 | 实机反馈修复部署前的旧版 |
| 20260812T194921861Z | `9317A1756162B792` | 965632 | 过渡节奏最终版部署前的旧版 |
| 20260812T123519114Z | `561523B0972E45B2` | 965632 | 动态证据门控部署前的旧版（AppData 归档） |
| 20260812T123700000Z | `561523B0972E45B2` | 965632 | 动态证据门控部署前的旧版（团子版归档） |
| 20260812T123834646Z | `3845FEBC58CD3E50` | 968192 | 动态证据门控最终清理部署前的旧版（AppData 归档） |
| 20260812T123900000Z | `3845FEBC58CD3E50` | 968192 | 动态证据门控最终清理部署前的旧版（团子版归档） |
| 20260812T132438776Z | `5A52E32E814120FB` | 967680 | 共享核心证据修复部署前的旧版（AppData 归档） |
| 20260812T132500000Z | `5A52E32E814120FB` | 967680 | 共享核心证据修复部署前的旧版（团子版归档） |
| 20260812T142533642Z | `2C45B9FE8881DF18` | 968192 | 泛用核心过滤修复部署前的旧版（AppData 归档） |
| 20260812T142600000Z | `2C45B9FE8881DF18` | 968192 | 泛用核心过滤修复部署前的旧版（团子版归档） |
| 20260813T074258417Z | `DF8D48B5168A46EE` | 991744 | T34 部署前的 AppData 当前版归档 |
| 20260813T154345924Z | `DF8D48B5168A46EE` | 991744 | T34 部署前的团子版归档 |
| 20260813T092121590Z | `67B155A30D1F4592` | 997888 | T35 部署前的 AppData 当前版归档 |
| 20260813T092204423Z | `67B155A30D1F4592` | 997888 | T35 部署前的团子版归档 |
| 20260813T093815210Z | `49C362E568B07E4700` | 1010176 | 第二遍对抗审查修复部署前的 AppData 归档 |
| 20260813T093927898Z | `49C362E568B07E4700` | 1010176 | 第二遍对抗审查修复部署前的团子版归档 |
| 20260813T094219016Z | `DCA3F855F4BE9FEA3D` | 1010688 | 最终第二遍修复部署前的 AppData 归档 |
| 20260813T094243015Z | `DCA3F855F4BE9FEA3D` | 1010688 | 最终第二遍修复部署前的团子版归档 |
| 20260813T100842776Z | `BB65DDA49D4B27A65` | 1011200 | 离线回放录制增强部署前的 AppData 归档 |
| 20260813T100906676Z | `BB65DDA49D4B27A65` | 1011200 | 离线回放录制增强部署前的团子版归档 |
| 20260813T101835338Z | `3DD3709BF8AE9C7838` | 1028608 | anomaly-inactive 重放门控部署前的 AppData 归档 |
| 20260813T101859215Z | `3DD3709BF8AE9C7838` | 1028608 | anomaly-inactive 重放门控部署前的团子版归档 |

当前部署版：

- SHA256=`2D41E2743EAEE4CB6AE8A00D281418D29788D69A56259965AFF5DE05C14D8A1B`
- bytes=`1028608`
- 部署归档：`E:\HDT-BobCoach-evidence\dll-archive\20260813T101835338Z-e8ea5fa4`
- 已部署：`%APPDATA%\HearthstoneDeckTracker\Plugins` + `%LOCALAPPDATA%\HearthstoneDeckTracker\app-1.54.0\Plugins` + `app-1.54.1\Plugins` + `D:\software\game\HDT-V2.7.0\HDT\Plugins` + `D:\software\game\HDT团子版V2.5.9\HDT\Plugins`
- 旧版备份：
  - anomaly 门控前 AppData：`E:\HDT-BobCoach-evidence\dll-archive\20260813T101835338Z-e8ea5fa4`
  - anomaly 门控前团子版：`E:\HDT-BobCoach-evidence\dll-archive\20260813T101859215Z-tuannzi-pre-anomaly-gate`
  - T35 前 AppData：`E:\HDT-BobCoach-evidence\dll-archive\20260813T092121590Z-e8ea5fa4`
  - T35 前团子版：`E:\HDT-BobCoach-evidence\dll-archive\20260813T092204423Z-tuannzi-pre-T35`
  - T34 前 AppData：`E:\HDT-BobCoach-evidence\dll-archive\20260813T074258417Z-e8ea5fa4`
  - T34 前团子版：`E:\HDT-BobCoach-evidence\dll-archive\20260813T154345924Z-tuannzi-pre-DF8D48B5`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T170145142Z-early-triple-status-pre`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T170454120Z-early-triple-status-pre-localapp`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T172836085Z-mainline-strategy-pre`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T175451094Z-meta-baseline-pre`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T183838730Z-dynamic-planner-pre`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T194117922Z-replay-feedback-fix-pre`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T194921861Z-tempo-fix-final-pre`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T123519114Z-e8ea5fa4`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T123700000Z-dynamic-evidence-gate-pre-tuannzi`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T123834646Z-e8ea5fa4`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T123900000Z-dynamic-evidence-gate-pre-tuannzi-3845`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T132438776Z-e8ea5fa4`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T132500000Z-shared-core-evidence-fix-pre-tuannzi-5A52`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T142533642Z-e8ea5fa4`
  - `E:\HDT-BobCoach-evidence\dll-archive\20260812T142600000Z-generic-evidence-fix-pre-tuannzi-2C45`

## 问题处理闭环与部署前检查表

> 全局流程(2026-09-06 补充, 与 KB ISSUE_TRACKER.md 顶部规则 + AGENTS.md Release SOP 第 0 节一致):
> 登记 → 定位 → 修复 → **修复后代码审查** → 单测 → **记录更新** → 部署 → 实机复验 → 回填状态。

每次部署新 DLL 前必须执行：

0. **修复后代码审查(2026-09-06 固化, 未审查不得部署)**: 对照改动目标逐条核对
   ①启发式/门控类修复双向误判验证(留回放帧证据, 阿凯 GoldTracker 教训: 只跑测试不算审查)
   ②C# 与 JS/测试镜像同步一致性 ③回归防线(单测/行为测试) ④部署完整性(SHA/5 目录/sidecar)。
   审查证据+判定写入本条状态栏或正文。
1. 检查本清单中 Open/Partial 项是否与本次改动相关。
2. 记录本次 Release 构建的 bytes + SHA256，并归档旧 DLL。
3. 运行 npm run test:behavior，确认 UI/算法/回放相关测试全绿。
4. 若改动涉及 UI、动态铸币、出售、刷新、升本、发现、黑暗之赐、英雄技能，必须打 1 到 3 局真实对局复验。
5. **记录更新(部署后必做)**: 本清单回填状态+修复版本+SHA+归档路径; 同步 ITERATION_LEARNINGS.md /
   HANDOVER / 知识库(适用时) / CHANGELOG(用户可见行为); 禁止部署完就结束、不回填。
6. 复验后更新问题状态，并附回放文件、HDT 日志或触发版本 SHA256 证据。

## 2026-08-13 三局实战复盘

三局完整证据：

- 阿凯：`global_20260813_092717_TB_BaconShop_HERO_16_7fa1d224.jsonl`，11 回合，第 4 名。
- 寄生之魂：`global_20260813_094944_BG36_HERO_101_ab2c8c21.jsonl`，9 回合，第 7 名。
- 芬利：`global_20260813_100506_TB_BaconShop_HERO_40_d9281bcd.jsonl`，15 回合，第 1 名。

动态回放分析器对三局均未发现 `zero-gold-paid-action`、负分差 Pivot/Hedge 或泛用核心污染，因此本轮问题不是硬金币门控回归。

已定位并进入源码修复：

- 芬利主技能选择：`TB_BaconShop_HP_057` 是选择英雄技能的种子，`BG28_HERO_400p` 才是玩家实际获得的主技能；旧逻辑可能继续选择实体 ID 更小的种子卡。
- 幸运骰主技能：`BG28_HERO_400p` 是主动经济技能，但主技能错误导致 UI 提示和 T1 南海卖艺者持有策略都走错路径。
- 火车王出售：T14 对手板面含多张中等大身材随从，现有 `OpponentTechAdvisor` 评分略低于保护阈值，被普通出售门禁放行。
- 三连奖励发现：Power.log 已输出权威候选，但正常提取/状态机渲染路径存在时序竞态，部分三连奖励没有 SHOW。
- 后期主线偏离：可见 legacy 推荐仍主要服从单帧身材价值，主线已明确时对中立或异族普通卡约束不足。
- 赢家的面包：`BG36_883` 是前期有持续滚雪球收益的法术，原法术优先规则未覆盖。

仍需数据/设计工作：

- 黑暗之赐候选侧重需要版本化词条事实和玩家选择复盘，不能仅凭本局主观偏好改权重。
- 黄金残片、场上发动机制、下轮铸币预算需要扩展跨回合状态模型。
- 购买标签位置刷新问题需要继续结合商店布局、实体身份和渲染批次诊断。

## 2026-08-12 对抗审查结论

当前版本 `F02AEFF0...` 仍不能证明“上面所有问题都已修复”。结论如下：

- 已确认修复且有回归测试：`UI-004`（0 金刷新硬门控）、`ALG-010`（回合3卖牌UI链路）、`ALG-011`（主线策略层）、`ALG-012`（环境基线/核心卡/暗赐热修）、`ALG-013`（动态路径规划层）、`ALG-014`（实机反馈修复）、`ALG-015`（替代流派证据门控/卖后买值得过滤）、`ALG-016`（共享核心排除/负分差不 Hedge/资源优先）、`ALG-017`（泛用引擎卡过滤/双核心信号）、`REPLAY-002`（全局事件级回放）。
- 有部分修复但尚未真实复验：`UI-001`、`UI-002`、`UI-003`、`UI-005`、`ALG-001` 至 `ALG-006`、`ALG-008`、`ALG-009`、`REPLAY-001`、`REPLAY-003`。
- 仍未修复或仍未落地：`ALG-007`、`DATA-001`、`DEPLOY-001`、`TEST-001`。
- `npm test` 全绿；UI 生命周期 `254/254`；四个官方/AppData 部署 DLL 和团子版 DLL 均为 `DF8D48B5168A46EE5172002FD4B3A5116C6A848983B2E1E4779D32E18890F086`。
- 自动化测试证明工程正确性，不证明实战策略效果；算法效果仍需按 `SEASON_DATA_CONTRACT.md` 的真实场景验收。
- HDT 正式基线已从 `1.55.0.0` 切换为当前 `1.55.5.0`，`test:contracts`、`test:behavior`、`test:package` 全部通过，不再存在“本机 HDT 自动更新后拿不到旧版基线”的环境限制。

## 2026-08-13 跨回合规划对抗审查

新增工作树基础设施只完成工程接受，未完成效果接受：

- `StrategicMemory` 上限为核心记忆 64 项、对子记忆 32 项，并在新局开始时显式重置。
- `SequenceSearchPlanner` 过滤空动作，处理非有限分数，并使用序列上下文重载让后续动作从累积模拟状态展开。
- `StrategySequencePlanner` 深度 3、束宽 8，跨回合核心/对子证据进入 Shadow 评分；失败被隔离在可见 legacy 路径之外。
- `BobCoachPlugin` 序列化策略目标和动作链前过滤空步骤；分析器保留空值兼容旧回放。
- 本次未构建、未部署新 DLL；三局 2026-08-13 回放由旧 DLL 产生，只能证明新分析器字段兼容，不能作为新规划效果的证据。
- 自动化测试证明确定性、有界性、隐私边界和 Shadow 隔离，不证明胜率、名次或策略质量提升。

### ALG-062: 留牌等三连缺失(R84 修复)——2026-08-22
- **反馈**: 算法不懂留牌在手牌等待三连(铜须/巴琳达/掠夺者等两张在场上只有一张效果, 变金才划算); 阵容要留活动位便于换卡/加buff
- **根因**: ①buyMinion board+hand>=7 合并上限(手牌占板位, 真实: 板面≤7/手牌≤10独立) ②三连检测只查板面, 手牌对子永不三连 ③play_hand 无差别打出手牌对子
- **修复(R84)**: ①板面独立限制 ②三连合并含手牌(仅核心引擎卡) ③核心卡板面紧张时留手 ④手牌对子(≥2张)识别第3张机会
- **验证**: 情景19-20/20局T1三连; 全量双seed: seed42(3.095/78.0%/24.7%) vs R83(3.231/75.1%/21.4%) 全面超越; seed7吃鸡+2.8pp
- **状态**: 已修复并全量确认(历史最佳: 名次3.095/前四78.0%/吃鸡24.7%三项刷新)

### ALG-063: 鱼人种族理解不足(R85 修复)——2026-08-22
- **反馈**: 算法对鱼人种族理解不足, 特别是召唤机制; 手牌资源理解需加强
- **根因**: ①鱼人核心卡评级低估(BG27_556召唤核心C1.7, 3张关键卡无评级) ②无"手牌鱼人=召唤弹药"联动 ③真实对局买BG27_556后100%快卖
- **修复(R85c)**: ①3张机制核心(BG27_556/BG26_350/BG26_137)校准A级 ②板面有召唤核心时鱼人卡权重+3(联动) ③情景注入卡补_buyTurn
- **验证**: 鱼人成型率1.1%→3.3%(3倍), 成型局名次1.33/吃鸡83%; 召唤情景1.45/100%/75%; 中期买核心1→23次
- **状态**: 已修复(全量微降0.095名次, 鱼人非每局主流的场景定向代价, 可接受)

### ALG-064: 自由组合/多路径能力不足(R86修复)——2026-08-22
- **反馈**: 不要总按攻略中一种流派, 要自由组合找最优搭配; 有局内记忆/路线感知
- **根因**: ①Bob终局板面杂牌(均攻101 vs 真实同回合5017, 74倍差距) ②93%局终局无成长引擎 ③R142引擎识别仅2卡 ④currentComp(攻略匹配)门槛高且违背自由组合诉求
- **修复(R86b/c/d)**: ①动态引擎识别 2→12卡(文本扫描: 回合末/使用X/购买召唤X/战吼触发→族buff, 含单字别名"龙"=巨龙) ②引擎卡获取优先(匹配板面族≥2或板面<3才买) ③SellModule引擎卡保护
- **验证**: 引擎购买+62%, 终局引擎局2倍; 有引擎局100%前四/吃鸡50%(vs无引擎79%/24%); 卡雷苟斯情景买龙25次
- **状态**: 已修复(全量名次3.176, 引擎=决定性胜利因子)

### ALG-065: 时空扭曲机制不全(米罗克/姆诺兹多/时空牌效果)——2026-08-22(R87修复)
- **反馈**: 时空扭曲卡牌机制需与真实对局数据比对; 米罗克/姆诺兹多+饰品时空牌
- **根因**: ①引擎timewarpTurns硬编码[6,9]不含米罗克T5/姆诺兹多T8 ②执行层board<7拒绝满板购买 ③时空牌效果0实现(白板) ④SellModule时空牌71%被卖
- **修复(R87)**: ①米罗克T5/姆诺兹多T8 offer生成 ②板面满可进手牌 ③时空牌EOT效果(回合末获取) ④优先级88
- **验证**: 米罗克T5时空购买20/20(真实10/10); 时空牌EOT效果真实触发; 全量名次3.143/前四78.7%历史最佳
- **状态**: 已修复(时空扭曲机制全链对齐真实)


### ALG-066: 一次性/克隆类技能时机不划算(雷诺金化等)——2026-09-08(实测反馈)
- **反馈**: 主动技能使用时机不当、无性价比; 一次性技能(雷诺金化/克隆系)前期低质量随从就交了, 不考虑中后期成长; 关注 雷诺、克隆大师
- **根因假设(待代码核实)**: HeroSkillDecisionService/HeroPlaybookDecisionService 对"限一次/高价值"技能缺少 目标质量x后期潜力 分层(V121: 雷诺 3 局实用于 T6/7/11; V136: 引擎 UseHeroPower 帧 turn 分布 1-17 宽); 金化目标评分可能只看即时身材
- **归属/优先级**: 引擎 HP 决策; 高(直接影响用户体验/资源价值)
- **状态**: Open(证据=replays+baseline 帧可定位具体局)

### ALG-067: 阮大师每回合换技能——选到一次性强技当回合必用缺失——2026-09-08
- **反馈**: 阮大师每回合 2 选 1; 若当回合选项含 雷诺/克隆大师 类强技(且下轮必换掉), 当回合立即使用十分合格; 引擎应识别"到期技能当回合必用"
- **根因假设(待代码核实)**: HeroPlaybook 对阮大师"每回合技能轮换"无"选到即用(use-this-turn)"规则; 需要区分 持续型 vs 一次性到期型
- **状态**: Open; 优先级 高

### ALG-068: 第3回合购买顺序问题(买错浪费铸币/可多买一个)——2026-09-08
- **反馈**: 随从购买顺序有问题; 购买错随从浪费铸币(可多买一个的局没多买); 第 3 回合买随从顺序需多研究, 参考玩家真实拿法
- **根因假设(待代码核实+拿法对照)**: BuyMinion 推荐未最大化当回合可用金(花满/多买一个); target 选择(同费多候选)顺序缺玩家拿法校准(T3 真实买序); 玩家动作=观测(拿法对照作参考, 不作标签)
- **状态**: Open; 优先级 中-高(需先做 T3 玩家拿法统计基准)

### ALG-069: 发动机制严重缺陷(发动效果混淆 + 无对应种族随从仍提示)——2026-09-08
- **反馈**: 发动(战吼/触发)随从各自发动效果不同却被搞混淆; 部分发动需选择友方场上指定种族随从, 场上无对应种族仍提示(极其愚蠢)
- **根因假设(代码定位 round284)**: ①无目标仍提示: ActionEnumerator.cs L268-296(Season14 Activate 发动候选) 仅按 ActivateCost/已发动过滤, 缺"发动要求的友方指定种族随从存在性"校验(板面无该种族仍生成候选) ②发动效果混淆: 待查 Activate 效果注册表/CombatEffects 按 cardId 映射(不同发动是否共用错误效果)
- **修复(round285 v1, 未部署)**: ActionEnumerator.cs L283+ 候选层新增 HasActivateTargetPresent: 发动文本要求"友方<种族>"目标时, 场上无该种族友方随从则不出候选; 方法 L367+ (MinionData.TribeMatches 校验, 保守: 无"友方"字样要求放行)
- **验证(round286)**: Release build 通过 BobCoach.dll bytes=1091584 sha256=5F982A10...(独立目录 _alg069_build, 未部署)
- **②发动效果混淆根因(round286)**: DecisionEngine.cs L1164-1177 Activate 评分走 UnifiedActionScorer 通用软评分(参数0.5/0, 注释缺少效果语义时按统一软评分) -> 引擎不按具体发动效果建模, 不同发动同质估价 -> 与 ALG-002/024(发动机制未建模)同族, 属算法迭代项(发动效果语义建模), 非小修
- **状态**: Partial -> Blocked@test-harness(2026-09-08 08:40): ①修复代码+Release编译就绪(5F982A10); 行为测试宿主缺失 - 仓库唯一 csproj(BobCoach.csproj) 不含 tests/*.cs, run_policy_benchmark.ps1 仅编 PolicyBenchmarkRunner.cs+SecureEvidenceIo.cs, validate_repository.ps1 仅内容扫描 -> tests/*.cs 无编译/运行入口, 需 owner 拍板(提供历史入口或授权重建最小测试宿主); ②发动效果语义建模=候选 D(Shadow)
### ALG-069 ①状态更新(2026-09-08 08:44): Done - 代码修复+编译(5F982A10/build2)+单测 7 用例 ALL_PASS(空板/无友方放行/多部落逗号); 宿主 tools/build/run_behavior_tests.ps1 重建(编译为 PolicyBenchmarkRunner 名匹配 IVT, 自动拷贝 HDT 依赖); 部署待命(HDT 空闲+用户指示)
### ALG-066 证据补充(2026-09-08 09:01): 今日4局(米罗克/ETC/蛇眼/BG31_005)未见金化/克隆实例; 帧缺口=recommend bestAction UseHeroPower 不带 hp 卡 id 与目标随从卡(仅 target 实体号) -> 无法离线判定'低质目标' -> 需 REC-7 记录增强或用户提供具体局/回合
### ALG-067 根因定位(2026-09-08 09:11, 083510 阮大师局 rank1): state.heroPower 未反映每回合 2 选 1 所选技能
- 帧证据: T1-11 heroPower 恒 BG20_HERO_202p(框架), T12 换固定技能(TB_BaconShop_HP_052)后才变 -> 引擎对阮大师当回合实际技能不可见
- 影响: 无法识别当回合持一次性/强技(用户 ALG-067 诉求), HP 建议可能与实际技能错位; 阮大师逐回合技能选择未入 state/choice(记录缺口 V125)
- 修复归属: GameStateExtractor 解析 Powerlog 阮大师技能选择事件 -> 逐回合更新 HeroPowerCardId(摄入修复, owner 许可, 不改可见推荐先行 shadow)
### ALG-067 修复与验证(2026-09-08 09:15): 阮大师框架期误导性 HP 建议已抑制
- 修复: ActionEnumerator.cs 两入口对 BG20_HERO_202p(框架占位技能)跳过 UseHeroPower 候选(ALG-067)
- 验证(引擎重评 083510 局, build 5D61E33B): 框架 202p 建议帧=0; 保留的具体技能建议(HP_103/ETC105p/HP_024)为局内实际获得技能 -> 修复目标达成
- 残余: 引擎对阮大师每回合实际持有技能可见性仍有限(建议技能集 vs 真实回合技能) = REC-8 摄入扩展待 owner; 本修复先行消除框架期错位
### ALG-068 数据补充(2026-09-08 09:17): T3 拿法差距=引擎推即时站场, 玩家优先成长/理财卡
- C 段 55 局(引擎 BuyMinion 建议): 玩家 T3 >=2 买仅 13/55=24%(双买率); 分歧样例多出现玩家买 BG24_715(耐心的侦查员, 出售发现逐回合升级) 与 BGS_115, 引擎推 BG31_818/BG22_202/BG33_430/BG29_300(即时站场)
- 指向: 引擎 Buy 评分缺 T3 窗口"成长/理财/跨回合收益卡"(BG24_715 类)优先级(与 ALG-002/024 跨回合价值同族) + 余金花满(多买)检查
### ALG-068 迭代结论(2026-09-08 09:36): 单 Buy 权重不足, 0 分歧(两轮 1.3/1.8)
- 触发面充足(T<=6 商店成长卡 716 帧; 105 帧含 recommend): 引擎 top1=Freeze 23/UseSpell 23/Buy 17/升级8; 冻结帧多因金<3 Buy 候选不存在; 够钱帧引擎仍偏好 Spell/升级
- 结论: 拿法差异=早期资源分配策略(买发育卡 vs 冻结/法术/升本), 非单帧买哪张; 修复转策略级再平衡(候选级, 需 shadow/模拟验证), 保留 1.8 加权(零副作用)待策略研究确认
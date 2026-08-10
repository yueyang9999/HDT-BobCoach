# BobCoach Season14 官方数据边界与复用分析

Date: 2026-08-10
Status: Maintainer research / for owner review
Scope: Season14 单人模式指导链路；不包含 BGDUO；不宣称胜率、名次、Top4 或策略质量提升。

## 1. 结论

暴雪没有开放酒馆战棋的可执行设计源码或实时对局规则引擎。所谓“官方开放设计代码”，在现实里应理解为三类可靠输入：

1. 暴雪官网的赛季公告、补丁说明和 Developer Insight 规则文本；
2. Battle.net Hearthstone Game Data API 的卡牌、卡组和 metadata 能力；
3. 用户本机由 HDT/HearthDb、Power.log 可观测到的对局事实。

BobCoach 当前提升指导上限最有效的路径，不是接入更大的云端模型，而是把“卡牌事实、英雄技能、赛季机制、实时局面、动作空间、UI 输出”收敛到一套版本化、可解释、可回放的本地决策链路上。这样做同时能显著提高个人开发的赛季更新速度。

## 2. 官方数据边界

### 2.1 没有官方开放代码

Hearthstone 和酒馆战棋的客户端逻辑、服务端逻辑、战斗结算、卡牌规则引擎没有以可执行源码形式开放。社区可见的 `CardDefs.xml`、`cards.json` 等数据，是从客户端文件提取后整理的社区产物，不等同于 Blizzard 官方授权或官方代码。

因此，插件不能依赖“读取官方源码”来解决机制理解问题。更现实的策略是：

- 把暴雪官方规则文本作为最高优先级事实；
- 把客户端数据提取结果作为版本化基线；
- 把本地 Power.log 和 HDT 状态作为运行时证据；
- 无法核对的内容明确标记 `unknown`，不做猜测。

### 2.2 暴雪官方可用的资料

- Season 14 公告：Dark Gifts、Activate、Lockbox、Fishbait、Venomous 改版、新英雄、新饰品数量、发布日期。
- Battlegrounds Developer Insight：Dark Gifts 的设计意图、数值边界和平衡热修。
- Patch Notes：具体卡牌、饰品、技能改动。
- Battle.net Hearthstone Game Data API：卡牌集合、卡牌、卡背、卡组编码、metadata；按公开 OpenAPI 资料，卡牌接口可带 `gameMode` 过滤，如 `constructed` 和 `battlegrounds`。

需要注意：Battle.net Game Data API 需要 OAuth/credential，且提供的是卡牌事实数据，不是酒馆战棋当前对局状态、酒馆刷新概率、内部战斗模拟或玩家招募阶段的 UI 状态。它不适合作为插件的实时状态源，只适合作为离线维护时的卡牌事实对照源。

### 2.3 本机与社区数据源

- HDT Plugin API：插件在 HDT 生命周期内运行，`OnUpdate()` 大约每 100ms 调用；`Core.Game.Entities` 可提供当前对局实体状态。
- HDT/HearthDb：用户本机已有可调用的 `.NET` 卡牌数据库，适合做卡牌基础事实和中文名对照。
- Power.log / log.config：客户端行为日志，可观测招募、购买、刷新、升级、发现、暗赐等事件序列。
- HearthstoneJSON / HearthSim hsdata：按 build 提取的卡牌 JSON/客户端数据，适合做离线 registry 生成。当前 hsdata 版本与 BobCoach 的 `36.2.248348` 证据一致。
- Firestone Developer resources：可参考其开发模式，但其数据/API 有其自身授权要求，不应默认接入 BobCoach 运行时。

### 2.4 数据合规约束

仓库 `DATA_SOURCES.md` 已明确：

- 原始第三方响应、用户日志、回收、账号数据不进 Git/DLL/ZIP；
- 新外部源必须先登记来源、用途、请求边界、缓存保留、许可和失败行为；
- 运行时保持本地离线、只读、fail closed；
- 不允许把未经授权的 HSReplay/Firestone 统计数据用于生产推荐。

## 3. 当前瓶颈

结合 `docs/superpowers/specs/2026-08-10-season14-live-decision-optimization-design.md`，当前主要瓶颈如下：

| 问题 | 根因 | 影响 |
| --- | --- | --- |
| 玩家看到的还是 legacy 推荐 | `DecisionOrchestrator` 固定使用 `Shadow`，统一策略未获准接管 | 新算法无法真正提升玩家可见指导 |
| 升本不看战力和血量 | 早期规则只检查回合、金币、版面 | 高风险升级提示会误导玩家 |
| 买/卖/刷/升/技能没有统一评分 | 大量 early-return 规则，`ActionFeatureExtractor` 的 45 个特征未充分进入生产链路 | 推荐彼此冲突，无法解释取舍 |
| 动态铸币未贯通 | 决策、视觉计划、UI 渲染分别读取 `state.Gold` / `liveGold` | 金币不足仍显示购买、刷新或升级 UI |
| 文字与 UI 不来自同一份计划 | `VisualPlan` 生成后被多处改写 | 面板文字和 UI 高亮不一致 |
| 发现时机错误 | 仍存在 zone6 兜底和文本匹配路径 | 非发现场景可能误捕获酒馆面板 |
| 中文名不完整 | 部分链路使用 `CardName ?? CardId` | 用户看到裸 ID |
| 英雄技能没有统一库 | `HeroPowerEngine` 依赖文本正则和固定 bias | 被动技能误提示、主动技能性价比判断缺失 |
| Activate 未接入决策 | `ActionType` 没有 `Activate`，registry 未消费 | 新赛季发动机制无法指导 |
| Dark Gift 未进入决策服务 | 只有状态门控和文本候选，没有价值评分 | 不知道何时花 3 金币，不知道选哪个 Gift |

## 4. 提高指导上限的路径

### 4.1 先把数据做成可审计对象库

建议实现 `CardObjectDatabase`，让每个 `CardId` 保存：

- 身份：`CardId`、`DbfId`、中英文名、类型、种族、职业、set；
- 版本：`season`、`gameBuild`、`cardPoolIdentity`、`schemaVersion`、`source`、`sha256`；
- 规则：费用、星级、身材、关键词、文本、可购买/可发动/可发现/可暗赐/可三连；
- 关系：所属英雄、英雄技能、伙伴、关联种族、机制互斥；
- 算法用途：作为 `CardNameService`、`ActionEnumerator`、`DarkGiftDecisionService`、`HeroSkillDecisionService` 的公共事实源。

这一步不追求“理解卡牌文本”，而是把“机器可读规则”和“人类可读文本”分离，避免每次赛季更新都改 C# 分支。

### 4.2 建立英雄技能库

为每个英雄维护：

- `heroCardId`、`heroPowerCardId`、`buddyCardId`；
- 中英文技能文本和结构化效果类型；
- `powerType`：Passive / Active / Conditional / Aura；
- 费用、解锁回合/星级、目标规则；
- 推荐使用阶段、目标条件、机会成本、机制搭配。

被动技能不生成“使用技能”提示；主动技能只有费用、解锁、目标和性价比门控都通过时才进入候选。

### 4.3 建立统一动作评分

买、卖、升、刷、冻、英雄技能、发动、发现、饰品、暗赐应进入同一份候选空间，而不是各自 early-return。

推荐结构：

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
    -> HeroSkillDecisionService
    -> DarkGiftDecisionService
    -> RecommendationPlan
  -> OverlayRenderer
```

`RecommendationPlan` 必须成为文字面板、商店高亮、按钮光晕、手牌建议、暗赐候选的唯一事实源。渲染层不再自行猜测动作含义。

### 4.4 动态铸币贯通

`liveGold` 应作为所有费用合法性、评分、UI 可支付过滤、`planHash` 的唯一输入。升级后剩余金币、刷新后剩余金币、购买后可继续动作的空间都要在同一份 `EconomySnapshot` 中计算。

### 4.5 真实局面分析而不是静态规则

升本建议必须至少考虑：

- 当前阵容战力；
- 当前血量/护甲；
- 对手平均或可见战力；
- 预期承伤；
- 剩余金币；
- 替代动作的收益；
- 当前 tavern tier 和可刷出的核心牌；
- 当前回合是否处于存活风险窗口。

同样，出售建议必须通过“出售后可用动作空间 + 替换后战力净提升 + 动态铸币”三重门控；手牌建议必须与当前最弱随从做净收益比较。

### 4.6 暗赐决策规则化

Dark Gifts 的官方规则应进入结构化服务：

- Turn 3 起，3 金币，每回合一次，每局最多 3 次；
- 三个候选随从各带一个唯一 Gift；
- Gift 会随等待时间增强；
- 需要结合阵容种族、机制搭配、当前战力、生存压力、剩余次数和当前铸币做评分；
- 未确认的 Gift、效果映射、真实 UI 保持 `unknown`，不能伪造置信度。

### 4.7 用错误分类而非胜率衡量提升

个人开发阶段最可信的指标是：

- 非法动作是否被过滤；
- 金币不足时是否还显示购买/刷新/升级 UI；
- 被动技能是否误提示；
- 发现面板是否只在权威触发时出现；
- 用户可见卡名是否还有裸 ID；
- 文字与 UI 是否来自同一份 `RecommendationPlan`；
- 暗赐候选是否显示中英文名、费用、评分和原因；
- Shadow 对比是否暴露新的 severe error。

不要在没有至少 300 个真实、版本匹配、人工复核的场景前宣称胜率或名次提升。

## 5. 提高更新节奏

### 5.1 建立“赛季发布管道”

个人开发建议按以下顺序准备：

1. 赛季公告发布后，把官方规则文本保存为原始证据；
2. 补丁上线后，抓取/保存与当前 build 对应的 HearthstoneJSON / hsdata 快照；
3. 运行确定性的 registry 生成器，输出 minion、tavern spell、trinket、activate、dark gift、hero、hero skill、buddy 等注册表；
4. 每个注册表携带 `season`、`gameBuild`、`cardPoolIdentity`、`source`、`capturedAt`、`sha256`；
5. 对新增/删除/改动卡牌做 diff，而不是整表重写；
6. 跑离线审计和合成行为测试；
7. 用真实 Power.log 回放用户反馈场景；
8. 门禁通过后再进入 Release。

这样做的目标是：补丁可用后当天生成可核对注册表，1-2 天内完成基础行为回归；复杂新机制需要额外设计阶段。

### 5.2 减少“硬编码赛季分支”

当前大量机制通过 C# 分支表达，导致每个赛季都要改代码、改测试、改 Release 风险。应把：

- 卡牌存在性和中文名；
- 英雄技能类型、费用、目标和时机；
- 暗赐资格、效果类型、机制交互；
- Activate 候选动作；
- 锁箱、鱼饵、新饰品规则；

迁移到版本化 JSON 注册表。代码只负责执行通用决策框架，具体赛季事实由数据驱动。

### 5.3 使用 build identity 做防串赛季

运行时只允许匹配 `GameBuild / SeasonId / CardPoolIdentity` 的统一策略生效。不匹配时保持 Shadow 或 legacy fallback，防止旧赛季数据污染新赛季判断。

### 5.4 保留 Shadow 和回放能力

新逻辑先与 legacy 同时计算，记录版本化决策事件。真实日志回放只做工程验证，不自动生成效果标签；达到数据门槛后再做离线效果评估。

## 6. 可复用与收益化方向

这套能力的核心资产不是“酒馆战棋推荐”，而是：

### 6.1 版本化卡牌/机制对象库

同一套 `CardObjectDatabase` 可以复用于：

- 英雄、技能、伙伴、饰品、暗赐的统一管理；
- 中文映射和版本审计；
- 测试 fixture 与覆盖率审计；
- 后续算法训练的特征来源；
- 直播/复盘工具中的卡牌信息展示。

### 6.2 本地可解释决策引擎

“实时局面分析 + 候选生成 + 统一评分 + 单一推荐计划”的架构可以迁移到其他卡牌游戏、Roguelike、策略游戏或自定义规则场景。相比纯 LLM，它的优势是：

- 离线、可解释、可回归；
- 不泄露对局数据；
- 费用/规则错误可控；
- 可独立测试。

### 6.3 赛季数据管线服务

把“官方公告 -> 数据快照 -> registry 生成 -> diff 审计 -> 行为测试 -> release”沉淀为可复用 pipeline，可面向个人插件开发者提供服务，包括：

- 卡池和中文名同步工具；
- 新赛季规则 diff；
- 机制注册表模板；
- 插件发布前门禁模板。

### 6.4 内容创作者/教练场景

同一份局面快照和解释可以输出为：

- 局内高亮；
- 战后复盘；
- 决策时间线；
- 错误分类报表；
- 教学直播中的规则和时机提示。

### 6.5 商业化边界

商业化应围绕自研代码、数据管线、分析服务、插件工程能力展开，而不是直接转售 Blizzard 卡牌文本、图片、商标或未经授权的第三方统计。任何外部数据接入前必须单独完成来源登记和授权审核。

## 7. 推荐优先级

### P0：数据与中文映射

- 建立 `hero_skill_registry.json` 和 `card_object_catalog.json` 数据契约；
- 补齐 minion、tavern spell、trinket、activate、dark gift、hero、hero skill、buddy 注册表审计；
- 所有用户可见候选名走 `CardNameService`，失败显示 `unknown`。

### P1：实时局面与动态铸币

- 实现 `LiveSceneAnalyzer` 和 `EconomySnapshot`；
- 统一 `liveGold`；
- 修复 `planHash`、UI 可支付过滤和视觉计划不一致。

### P2：统一动作评分

- 新增 `ActionType.Activate`；
- 实现 `HeroSkillDecisionService`；
- 把买、卖、升、刷、冻、技能、发动纳入 `UnifiedActionScorer`；
- legacy 保留为 Shadow 对比和回退。

### P3：暗赐决策服务

- 把官方 Gift/offering/限制规则结构化；
- 实现“是否花 3 金币现在点”和“候选随从 + Gift 评分”；
- 接入 `RecommendationPlan` 和暗赐 UI。

### P4：真实日志验证

- 用真实 Power.log 回放用户反馈场景；
- 修复发现误触发、中文映射、文字/UI 不一致；
- 全量门禁通过后再决定 Release。

## 8. 风险与约束

- 不修改 `.env`、密钥、CI/CD、数据库 schema；
- 不读取/写入 `%APPDATA%\bob-coach\data`；
- 不引入 BGDUO；
- 不把未经授权的第三方数据写入 Git/DLL/ZIP；
- 暗赐真实 UI、`BG36_MidGameEffect_000`、`BG36_HERO_101p` 和“配置 RG”继续标记 `unknown`；
- 不宣称胜率、名次或策略质量提升；
- 设计文档确认后，下一步应按 `writing-plans` 生成分阶段实现计划，而不是直接大规模改生产代码。

## 9. 主要参考

- Blizzard Season 14 公告：<https://news.blizzard.com/en-us/article/24290433/announcing-battlegrounds-season-14-dark-gifts-of-dalaran>
- Battlegrounds Developer Insight: Dark Gifts：<https://us.forums.blizzard.com/en/hearthstone/t/battlegrounds-developer-insight-dark-gifts/163606/3>
- HDT Creating Plugins：<https://github.com/HearthSim/Hearthstone-Deck-Tracker/wiki/Creating-Plugins>
- HearthstoneJSON：<https://hearthstonejson.com/>
- HearthSim hsdata：<https://github.com/HearthSim/hsdata>
- Firestone Developer resources：<https://github.com/Zero-to-Heroes/firestone/wiki/Developer-resources>
- Battle.net Hearthstone Game Data API：<https://community.developer.battle.net/documentation/hearthstone/game-data-apis>
- 本地证据：`E:\HDT-BobCoach-evidence\season14\raw\`、`E:\HDT-BobCoach-evidence\season14\generated\registries\`

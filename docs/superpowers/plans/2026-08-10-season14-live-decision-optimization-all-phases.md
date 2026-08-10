# BobCoach Season14 实时决策优化 Phase 1-4 总计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development. Each phase must keep legacy visible, Shadow compare available, and fail closed on unknown data.

**Goal:** 完成 Season14 单人模式插件从“规则早退 + 多层 UI 改写”到“统一数据对象库 + 统一局面分析 + 统一动作评分 + 单一推荐计划 + 真实日志验收”的迭代优化。

**Architecture:** Phase 1 先建立数据对象库、动态铸币和 Activate 最小链路；Phase 2 建立统一动作评分、英雄技能和暗赐决策；Phase 3 让 `RecommendationPlan` 成为文字和 UI 的唯一事实源；Phase 4 用真实 Power.log 回放和 UI 证据验证。

**Tech Stack:** .NET Framework 4.7.2、C# 7.3、HDT Plugin API、HearthDb、Power.log、版本化 JSON registry、Node.js/PowerShell 门禁。

## Global Constraints

- 不 push、不 rebase、不 reset --hard、不 force push、不删历史。
- 不改 `.env`/密钥/CI/CD/database schema；不读写 `%APPDATA%\bob-coach\data`；不动 BGDUO。
- 不把原始第三方响应、用户日志、回收数据写入 Git/DLL/ZIP。
- 不编造赛季卡牌、机制、权重、tier 或效果；缺失数据显式 `unknown`，fail closed。
- 不宣称胜率、名次、Top4 或策略质量提升。
- 暗赐真实 UI、`BG36_MidGameEffect_000`、`BG36_HERO_101p`、“配置 RG” 保持 `unknown`。
- 每阶段必须独立测试、可回退、可发布；legacy 仍是玩家可见推荐的默认边界。
- 所有新数据工具必须要求显式 `--data-root`，禁止默认读取 `%APPDATA%\bob-coach`。

## Phase 1：数据与实时局面基础

目标：

1. 建立运行时 `CardObjectDatabase`，精确 CardId 查询、中文名、机制、版本身份。
2. 建立 `HeroSkillCatalog`，主动/被动技能类型和费用规则化。
3. 建立 `EconomySnapshot` 和 `LiveSceneAnalyzer`，统一 `liveGold`。
4. 新增 `ActionType.Activate` 和 `ActivateHint`。
5. 增加发布前注册表审计和未知卡名门禁。

详细任务见：
[Phase 1 实施计划](2026-08-10-season14-live-decision-optimization-phase1.md)

## Phase 2：统一动作评分与赛季决策服务

目标：

1. `ActionType.Activate` 进入 `UnifiedActionScorer`。
2. `HeroSkillDecisionService` 替代 `HeroPowerEngine` 的文本规则猜测。
3. `DarkGiftDecisionService` 把官方 Gift/offering/限制规则结构化为可计算服务。
4. 买、卖、升、刷、冻、技能、发动、暗赐进入同一候选评分面。
5. legacy 保留为 Shadow 对比和回退。

## Phase 3：RecommendationPlan 与 UI 一致性

目标：

1. `RecommendationPlan` 成为状态文本、商店高亮、按钮光晕、手牌建议、暗赐候选的唯一事实源。
2. 渲染层禁止再次猜测动作含义。
3. `planHash` 包含 `liveGold`、动作类型、目标 CardId、文本摘要、风险等级和面板签名。
4. 发现面板只接受 Power.log 权威 `WaitThenShowChoices` 触发，移除 zone6/文本匹配兜底。
5. 用户可见卡名全部走统一中文映射，失败显示 `unknown`。

## Phase 4：真实日志回放、验收与 Release

目标：

1. 用真实 Power.log 回放用户反馈场景，验证发现、暗赐、被动选择、0 铸币门控。
2. 验证文字面板和 UI 元素来自同一份计划。
3. 至少完成源码、合成测试、真实日志回放、门禁四层验证。
4. 更新过程实施文档和交接文档。
5. 没有真实 HDT/Power.log/截图/录像证据时，不把源码测试通过当作真实 UI PASS。

## 成功标准

- 玩家可见推荐和 UI 来自同一份可解释计划。
- 动态铸币不足时不会显示购买、刷新、升级、出售、发动等不可支付 UI。
- 被动英雄技能不提示，主动技能必须满足费用、解锁、目标、机会成本门控。
- Activate、暗赐、新英雄、新卡牌通过数据对象库进入决策，不再依赖硬编码分支。
- 未知数据不参与高置信推荐，不泄漏裸 CardId。
- 每次 Release 前有注册表、行为测试、真实日志和 UI 证据门禁。

## 参考

- [Season14 实时决策优化设计](2026-08-10-season14-live-decision-optimization-design.md)
- [官方数据边界与复用分析](../maintainer/2026-08-10-season14-official-data-and-reuse-analysis.md)
- [Phase 1 实施计划](2026-08-10-season14-live-decision-optimization-phase1.md)

# BobCoach Season14 实时决策优化过程实施日志

Date: 2026-08-10
Status: In progress
Owner: Codex goal session

## 当前目标

完成 Phase 1-4 的迭代优化，使算法逻辑、动态铸币、英雄技能、暗赐、Activate、发现面板和 UI 视觉计划保持一致，并通过真实日志回放验证。

## 阶段状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| Phase 1 | In progress | 核心已实现；生成器/未知卡审计门禁待补齐 |
| Phase 2 | In progress | 三个决策服务已实现并测试；接入 DecisionEngine 待做 |
| Phase 3 | In progress | RecommendationPlan 与 planHash liveGold 已加；全量 UI 接管待做 |
| Phase 4 | In progress | 真实旧日志回放已通过；Release/部署待做 |

## 过程记录

### 2026-08-10：建立目标与计划

- 创建 Phase 1-4 总计划。
- 确认全局约束：不 push、不删历史、不读写 AppData 数据、不碰 BGDUO、不宣称胜率。
- 选择 inline 执行，不使用 subagent，以控制 token 成本。

### 2026-08-10：Phase 1 基础实现

- 新增 `GameState.LiveGold`，由 `GameStateExtractor` 写入，`Simulator.DetachedCopy` 同步。
- 新增 `EconomySnapshot` 和 `LiveSceneAnalyzer`，并加入独立行为测试。
- 新增 `CardObjectDatabase` 和 `HeroSkillCatalog`，聚合现有 season14 registry。
- 新增 `ActionType.Activate`、`ActivateHint`，并在 `ActionEnumerator` 和 `DecisionVisualizer` 接入。
- `ActionFeatureSchema.ActionTypeBiases` 固定为原有 10 类，避免破坏现有模型契约。
- `CardNameService` 未知卡改为 `unknown`，不再泄漏裸 CardId。
- 生产项目已编译通过。

### 2026-08-10：Phase 2-4 基础实现

- 新增 `UnifiedActionScorer`、`HeroSkillDecisionService`、`DarkGiftDecisionService`。
- 新增 `RecommendationPlan`，`BobCoachPlugin.planHash` 加入精确 `LiveGold`。
- 完成 Phase 1-4 独立行为测试并接入 `run_behavior_tests.ps1`。
- 真实 `Power_old.log` 回放通过：Faelin 被动选择保持独立。

## 验证记录

| 时间 | 验证项 | 结果 | 证据 |
| --- | --- | --- | --- |
| 已执行 | Phase1LiveDecisionBehavior | PASS 16 assertions | `tests/test_phase1_live_decision.ps1` |
| 已执行 | Season14RegistriesHarness | PASS 349 assertions | `tests/test_season14_registries.ps1` |
| 已执行 | Production build | PASS | `BobCoach.dll` 编译成功 |
| 已执行 | npm run test:contracts | PASS | 全部 contract tests |
| 已执行 | npm run test:behavior | PASS | 含 Phase1-3 新测试 |
| 已执行 | npm run test:package | PASS | 离线包/安装/回滚测试 |
| 已执行 | validate_repository.ps1 | PASS 519 files | - |
| 已执行 | git diff --check | PASS | 仅 LF/CRLF 警告 |
| 已执行 | 真实 Power_old.log 回放 | PASS | Faelin 2026-08-09 real log |

## 下一步

- 补齐 Phase 1 的 `generate_card_object_catalog.js`、`hero_skill_registry` 生成器和未知卡审计门禁。
- 把 Phase 2 三个服务接入 `DecisionEngine` / `BobCoachPlugin`。
- 让 `RecommendationPlan` 真正接管文字面板、商店高亮、按钮光晕和暗赐候选。
- 完成正式 Release 构建、部署、真实 UI 证据和交接文档。

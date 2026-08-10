# BobCoach Season14 实时决策优化过程实施日志

Date: 2026-08-10
Status: In progress
Owner: Codex goal session

## 当前目标

完成 Phase 1-4 的迭代优化，使算法逻辑、动态铸币、英雄技能、暗赐、Activate、发现面板和 UI 视觉计划保持一致，并通过真实日志回放验证。

## 阶段状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| Phase 1 | Completed | 数据对象库、动态铸币、Activate、生成器和未知卡审计已接入 |
| Phase 2 | Completed | LiveSafetyGate/HeroSkill/DarkGift 已接入玩家可见生产链路 |
| Phase 3 | In progress | RecommendationPlan 与 planHash liveGold 已加；全量 UI 接管待做 |
| Phase 4 | In progress | Release 已构建部署；真实 UI 证据仍待用户实测 |

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
- 阶段性提交：`cbb7db6 feat(season14): add live decision foundation and phase1-3 services`

### 2026-08-10：生产决策接入与数据工具

- 新增 `LiveSafetyGate`，已接入 `BobCoachPlugin` 玩家可见升本/刷新/购买抑制。
- 英雄技能提示改用 `HeroSkillCatalog + HeroSkillDecisionService`。
- 暗赐提示接入 `DarkGiftDecisionService`，T3、每回合一次、每局 3 次、3 金规则参与 UI 判断。
- 新增 `generate_card_object_catalog.js`、`generate_hero_skill_registry.js`、`audit_unknown_card_ids.js`。
- 已生成 `card_object_catalog.json` 816 项、`hero_skill_registry.json` 116 项。
- 未知卡名兜底审计通过，源码不再使用裸 CardId 作为用户可见卡名。

### 2026-08-10：Release 与部署

- Release DLL：bytes=867840，SHA256=`079DB8AEA85EB78D641C6E54FC1216BC5CE79ABB69CFF8B708914F68D1E1E8A2`。
- AppData 与 HDT 程序目录均已部署同一 Release DLL，哈希一致。
- 离线包：`BobCoach-1.1.0-win-x64.zip`，SHA256=`AB207DFD755B66C5CBF768973DDA00F081211914161F892DABA4F0A6BF49EFF1`。
- AppData 部署前归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T082625389Z-39ad60aa`。
- 程序目录部署前归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T162653Z-program-dir-pre-deploy`。

### 2026-08-10：Activate 决策与 UI 一致性补强

- `Simulator` 新增 `SimulateActivate`：扣铸币、标记本回合已发动，进入决策模拟。
- `DecisionEngine` 对 Activate 使用 `UnifiedActionScorer`，不再被当作“状态不变”忽略。
- `BobCoachPlugin` 消费 `VisualPlan.ActivateHint`，铸币足够时渲染目标脉冲和“发动”状态文本。
- `RecommendationPlan.FromVisualPlan` 从最终 `VisualPlan` 派生统一计划，并在 `DispatchRender` 做一致性校验。
- 最新 Release：`BobCoach.dll` bytes=869888，SHA256=`F0A15DEDF4A909BD59B17E56FF036E3B5832F51D5991DD4F0AAA804762E9AAE5`。
- 最新离线包：`BobCoach-1.1.0-win-x64.zip`，SHA256=`590805FCD3102A48B1425F141CEA0914A5CD46E598B049D35A4C0884507A3F9F`。
- AppData 归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T083753644Z-4b8f17cb`。
- 程序目录归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T163811Z-program-dir-pre-deploy`。
- `VisualPlan` 新增 `Recommendation` 字段，`DispatchRender` 把最终 `RecommendationPlan` 挂回计划对象。
- Release v4：`BobCoach.dll` bytes=869888，SHA256=`B0D9A1A272733D01ED9102EDDE32751155ECF39B3CAC2F4B55D4836AE15F32F3`。
- Release v4 离线包：`BobCoach-1.1.0-win-x64.zip`，SHA256=`60B7117175850934E9F8C52C2E2BFE5C87AC333D606F13EAD7DCFD7BFC9CBF0C`。
- v4 AppData 归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T084616357Z-1394f82e`。
- v4 程序目录归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T164633Z-program-dir-pre-deploy`。

### 2026-08-10：真实日志证据采集器

- 新增 `tools/release/collect_real_ui_evidence.ps1`，校验两处 DLL 哈希、定位最新非空 `Power.log`、截取尾部 20000 行并回放。
- 最新会话 `Hearthstone_2026_08_10_15_56_35` 的尾部回放通过：
  `discover=1 trinket=2 darkGift=0 timewarp=0 completion=2 passive=0`。
- 尾部回放证据：`E:\HDT-BobCoach-evidence\season14\generated\ui-evidence\power-latest-tail-20000.log`。
- 真实 UI 证据仍待 HDT 实机截图/录像和插件日志。
- 新增验收协议：[真实 UI 验收协议](2026-08-10-season14-real-ui-validation-protocol.md)。

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
| 已执行 | Phase1 data tools | PASS | 生成器/审计已接入 contract suite |
| 已执行 | UI lifecycle | PASS 178 | 含安全门禁/英雄技能/暗赐 |
| 已执行 | npm run test:contracts | PASS | 含 phase1 data tools |
| 已执行 | npm run test:behavior | PASS | Phase1-3 + safety/dark gift |
| 已执行 | npm run test:package | PASS | Release DLL/ZIP/安装/升级/回滚 |
| 已执行 | validate_repository.ps1 | PASS 524 files | - |
| 已执行 | git diff --check | PASS | - |
| 已执行 | Release 部署 | PASS | AppData + HDT 程序目录哈希一致 |

## 下一步

- 补齐 Phase 1 的 `generate_card_object_catalog.js`、`hero_skill_registry` 生成器和未知卡审计门禁。
- 把 Phase 2 三个服务接入 `DecisionEngine` / `BobCoachPlugin`。
- 让 `RecommendationPlan` 真正接管文字面板、商店高亮、按钮光晕和暗赐候选。
- 完成正式 Release 构建、部署、真实 UI 证据和交接文档。

## 阻塞状态

截至 2026-08-10，真实 HDT 对局 UI 证据仍无法采集：

- HDT 进程未启动；
- 最新 Hearthstone 会话没有完整 Season14 对局 `Power.log`；
- AppData 中没有新的 BobCoach/HDT 插件运行日志；
- 截图/录像证据目录为空。

采集工具和验收协议已就绪，用户启动 HDT 并进入 Season14 单人模式对局后即可恢复。

## 2026-08-10 实机修复：HDT 实际加载目录未同步

根因：HDT 本机实际加载 `C:\Users\19844\AppData\Local\HearthstoneDeckTracker\app-1.54.1\Plugins\BobCoach.dll`，
该目录仍为旧版 675840 DLL，导致插件未显示最新 UI。

修复：

- 已关闭 HDT 进程。
- 已将 Release v4 DLL 和 checksum 同步到：
  - `app-1.54.1\Plugins`
  - `app-1.54.0\Plugins`
  - `%APPDATA%\HearthstoneDeckTracker\Plugins`
  - `D:\software\game\HDT-V2.7.0\HDT\Plugins`
- `collect_real_ui_evidence.ps1` 已增加本地 HDT app 目录校验。
- `deploy_local_release.ps1` 已增加本地 HDT app 目录自动同步。
- 下一步：重新启动 HDT，进入 Season14 对局验证 UI。
- 2026-08-10 用户手动关闭 HDT 后，已通过 `deploy_local_release.ps1` 正式部署 v4 到 AppData 与本地 app 目录。
- 部署归档：`E:\HDT-BobCoach-evidence\dll-archive\20260810T090620500Z-60c731f0`。
- 四个插件路径的 DLL/checksum 均匹配 Release v4。

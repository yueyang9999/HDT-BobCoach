# BobCoach 赛季数据契约与 8/5 更新预留实施计划

> 状态：已确认 v1（2026-08-03，owner 已确认；S0/S1/S2 已完成并通过全量验证，S3-S5 待 8/5 数据源更新后按序推进）
> 衔接：`docs/superpowers/plans/2026-07-31-next-season-algorithm-optimization.md`（P0-P4 已交付）、`docs/maintainer/SEASON_DATA_CONTRACT.md`（本计划配套规范）
> 性质：本计划只做「数据契约、资产归档、生成器管线、验证路径」，不预测新赛季内容，不改变 Shadow/legacy 策略。

## 1. 目标与边界

**目标**

1. 在 8/5 游戏内容更新前，把「新机制/新卡牌规则化模块化」所需的格式、管线、校验全部预留到位。
2. 建立赛季数据契约，让注册表、数据集、模型工件都带版本身份，解除「数据不知道属于哪个赛季」的问题。
3. 用最低成本打通 Shadow 采集到训练标注的路径，为解除 `blocked-by-season-data` 铺路。

**边界（继承 AGENTS.md 与 7/31 计划）**

- 不虚构/推断新赛季卡牌、机制、数值；规则只能来自 8/5 后的真实数据源。
- 统一策略保持 Shadow，`legacy-v1.1.0` 保持可见；效果验收仍需 300+ 真实 version-matched 标注。
- 原始回放/数据集/模型候选不进仓库；不进新依赖；不加网络上传。
- 8/5 前不动生产数据文件（`%APPDATA%\bob-coach\data\`），只做规范、工具、代码层准备。

## 2. 现状资产盘点（2026-08-03 实测）

### 2.1 机制注册表（运行时 `%APPDATA%\bob-coach\data\`）

| 资产 | 大小 | 模块化现状 | 契约头 |
| --- | --- | --- | --- |
| `anomaly_registry.json` | 39KB | 数据驱动（Registry+RuleEvaluator+FactSource） | 无 |
| `trinket_registry.json` | 151KB | 数据文件已存在，但 C# 侧 `TrinketEffectRegistry` 仍硬编码 17 个 CardId | `_meta` 有日期无版本 |
| `buddy_registry.json` / `hero_power_registry.json` | - / 274KB | 数据驱动 + 生成器 | 无 |
| `cards.json` | 591KB | 静态快照 | 无（版本只藏在图片 URL） |
| `golden_rules.json` / `anomaly_facts.json` | 156KB | 数据驱动 | 无 / 仅有 build number |
| `meta_info.json` | 小 | 同步状态 | **无版本字段** |

### 2.2 历史与评估数据

| 资产 | 说明 | 处理 |
| --- | --- | --- |
| `_firestone_cached.json`（21.8MB）等 | 完整卡牌库快照；`DATA_SOURCES.md` 已声明无授权路径 | 归档不读不删 |
| `replay_enhanced\` / `replay_xmls\` | 大量对局回放 | 归档 + manifest，不删 |
| `eval_decisions.jsonl`（246 行）/ `eval_decisions_clean.jsonl`（1849 行） | 决策样本，**无版本身份字段** | 降级为 legacy 参考，不能作 `versionMatched` 训练集 |
| `trinket-stats\` / `engine_vs_player_dataset.json` / `combat_calibration_dataset.json` | 统计与校准数据 | 归档 + 补 manifest |
| `decision-events.jsonl` | Shadow 事件，当前不存在 | 8/5 后落地，事件内联版本身份 |

### 2.3 已就绪的预留（代码层）

- 45 特征槽第 45 位 `season_extension_value` + `HasExactSeasonProvenance`（`ActionFeatureExtractor.cs:102`、147）。
- `DecisionPolicyFactory` + `ISeasonFeatureExtension` 注入点；`ProductionSeasonFeatureExtension` 为占位。
- `DecisionEventRecorder` 五类事件已实现并接线（`BobCoachPlugin.cs:837`），`DecisionRuntimeMode.Shadow` 硬编码。
- `train_pairwise.js` 的 `versionMatched` 校验已存在（只差真实带版本数据）。
- `tools/build/` 生成器先例：buddy、shop pool、timewarp、card classification、trinket stats diff。

## 3. 8/5 更新预留设计（本计划核心）

原则：**8/5 前不猜规则，只把「规则进来后能快速落库」的通道修好。**

### 预留项 R1：注册表契约头（数据层）

- 所有生成器输出统一 `_meta` 头部（字段见 `SEASON_DATA_CONTRACT.md` §3）。
- `meta_info.json` 补 `season/gameBuild/cardPoolIdentity`（8/5 数据同步时一并写入，非现在）。
- 影响：全部 `tools/build/generate_*.js|ps1` + 生成器代码，产出契约头。

### 预留项 R2：cardPoolIdentity 定义（必须先定死）

- 规则见 `SEASON_DATA_CONTRACT.md` §4：卡池 CardId 排序 → 紧凑 JSON → SHA-256 前 16 位。
- 落点：`tools/model/contracts.js` 新增函数 + 单元测试；C# 侧不改。
- 风险：若运行时 `state.CardPoolIdentity` 与离线算法不一致，需先修数据来源，不绕过校验。

### 预留项 R3：Trinket 注册表化（唯一的机制代码改造）

- 现状：`TrinketEffectRegistry.cs` 硬编码 17 个 CardId + switch；新赛季饰品必须改代码。
- 改造：新增 `generate_trinket_registry.js`（读取 trinket 数据 → 输出带契约头的注册表），C# 侧改为读取注册表 + 现有 `TrinketRuleEvaluator` 求值。
- 用现有 17 个 CardId 做回归验证；8/5 新数据一到即跑生成器，不改 C#。
- 替代方案（不改代码）：8/5 后临时补 CardId 常量，撑一期。成本最低但不可持续，且 Shadow 采集到的饰品特征不完整。

### 预留项 R4：Shadow 采集验证路径

- 验证 `DecisionEventRecorder` 在本地回放能产出 `decision-events.jsonl`，事件内带 `gameBuild/seasonId/cardPoolIdentity`。
- 确认 `CreateForLocalReplay` 触发条件（现有「本地回放录制选项」）在 8/5 后可用。
- 产出：最小可用的真实标注采集链路（采集≠标注，标注仍需人工/专家）。

### 预留项 R5：资产归档目录与 manifest

- 建 `E:\HDT-BobCoach\archive\<season>\`（项目盘、不进 git），现有大文件按赛季/来源归位。
- 每目录 `manifest.json`：来源、版本、时间、sha256。
- 只归档，不删除任何现有文件（删除属红线）。

## 4. 实施阶段与成本

估算基准：单人、熟悉现状代码、Node 标准库 + C# 7.3，无新增依赖。

| 阶段 | 内容 | 成本（有效天） | 依赖 | 交付物 |
| --- | --- | --- | --- | --- |
| S0 文档与契约（现在） | 契约规范、本计划、资产清单 | 0.5-1 | 无 | 两份文档 + 资产清单 |
| S1 契约工具（8/5 前） | R2 cardPoolIdentity 函数+测试；生成器输出契约头 | 1-1.5 | S0 | `contracts.js` 扩展、生成器升级 |
| S2 Trinket 注册表化（8/5 前，可选） | R3 生成器 + C# 读取改造 + 回归 | 1.5-2 | S1 | `generate_trinket_registry.js`、注册表读取 |
| S3 Shadow 验证（8/5 后） | R4 本地回放采集验证 | 0.5-1 | 8/5 数据 | `decision-events.jsonl` 样本 |
| S4 数据同步与契约落地（8/5 后） | R1/R5 新数据入库、meta_info 补版本、归档 manifest | 1-1.5 | 8/5 数据 | 带契约头的新注册表 |
| S5 训练解锁（后续） | 用真实 Shadow 数据构建 version-matched 标注 → 训练 | 人工标注为主，开发 1-2 | S3+S4 | 候选模型（仍 Shadow） |

**合计**：8/5 前 S0+S1 约 1.5-2.5 天；加 S2 约 3-4.5 天（8/3-8/5 窗口紧张，建议 S2 视 S1 完成度决定是否压缩）。

## 5. 决策点（owner 逐项确认）

1. **契约规范**（S0 产物）是否采纳？采纳后写入 `AGENTS.md` 引用。
2. **S1 是否执行**：`cardPoolIdentity` 定义 + 生成器契约头改造。这是 8/5 预留的必备项。
3. **S2 是否执行**：Trinket 注册表化。成本 1.5-2 天，8/5 前窗口紧；不做则 8/5 后临时补 CardId 常量撑一期。
4. **S3-S5 是否按序推进**：取决于 8/5 数据源更新内容；先出数据再排期。
5. **资产归档位置**：`E:\HDT-BobCoach\archive\` 是否可接受（不进 git）。

## 6. 风险与取舍

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| 8/5 前 S2 未完成 | 新饰品只能补硬编码常量 | 接受临时方案，S2 顺延；不影响 Shadow 主链路 |
| `cardPoolIdentity` 运行时与离线不一致 | 统一策略永远不启用 | S1 内加离线/运行时对拍测试，先修来源不绕过 |
| 数据源更新内容与预期差异大 | 生成器规则覆盖不全 | 生成器按「未知机制标记 unknown + 降置信度」设计，不硬猜 |
| 人工标注是瓶颈 | `blocked-by-season-data` 长期存在 | 先跑通采集与半自动标注流程，再积累 300+ 场景 |
| 归档目录增长 | 磁盘占用 | manifest + 赛季保留策略，清理走 owner 确认 |

## 7. 验收

- S1：`contracts.js` 契约测试通过；生成器产物带契约头；离线/运行时 cardPoolIdentity 对拍一致。
- S2：Trinket 注册表化后，17 个现有 CardId 行为与改造前一致（回归）。
- S3：本地回放产出带版本身份的 `decision-events.jsonl`。
- S4：新赛季注册表/卡牌数据带契约头入库，`meta_info.json` 含版本字段。
- S5：`train_pairwise.js` 能以真实 version-matched 数据跑通（结果仍保持 Shadow）。

## 8. 未决事项

- 运行时 `state.CardPoolIdentity` 的确切来源与取值格式（S1 前需在代码中定位确认）。
- Shadow 采集的触发选项与 8/5 后实际对局环境。
- 人工标注的专家来源与流程（S5 前置条件）。

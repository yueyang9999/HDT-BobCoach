# 赛季数据契约（Season Data Contract）

> 状态：已确认 v1（2026-08-03，owner 已确认）
> 关联文档：`AGENTS.md` 的 Next-Season Algorithm Data Boundary、`docs/superpowers/plans/2026-07-31-next-season-algorithm-optimization.md`、`tools/model/contracts.js`
> 本规范是执行性约定，不授权任何代码改动；实施需先经 owner 确认。

## 1. 目的

让每一份机制注册表、数据集、模型工件都能稳定回答三个问题：

1. **它属于哪个游戏版本/赛季？**（`gameBuild` + `season` + `cardPoolIdentity`）
2. **它来自哪个数据源、何时生成？**（`source` + `capturedAt`）
3. **内容是否可复现、可校验？**（`sha256` + 生成器确定性）

运行时已有版本隔离雏形：`HasMatchingStateIdentity` 要求 `GameBuild / SeasonId / CardPoolIdentity` 三项完全匹配才启用统一策略（见 `src/BobCoach/Core/Decision/ActionFeatureExtractor.cs`）。本契约把同一套身份从**运行时**推广到**数据层**，否则生产统一策略永远无法被版本校验放行。

## 2. 现状缺口（2026-08-03 实测）

| 文件/位置 | 现状 | 缺口 |
| --- | --- | --- |
| `%APPDATA%\bob-coach\data\meta_info.json` | 只有 `lastSyncCheck/lastSyncApplied/cardsCount` 等 | **无版本字段**；交接摘要中的 `lastSyncVersion` 实际不存在 |
| `trinket_registry.json` | 有 `_meta`（`last_updated/source/total_trinkets`） | 无 `season/gameBuild/cardPoolIdentity/sha256` |
| `anomaly_registry.json` | 纯字典，无 `_meta` | 无任何契约头 |
| `cards.json` | 数组；图片 URL 内含 `35.6.2.244584` | 无统一版本头，版本号只能从 URL 推断 |
| `hero_power_registry.json` / `buddy_registry.json` / `golden_rules.json` | 纯字典 | 无任何契约头 |
| `anomaly_facts.json` | 有 `build: 246003` | 是 build number，非游戏版本字符串 |
| `eval_decisions*.jsonl` / `engine_vs_player_dataset.json` 等 | 无版本字段 | 不能充当 `versionMatched` 训练集（`train_pairwise.js` 因此保持 `blocked-by-season-data`） |
| `decision-events.jsonl`（Shadow 采集） | 当前不存在 | 落地时必须在事件内携带版本身份 |

## 3. 契约字段

所有注册表 JSON、数据集 JSONL、模型工件统一携带以下头部（JSON 对象或每行事件内联字段）：

| 字段 | 类型 | 必填 | 含义 |
| --- | --- | --- | --- |
| `schemaVersion` | int | 是 | 本契约版本，当前为 `1` |
| `season` | string | 是 | 赛季标识，如 `"35.4"`、`"35.6"`（与 Hearthstone 补丁版本对齐） |
| `gameBuild` | string | 是 | 完整构建号，如 `"35.6.2.244584"`；`unknown` 时必须显式写 `"unknown"` |
| `cardPoolIdentity` | string | 是 | 卡池指纹（见 §4），`unknown` 时显式写 `"unknown"` |
| `source` | string | 是 | 数据来源，如 `hearthdb-snapshot-2026-08-05`、`gamerhub-35.6.2`、`shadow-collection-v1` |
| `capturedAt` | string | 是 | ISO-8601 UTC 生成时间 |
| `sha256` | string | 是 | 内容规范序列化的 SHA-256（生成器输出或回读校验） |
| `provenance` | object | 否 | 生成器名称+参数、输入文件指纹，用于复现 |

缺失任何必填字段或值为 `unknown` 时，下游按现有降级策略处理：降低置信度、保持 Shadow、回退 `legacy-v1.1.0`。

## 4. cardPoolIdentity 生成规则（必须先定死）

**问题**：`HasMatchingStateIdentity` 依赖 `cardPoolIdentity` 相等，但项目内目前没有统一的生成定义。各组件若各自计算，值永不相等，版本校验形同虚设。

**建议规则（v1）**：

1. 输入：当前赛季卡池的全部 `CardId`（以 `cards.json` 或 HearthDb 卡数据为准，取 `"type": "minion"` 或酒馆战棋相关实体）。
2. 集合语义：按卡池集合去重（重复 `CardId` 不影响指纹），保证不同来源快照仅因重复行差异时指纹一致。
3. 排序：按 UTF-8 字节字典序排序（Ordinal）。
4. 序列化：`["ID1","ID2",...]` 紧凑 JSON，无空白。
5. 摘要：SHA-256，取前 16 位十六进制小写。
6. 输入校验：`cardIds` 非数组、为空、或包含非字符串/空字符串时抛错；调用方无可用卡池时显式传 `null` 得到 `"unknown"`。
7. 实现落点：`tools/model/contracts.js` 增加 `cardPoolIdentity(cardIds)`，C# 侧 `HasMatchingStateIdentity` 保持字符串比较不变；`unknown` 值与空输入在运行时按 `IsKnownIdentity` fail-closed（统一策略不启用）。

实施时必须验证：离线计算的指纹与运行时 `state.CardPoolIdentity` 一致；不一致则先修来源，不能改校验逻辑绕过。

## 5. 各类资产适配清单

| 资产 | 契约形态 | 生成/校验工具 |
| --- | --- | --- |
| 机制注册表（trinket/anomaly/buddy/hero_power/prize/timewarp/golden_rules） | JSON 根级 `_meta` 头部 + 原数据 | 现有 `tools/build/generate_*.js|ps1` 生成器升级为输出契约头；新增 `tools/build/audit_*` 校验 |
| 卡牌库 `cards.json` | 文件头对象或外部 manifest | 同步脚本记录 source/build |
| Shadow 事件 `decision-events.jsonl` | 每行事件内联身份字段 | `DecisionEventRecorder` 落地时带上身份（需确认 `GameBuild/SeasonId/CardPoolIdentity` 可得） |
| 训练/评估数据集 | 每行内联身份字段（与 `train_pairwise.js` 的 `versionMatched` 一致） | `build_pairs.js/split_dataset.js` 增加身份校验 |
| 模型工件 envelope | 已有 `payloadSha256` 机制 | 补充 `season/gameBuild/cardPoolIdentity` 头 |

## 6. 校验与清理

- 契约校验做成确定性脚本（Node 标准库，符合现有 `tools/model` 约束），输入文件、输出通过/失败清单；不进仓库的原始数据仍留在外部 data root。
- `AGENTS.md` 的公开内容白名单不变：原始回放、数据集、模型候选仍禁止入仓；契约头只作用于外部数据与生成器产物。
- 过期数据按 `AGENTS.md` Cleanup 规则归档删除，删除需 owner 确认。

## 7. 与现有约束的关系

- 本契约**不**授权预测/推断新赛季卡牌或机制；AGENTS.md 的「不虚构数据」边界不变。
- 本契约只规定「数据怎么标注身份、怎么校验」，不改变 Shadow/legacy 运行策略。
- 8/5 数据源更新前，只做规范与工具准备，不读写生产数据文件。

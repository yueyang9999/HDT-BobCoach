# 组合记忆与上限/破局推理层 —— 设计规范 v1.0（实施总纲）

- 日期：2026-09-07 · 版本：1.0 · 状态：评审完成，待实施
- 实施对象：HDT-BobCoach（插件 C# + 确定性模拟器 JS）
- 本文档为**实施唯一权威入口**；其余文档按 §12 索引引用，冲突以本文档为准。
- 阅读顺序建议：§0–§3 → §10（先立项与门禁）→ §4–§9（实现）→ §11（验收）。

## 0. 摘要与关键决策（ADR 摘要，含理由）

| # | 决策 | 理由 / 证据 |
|---|---|---|
| D-A | **不做全局体系引导（聚合口径证伪）** | 最小可行性实验：默认环境自然平均 2.33/前二4次，全局 steer 大多劣化；缺口是条件性的 |
| D-B | **组合层只在 rescue/pivot 条件触发时干预；平时 Shadow** | 破局缺口仅出现在自然轨迹失败/停滞局（G3：8→3） |
| D-C | **解析概率=低成本先验+剪枝；确定性模拟器 rollout=真值** | 池/时钟/预算可解析；全随机过程用 rollout 确认 |
| D-D | **分层投入 L0→L4，Go/No-Go 逐层放行** | 防止整包沉没成本；L0+L1 约 0.5 周出方向性结论 |
| D-E | **可解析动态进规则层；整局联合动态留给学习层** | 规则穷尽不了 level×buy×comp 联合最优 |
| D-F | **legacy 零改动；改动经 ISSUE_TRACKER + 代码审查 + 100% 回归** | 项目硬纪律 |
| D-G | **数据边界：不发明新赛季卡/权重；数值位 null=unknown 降置信；产物标 build/日期** | AGENTS 政策 |

## 1. 目标 / 非目标

目标：给每个局面输出"候选体系 ×（成型度/缺口/可达上限）→ 破局动作优先级"，
先经 ceiling-gap 量化缺口，再以 rescue 模式进入 Shadow，提升"复杂局面下快速提升战力/够到上限"的能力。
非目标：不做 LLM 决策判分；不做大规模 RL；不做全局行为改写；不发布未经验证数值。

## 2. 术语

体系(comp)：引擎卡+增幅卡+节奏/过渡卡构成的可成型方向。引擎卡=CORE、增幅=ADDON、过渡=CYCLE。
成型率：给定视野 T\* 内凑齐目标核心子集的概率。破局：自然轨迹停滞/失败时切换到价值更高的方向。
视野 T\*：由计划升本曲线决定的高星级卡进入可抽池的时间上限。崩盘-clock：预计剩余回合。

## 3. 架构分层（决策入口，预算按层批）

```
L4 学习层(远期)     价值函数+束/深度规划（承接联合动态）
L3 动态增强(可选)   D7 自适应K(DPP) · D8 期权资产 · D12 M5正式化
L2 模型细化(条件)   D4 竞争/反制 · D3 流动性 · D6 阶段目标 · D1 多通道 · D9 记忆+复盘
L1 骨架(核心)       P1 三桶 ceiling-gap · C-12 最小版（D2/D5/D10）
L0 基座(必做)       模板归一45真卡ID · 确定性门禁 · 回放/真实状态语料
```
每层门禁、成本、Go 条件见 §11。

## 4. L0 数据契约

### 4.1 composition_templates.json（evidence/knowledge-base/，不入库）
契约头：`{schemaVersion, season:"season14", gameBuild, source:[{name,path,buildOrDate}], capturedAt, sha256}`
条目字段（当前已生成 + P0 补齐项）：
```
{ compId, nameCn, direction:"engine", forcedTribes:[],
  medianCoreTier, engineCardIds[], addonCardIds[], ceilingPower:null,
  missingLinkType:["engine"|"pair_triple"|"spellgen"|"economy"],   // P0 补
  endgame:{tier, template[]}, activation:{requires, windowTurn},     // P0 补(有据才填)
  counters:[],                                                       // 有据才填
  provenance:{src, patch, build} }
```
生成器：`tools/simulation/_build_composition_templates.js`（已滤占位 ID）。
**P0 待办（关键）**：中文 23 体系的主数据源是 `evidence/season14/generated/season14_guide_weight_reference.json`
的 `guideReferences[]`（含 officialCardId），非 comp_strategies 的占位条目——合并后总数 45，
校验：占位 ID=0、无重复 compId、每体系 engine≥1；输出 sha256 与差异报告。

### 4.2 确定性门禁（脚本化，任何改动必跑）
- `node tools/simulation/rollout_probe.js --data-dir <season14-data> --output <o> --diag-repro`（3 连跑一致）
- 每局卡表指纹断言（SimulationEngine.runOneGame 已内置恢复；产物在回归日志贴输出）。
- 数据版本：所有产物标注 数据源 build/日期；混用标分布（AGENTS 版本纪律）。

## 5. 模拟器侧设计（P1 ceiling-gap）

### 5.1 现状（已核实，勿重复实现）
- 引擎引导钩子已存在：`SimulationEngine._rolloutSteer = function(p,ctx,turn,sharedPool,decisions)`
  返回决策数组则替换（SimulationEngine.js 决策循环 ~L730；未设置=零影响）。
- 工具已存在：`tools/simulation/ceiling_gap.js --ceiling [--steer-comps a,b,c] [--templates <file>]`：
  同 seed 确定性重放，steer 全程替换购买（保留节奏项），输出逐局名次表与聚合。
- 确定性已验证：diag-repro 3 连一致、全状态 diff=0、phase1 6 局 0 不一致。

### 5.2 待扩展（P1 三桶口径）
1. 桶①全样本 / 桶②崩盘-clock 子集 / 桶③真实状态子集（回放语料同 build 采样 mid-game 状态）。
2. 崩盘-clock 判定（替代静态 血≤15）：`expTurns = floor((hp+armor)/E[damagePerTurn|stage])`；
   `formTurns = turnsUntilCoreSetReachable(plannedTierHorizon, gold)`；rescue 窗口 = `expTurns < formTurns + safety(2)`；
   血≤15 作兜底。
3. 多 seed（≥3）× 每决策点 3 桶；报告分桶输出 `runs/ceiling-gap-<seed>-<桶>.md`。
4. M1 校准：解析可达性预测 vs 同 seed 引擎实际出现率（多通道口径见 D1）——残差存档持续监控。
产物/追踪：报告 + 残差文件 + ISSUE C-12。

## 6. 插件侧设计（C-12，legacy 零改动）

### 6.1 新增组件与接口（C# 伪签名，实施时对齐现有风格）
```
CompositionState.Evaluate(GameState, TemplateRef) -> {
  compId, enginesOnBoard:int, coreOwned:set, coreMissing:set,
  pairProgress:map, tripleReady:int, spellGenReady:bool, econReady:bool,
  stage:"early|mid|late", clock:{expTurns, formTurns, windowOpen:bool} }
CeilingEstimator.Estimate(state, compState, plannedTierHorizon) -> {
  reachByChannel:{shop, discover, triple, spell, buddy}, ceilingPower:double? (null=unknown),
  confidence, missingLinks:[{type, cards, channel}] }
CompositionPlanner.Plan(state, estimates[], templates) -> {
  mode:"rescue|shadow",  // 仅 rescue 可影响返回(见 6.3)
  recommended:{compId, reason}, actions:RankedAction[2..3],
  windowDesc, contestPenalty, antiFormation:bool }
CompositionMemory.Record(state, plan) / Snapshot   // P3 起全量, L1 先存 {seenPassed,ownedNow,ownedLost,opponentSeen}
```
- 特征用机制/角色（复用 CardClassifier/SynergyEngine/CompLockDetector），不硬编码新卡。

### 6.2 集成点（DecisionEngine.GetBestAction）
位置：mainline.Analyze / memory.Observe 之后、规则瀑布前（现 synergyTribeBuy 同层 ~L510）。
门控：env `BOBCOACH_COMP_PLAN=1` 启用（默认关=现行为逐字节一致）；`BOBCOACH_COMP_PLAN_CONF` 默认 0.6。
Shadow：无论是否返回都写决策事件（DecisionEventRecorder），供 V 系列口径离线分析。

### 6.3 rescue 语义与幂等
- rescue 触发=clock.windowOpen 且 反事实优势>阈值（崩盘局才可能影响返回；稳态只记录）。
- D10 幂等/预算：同回合状态指纹→缓存结论；先规则后组合 early-exit；每次调用 ≤预算；
  重入不重复建议（审查清单含"重入幂等"）。

### 6.4 单测（合成夹具，禁止回放派生）
`AlgorithmCompositionStateBehavior` / `AlgorithmCeilingEstimatorBehavior` / `AlgorithmCompositionPlannerBehavior` /
`AlgorithmCompositionMemoryBehavior`：含 env 关=行为逐字节一致断言。

## 7. 动态项 D1–D12 → 层/接口映射

| 项 | 说明 | 层 | 挂载点 |
|---|---|---|---|
| D1 多通道可达 | shop+discover+triple+spell+buddy | L2 | CeilingEstimator.reachByChannel |
| D2 升本视野耦合 | plannedTierHorizon 进可达性 | L1 | CeilingEstimator 输入 |
| D3 爆买流动性 | 金+可卖资产+在途经济 | L2 | 窗口与可达性 |
| D4 竞争/反制出口 | contestPenalty + anti-formation 路线 | L2 | Planner.V / 路线集 |
| D5 血量时钟 | clock 触发取代 血≤15 | L1 | CompositionState.clock |
| D6 阶段目标 | early=shadow/mid=commit/late=counter | L2 | Planner 分派 |
| D7 自适应K(DPP) | K=f(不确定性,健康,阶段) | L3 | 发散集选择 |
| D8 期权资产 | 对子/三连=转型流动性 | L3 | CompositionState 资产维度 |
| D9 记忆动态+复盘 | seenPassed/ownedLost/opponentSeen+归因 | L2(L1 存最小) | CompositionMemory |
| D10 幂等/预算 | 指纹缓存/early-exit | L1 | GetBestAction 集成 |
| D11 真实状态桶 | 回放语料第三桶 | L1 | P1 报告 |
| D12 成型率指标 | M5 成型率/成型回合 | L3 | 验收矩阵 |

## 8. 验收与评测（M 系列，全部附口径纪律：条件化/防截断/同 build）

| # | 指标 | 门槛 |
|---|---|---|
| M0 | 回归 | 100% 一致；legacy 零改动；env 关=原行为逐字节一致 |
| M1 | 可达性预测 vs 引擎真值 | 平均误差 ≤5pp，分层无系统偏 |
| M2 | 路线排序 vs 回放体系胜率 | Kendall τ ≥0.7（同 build，条件化 mid） |
| M3 | 破局缺口（崩盘-clock 桶） | 缺口>0 且显著于噪声（>同分位数），多 seed 稳定 |
| M4 | C-12 Shadow A/B | 300 场景 + owner 评审；稳态无 rescue 误伤（双向验证） |
| M5 | 成型率/成型回合对照 | policy A vs B，条件化 mid/late（L3 起正式） |

## 9. 稳定性与追踪

- 产物命名：`composition_templates.json(+sha)` · `runs/ceiling-gap-<seed>-<桶>.md` ·
  `M1-<日期>.json 残差` · 归因案例库（标 build/日期）。
- ISSUE：`L0-*`（数据）· `C-12`（组合层主项）· `Dx-*`（动态子项）。
- 问题定位顺序：结果不对 → 先 L0（确定性/模板/语料）→ L1（残差/分桶口径）→ L2（参数口径）→
  最后才疑引擎（先跑 M0 回归）。
- 稳定性：每层契约版本化；可整层回退；记录回填（ITERATION_LEARNINGS/HANDOVER）为硬环节；
  部署 SOP 只对已验收用户可见行为执行（C-12 验收前无部署）。

## 10. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 模拟器对手弱掩盖真实缺口 | D11 真实状态桶 + 更强/拟真对手对照 |
| 崩盘-clock/多通道参数化过度 | 先 L1 最小集（D2/D5/D10），分层放行 |
| 模板数值缺权威表（36.4.2 缺失） | ceilingPower=null、置信降级；只用结构/缺口口径 |
| 解析模型系统性偏差 | M1 残差监控；偏差>阈值降级不用 |
| 与并行迭代冲突 | ISSUE_TRACKER 闭环 + 审查 + M0 回归；分支 codex/composition-reasoning |

## 11. 实施顺序与门禁（Go/No-Go）

L0（0.5–1 天：模板归一45真ID+校验、门禁固化、语料就绪）→
**L1（3–5 天：P1 三桶 + C-12 最小版 + M0/M1/M3）** → 汇报 & Go/No-Go：
- 崩盘-clock 桶无显著缺口 → 收敛为"仅 Shadow 记录"，停 L2+；
- 有缺口 → L2（4–6 天，M2+Shadow A/B 300 场景）→ L3/L4 按证据立项。

## 12. 文档索引与关系

| 文档 | 角色 |
|---|---|
| 本文档 | **实施唯一权威入口** |
| LLD 2026-09-07-compositional-reasoning-layer-LLD.md | 代码锚点/实现细节/动态审查 §八§九§十（含可行性实验） |
| layers-priority 2026-09-07-*.md | 价值/成本核定表（§二） |
| theory-adaptation 2026-09-07-*.md | 规则×环境×胜率 数学依据 |
| research 2026-09-07-compositional-memory-breakthrough-plan.md | GitHub 调研与总纲 |
| 08-31 rollout-understanding-core.md | 三层架构 + 组合推理增补（历史） |

## 附：勘误合并（相对早期文档的一致性修正，实施勿再犯）
1. SimulationEngine 路径含 `simulation/` 一层；probe/steer 均在决策循环块（probe ~L710-728，steer ~L730）。
2. 中文 23 体系的真实卡 ID 源 = guide_weight_reference.json（officialCardId）；comp_strategies 中文条目是占位。
3. 崩盘定义从"血≤15"升格为 clock（D5）；血≤15 仅兜底。
4. "全局 steer"已被可行性证伪 → 只做 rescue 语义。

## 附2：实施进度记录（2026-09-08 01:1x, 实施会话回填）
**L0 已完成**: composition_templates.json 对齐 §4.1 全 13 字段(45条; 中文23真卡ID来自 guide_weight_reference);
生成器 tools/model/build_composition_templates.js(经 contracts.js 数据根/契约头校验); commit f96a205 + 716afad。
并行会话的 tools/simulation/_build_composition_templates.js 曾产出 23 中文空 engine(占位ID bug)→ 已用正确实现覆盖, 坏文件备份 .bad-empty-23cn.json。

**P1 已完成(工具+证据)**: 
- 工具 tools/simulation/ceiling_gap.js(--ceiling [--game-idx N]): steer 全程替换购买决策(保留节奏项)+崩盘-clock(min-hp 口径)+crash 桶报告
- **确定性修复(关键)**: 组装时曾丢失 rollout_probe 的 R146 Math.random 种子化块 → 引擎裸随机致特定局漂移;
  补块后 seed7/42/111 × 6局 全确定。教训: 派生工具必须完整复制随机种子化加载骨架。
- crash-clock 口径: expTurns 用历史最低血线(排除死亡快照); RESCUE 需 natural≥4(自然已差)
- **证据(CEILING_GAP_EVIDENCE_P1_20260907.md)**: 3seed×6局 rescue 成功率 2/6(33%); 全局 steer 证伪(自然优局普遍劣化);
  **pivot A/B 反直觉**: 崩盘局"卖旧换体系"全劣化(G4 无pivot=1→有pivot=8), rescue 应补强板面非激进转型

**Go/No-Go 状态(§11)**: L1 证据显示 rescue 缺口真实但罕见且特定(33%, 依赖崩盘时商店可转该体系);
多数崩盘局 steer 救不了(太晚)。L2 启动条件未完全满足(需更多 seed 扩样 + M2 路线排序 + rescue 动作语义细化[D4/D6])。
C-12 插件实现(CompositionState/CeilingEstimator/CompositionPlanner)可作为 Shadow 记录先行(P2 §6), 300 场景门禁前不改变可见推荐。

**引擎多局连跑边界**: rollout_probe/ceiling_gap 多局单进程连跑仍会卡死(sharedPool/模块单例残留, 非 Math.random);
可靠采集 = 每局独立进程(已验证)。ISSUE I-20260907-25 已关(原疑引擎幂等, 实为工具缺种子化; 多局连跑残留另记)。

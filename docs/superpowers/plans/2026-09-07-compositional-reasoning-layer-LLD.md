# 组合记忆 + 上限/破局推理层 — 实施级 LLD 规划（自验证版）

日期：2026-09-07
性质：**供另一会话实施**的详细规划。本文档每个关键假设都经本会话在代码/数据中验证过，
证据见 §六"自验证记录"。实现前若发现与证据不符，以实际代码为准并回填本文档。
上游：`2026-08-31-rollout-understanding-core.md`（09-08 增补）+ `2026-09-07-compositional-memory-breakthrough-plan.md`（调研）。

## 〇、目标（可验收表述）

在不改动 legacy 可见行为的前提下，新增一条**决策前组合推理**能力：
对每个局面，输出"候选体系 ×（成型度/缺口/可达上限）→ 破局动作优先级"，并在模拟器侧
先用 ceiling-gap 量化"当前策略离可达上限差多少"，证明该能力有提升空间后再进 Shadow。

## 一、范围与分阶段（实施顺序）

| 阶段 | 交付 | 运行环境 | 门禁 |
|---|---|---|---|
| P0 | `composition_templates.json` 规范化（schema 见 §三） | 纯数据 + 校验脚本 | 覆盖校验 fail-closed；不碰模拟器/插件行为 |
| P1 | 模拟器 ceiling-gap 度量工具（§四） | tools/simulation | 确定性检查；结果只作定位不作上线声明 |
| P2 | C# 组合推理层（§五）→ **C-12** shadow 候选 | src/BobCoach（legacy 零改动） | 单测+代码审查+回归100%+300场景 |
| P3 | CompositionMemory 跨回合 + 对局复盘归因 | 同上 + 回放管线 | 归因与 V 系列一致 |

## 二、关键设计决策（已验证可落地）

1. **组合数据三源归一**：模拟器 `comp_strategies.json`(45, 结构化) ∪ 插件
   `season14_comp_guides.json`(23, T0 分层) ∪ `META_STRATEGY_LIBRARY.md`(社区, 带日期/版本)。
   → P0 归一成一份版本化 `composition_templates.json`，C# 与 JS 两侧共用（契约头）。
2. **Bob 方向引导不用新造**：模块栈已存在 DivergentPlanner（env `BOB_DIVERGENT*`），挂
   `ctx.divergentPlan`（延续/协同/卡池/保命/高本 5 类路线）。P1 用它当"组合方向"注入载体（作
   **测量工具**；R599 生产调制曾劣化≠本工具用途）。
3. **C# 插入点固定**：`DecisionEngine.GetBestAction` 在 mainline Analyze(L484) → memory
   Observe(L499) 之后、R0 规则瀑布之前；与 synergyTribeBuy(~L510) 同层，env 默认关
   （对齐现有 `BOBCOACH_*` 候选模式）。
4. **数据边界**：模板用机制字段与角色标签，**数值上限标 unknown/待校准**（36.4.2 权威表缺失），
   不发明新赛季卡/权重；全部保持 Shadow。

## 三、P0 — composition_templates.json 契约

文件：`E:\HDT-BobCoach-evidence\knowledge-base\composition_templates.json`（证据库，不入库）
契约头：`{schemaVersion, season, gameBuild, source:[{name,path,buildOrDate}], capturedAt, sha256}`
条目（每条体系）：
```
{
  "compId": "beast_self_damage",          // 沿用 comp_strategies.compId
  "nameCn": "野兽自残流",
  "provenance": { "src": "comp_strategies|comp_guides|meta_library", "patch": 235290, "tier": "C|T0|..." },
  "tribes": ["野兽"],
  "direction": "engine",                   // engine(引擎卡) | poison(毒) | stats(数值) | econ(经济爆发) | tech(反制)
  "engineCardIds": ["BG29_807","BG26_801","BG_LOE_077","BG25_354"],  // status=CORE+ADDON 去重
  "missingLinkType": ["pair_triple","engine","spellgen","economy"],  // 距成型常见缺口类型
  "ceilingPower": null,                    // 数值位: 权威卡表到齐前必须 null(=unknown, 降置信)
  "endgame": { "tier": 5, "template": ["引擎位","引擎位","增幅位",...] },
  "counters": ["毒/圣盾体系..."],           // 反制关系, 仅当有据(攻略/回放)时填
  "activation": { "requires": "重金属双头飞龙+狂乱的猎豹在场", "windowTurn": 8 }  // 有据才填
}
```
- 映射脚本：`tools/model/build_composition_templates.js`（只读三个数据源 + 写校验报告：
  每 comp 至少 1 引擎卡、无重复、字段枚举合法）。
- 校验：覆盖数、唯一性、契约头；与真实终局板面匹配率（回放 final-boards 语料, 标注 build）作为
  参考报告, 不作门禁。
- **不要**：改动 cards.json/池/机制/任何模拟器行为。

## 四、P1 — ceiling-gap 度量（模拟器侧, 扩展 rollout_probe）

### 4.1 引擎小改（1 处, 锚点已确认）
`tools/simulation/simulation/SimulationEngine.js`（注意实际路径含 `simulation/` 子层）决策循环
（L710-734, 2026-08-31 加的 rollout probe 块内）增加**全对局引导钩子**：
```js
// 已有: collect/force 仅在 dl==0 且 turn==target
// 新增: this._rolloutSteer = function(p, ctx, turn, sharedPool) -> decisions|null
// 在 _applyDecisions 前: if (this._rolloutSteer && p.aiType==="bob") { var sd = this._rolloutSteer(...); if (sd) decisions = sd; }
```
- 无 steer 时零行为影响（与现有 probe 同风格, 守卫 `this._rolloutSteer` 为真才动）。

### 4.2 引导策略（steer 函数, rollout_probe.js 内实现, 不碰生产模块）
- `steerTemplate(compTemplate, tier, gold)`: 每回合决策被替换为——
  ①优先买 compTemplate 内 status=CORE 且板面缺的卡；②有对子→留/买第 3 张；
  ③无 CORE 可买→按该 direction 的启发式（engine: 买放大器类; econ: 保金升本; stats: 买高身材同族）；
  ④该升本的时机沿用 leveling 决策。= "全程只围绕体系 X 打"的确定性反事实轨迹。
- `steerLockTribe(tribe)`（可选复现对照）与 `steerNone`（=基线自然策略）。

### 4.3 运行流（复用现有 collect/rollout/报告骨架）
1. collect（正常策略）→ 决策点（T4/T5/T7/T10 等）;
2. 对每个决策点 × 引导目标 X ∈ {自然, 候选体系 A, 候选体系 B}（X 集合 = 该局面 CompMatcher
   判 top-3 体系 + 攻略标注体系, 从模板库取）→ 同 seed 重放 + 全局 steer → 终局名次;
3. 报告：每决策点"自然名次 vs 体系X名次"，聚合 = **ceiling-gap**（哪些局面换体系能显著更好=
   破局缺口坐标）。输出 rollout-report 同款 md/json。

### 4.4 验证（P1 完成标准, 跑给证据）
- `--diag-repro` 3 连跑一致; 确定性检查 0 不一致（卡表快照已内置）;
- seed 集 ≥3（42/7/111…）, 决策点 ≥20, 覆盖 前中后期与至少 3 类体系 direction;
- 报告含逐点与聚合表。输出目录：`evidence/.../runs/ceiling-gap-<seed>`。
- **不做**：把 steer 结果当策略改动的依据；不声称真实强度提升（模拟器量级）。

## 五、P2 — C# 组合推理层（C-12, Shadow）

### 5.1 新增文件（放 `src/BobCoach/Core/`）
1. `CompositionState.cs` — 从 GameState 推断每候选体系成型度：
   `{CompId, HasEngine(int 引擎卡在板数), CorePresent, MissingCoreIds, PairProgress,
   TripleReady, SpellGenReady, EconReady, StartWindowTurn, Confidence}`
   - 特征用机制/角色（复用 CardClassifier 的 economy/amplifier + SynergyEngine 亲和）,
     不硬编码新卡。
2. `CeilingEstimator.cs` — `Estimate(GameState, CompositionState, compTemplate) -> {CeilingPower?, GapType[]}`；
   数值上限无权威数据时 `CeilingPower=null, Confidence 降`（fail-soft）。
3. `CompositionPlanner.cs`（=BreakthroughSearch）—
   `Plan(state, compStates, templates) -> CompositionPlan{RecommendedComp, CeilingDelta(定性),
   PriorityActions[2..3], WindowDesc}`；
   内部 = 缺口类型 → 近 2 回合动作候选（买缺口引擎 / 攒对子 / 升本窗口 / 触发法术生成器 /
   饰品配合——全部来自既有候选枚举子集, 不新造动作类型）。
4. `CompositionMemory.cs`（P3 起接线）— 扩展 StrategicMemory 语义：跨回合 `CurrentCompId/
   MissingEngineIds/PairProgress/LastPlanHash`, 老化上限(容量常量, 参考 StrategicMemory 的
   MaxObserved* 模式)。

### 5.2 接线（legacy 零改动）
`DecisionEngine.GetBestAction`：在 mainlineAnalyze/Observe 之后、`synergyTribeBuy` 块旁加：
```csharp
// C-12 组合推理(默认关): env BOBCOACH_COMP_PLAN=1 启用(对齐 BOBCOACH_* 候选模式)
if (compPlanEnabled && compPlan != null && compPlan.Confidence >= threshold
    && compPlan.PriorityActions.Count > 0) {
    // 仅当最高置信体系的动作优先级显著高于当前规则 top(防抖动) 才提前返回;
    // 否则降级为只写 Shadow 记录(ComparisonSink/决策日志)不改变返回
}
```
- **Shadow 记录**：无论是否提前返回，都把 `CompositionPlan`（推荐体系/动作/置信）写入决策事件
  （复用 DecisionEventRecorder / 现有 shadow 采集）, 供 V 系列口径离线分析。
- 阈值/λ 常数放类顶部并支持 env（`BOBCOACH_COMP_PLAN_CONF`），默认 0.6+。

### 5.3 单测（镜像现有 Algorithm*Behavior.cs 命名；夹具仅合成, 遵守 tests/fixtures 纪律）
`AlgorithmCompositionStateBehavior.cs`（成型度推断/引擎计数/缺口枚举边界）
`AlgorithmCeilingEstimatorBehavior.cs`（null 上限 fail-soft/模板缺失降级）
`AlgorithmCompositionPlannerBehavior.cs`（缺口→动作映射/置信门槛/legacy 等价性: env 关=现行为逐字节一致）
`AlgorithmCompositionMemoryBehavior.cs`（跨回合进度/老化/Reset）

### 5.4 代码审查清单（按 AGENTS 修复后审查硬环节）
①启发式/门控双向验证误判（推荐体系 ≠ 玩家实际, 留帧证据） ②C# 与模拟器/模板 JS 侧 schema 一致
③回归防线（上述单测 + 现网 100% 回归：env 默认关 → 行为不变） ④记录回填。

## 六、自验证记录（本会话逐项核对, 附证据）

| # | 规划假设 | 验证证据（2026-09-07） | 结论 |
|---|---|---|---|
| V1 | comp_strategies.json 存在且字段可用 | `season14-data/comp_strategies.json`: 45 条; 字段 compId/name_cn/powerLevel/forcedTribes/cards[{cardId,status:CORE/ADDON/CYCLE,finalBoardWeight}]/tips[{whenToCommit,date,patchNumber}]/freshness | ✓ 模板基础已存在 |
| V2 | 插件侧 comp 数据在证据库 | `evidence/season14/generated/registries/season14_comp_guides.json`: 23 条, {id,tier:T0,tribes,coreCards(文本),trinkets,strategy}; 由 `src/BobCoach/Core/CompGuideCatalog.cs`(注意: 在 Core\ 非 Core\Decision) 经 SeasonDataSelector().ResolveConfiguredRoot() 加载 | ✓ |
| V3 | C# 无组合/上限/破局概念 | 检索 Ceiling/上限/天花板/Breakthrough/破局/Composition/成型度 = 0 命中(仅 StrategicPlan.TechPivot 启发式) | ✓ 缺口真实 |
| V4 | StrategicMemory 是单卡集合非进度 | `Core/Decision/StrategicMemory.cs`: Snapshot{ObservedCoreCardIds,ObservedPairCardIds,LastBoardSignature,...} | ✓ 需扩展为组合进度(P3) |
| V5 | GetBestAction 插入点 | `Core/DecisionEngine.cs`: L480-520 = Enumerate→mainline.Analyze(484)→memory.Observe(499)→synergyTribeBuy 提前返回(~510)→R0 瀑布; 计划仅少数点被消费(3022 首步/2612 核心保护) | ✓ C-12 插在 ~510 层 |
| V6 | Bob 无 lockedTribe; 方向引导载体=DivergentPlanner | BUY_TRACE/lockedTribe 仅存在于 `simulation/HeuristicAI.js`(对手); `modules/DivergentPlanner.js` 提供 5 类路线挂 ctx.divergentPlan, env BOB_DIVERGENT*, 默认关; R599 调制曾劣化 | ✓ P1 用 steer 而非改生产模块 |
| V7 | rollout 探针钩子可扩展 | `simulation/SimulationEngine.js` 2026-08-31 已加 collect/force(afterDecisions/afterCombat/pairs/combatResult/allPlayers), 守卫 this._rolloutProbe | ✓ 4.1 加 _rolloutSteer 同锚点 |
| V8 | 确定性前提成立 | diag-repro 3 连跑一致; diag-full 0 差异; phase1 6 局 0 不一致; runOneGame 卡表快照恢复已内置 | ✓ |
| V9 | C# env 候选模式 | DecisionEngine 顶部 BOBCOACH_SELL_MULT/O1_SUPPRESS/EFFECT_LAMBDA/UPGRADE_* 默认关, A/B 用 env | ✓ C-12 同款 |
| V10 | 测试命名/夹具纪律 | tests/Algorithm*Behavior.cs 模式; fixtures 必须合成(AGENTS) | ✓ |
| V11 | rollout phase1 已有产出 | `evidence/.../runs/rollout-phase1-seed42`: 10 点 4 分歧, 确定性 0 不一致 | ✓ P1 在其上扩展 |
| V12 | META_STRATEGY_LIBRARY 存在 | `evidence/simulation-season14/knowledge-base/META_STRATEGY_LIBRARY.md`(带日期/版本) | ✓ |
| V13 | 权威 36.4.2 卡表缺失 → 数值位 null | AGENTS 版本注记: 36.4.2 全量卡表缺失(hearthstonejson 404) | ✓ 上限数值标 unknown |

未验证/需实施者确认项（清单）：DecisionEngine 中 C-12 返回前需确认 `synergyTribeBuy` 与规则层
是否会二次吞掉推荐(实现时读 L510-700 全段); CompGuideCatalog 权重文件 `season14_guide_weight_
reference.json` 的字段与 CeilingEstimator 复用方式; DivergentPlanner 对 bob 的 ctx 注入在 `_runBobCoach`
实例化顺序中的位置(modules 数组 push 顺序, SimulationEngine.js L1835 附近)。

## 七、交接要点（实施会话）

- 分支 `codex/composition-reasoning`; 先 ISSUE_TRACKER 登记 C-12; P0/P1 不入插件, P2 起 legacy 零改动
  + 100% 回归; 完成后按部署 SOP 记录回填（本阶段无可部署用户可见行为=不回填部署, 只回填候选状态）。
- 复现命令（实施自测用）:
  `node tools/simulation/rollout_probe.js --data-dir <season14-data> --output <out> --games 6 --seed 42 --diag-repro`（确定性）
  `node tools/simulation/rollout_probe.js ... `（P1 ceiling-gap 扩展后)
  `dotnet test`（P2 单测）
- 记录更新: ITERATION_LEARNINGS.md / HANDOVER / 本文件回填"已实施"列。

## 八、实施会话勘误回填（2026-09-07 22:4x 实施会话核验后追加）
以下为实施会话按 LLD §六"需实施者确认项"逐一核验的修正/补全（以实际代码为准）：
1. **SimulationEngine.js 路径**: LLD 四.1 写 tools/simulation/SimulationEngine.js；实际 = tools/simulation/simulation/SimulationEngine.js（多一层 simulation/）。probe 块实际在 L710-734（决策循环内、_applyDecisions 前; collect 仅 dl==0 且 turn 属于 turns, force 替换 decisions）。P1 steer 钩子加在同块: 命中 turn 且 _rp.steer 为函数时, 用 steer(p,ctx,turn,sharedPool) 返回值替换 decisions。
2. **rollout_probe 挂载**: rollout_probe.js L554 SimulationEngine._rolloutProbe = probe; probe 对象已含 collect/force/afterDecisions/pairs/combatResult/afterCombat。steer 加进同一 probe 对象即可。
3. **中文 23 体系的结构化卡 ID 源 = season14_guide_weight_reference.json**（LLD V2 提到 weights 文件但未列路径）: 实际路径 evidence/season14/generated/season14_guide_weight_reference.json（149KB, 不在 registries/ 子目录）; 其 guideReferences[23] 已含每体系 coreCards 数组（name/count/officialCardId）+ trinkets + tier + tribes + twoCopyCoreCards —— 这是 P0 中文体系的主数据源。而 comp_strategies.json 的 23 个中文 compId 条目是占位（cards.cardId 为 "1".."6", 无真实卡 ID）, 22 个英文 compId 条目才有真实卡 ID。
4. **CompGuideCatalog 加载依赖**: 需 guides + weights 两文件; weights 读 ConfiguredRoot/generated/season14_guide_weight_reference.json（非 registries 子目录）。decision-data root 下缺 weights → P0 后需同步该文件到 decision-data/generated/（I-20260905-10 同族教训）。
5. **cardPoolIdentity**: comp_guides._meta 含 cardPoolIdentity=0bcc979ae21df891（36.2.248348 卡池快照）; guide_weight_reference 无 _meta（用 generatedAt/season）。P0 契约头用 comp_guides 的 meta 值, source 数组逐一标注。
6. P0 归一主键 = guideId（中文23）与 comp_strategies.compId（英文22）→ 模板总数 45; 中文 23 从 guideReferences 取 engineCardIds, 英文 22 从 comp_strategies 取; 若合并数与预期不符则校验失败（报告差异, 不自动吞）。

## 九、最小可行性研究（2026-09-07 实跑, 证据 → 方案走向修正）

### 实验
- 工具：`tools/simulation/ceiling_gap.js --ceiling`（确定性重放 + steer 全程替换购买决策, 保留节奏项）。
- 模板：P0 生成器 `tools/simulation/_build_composition_templates.js` → `evidence/knowledge-base/composition_templates.json`
  （45 套契约头；§八 勘误后占位 ID 已过滤, 22/45 有真实卡 ID；中文 23 待 guide_weight_reference 归一[P0 待办]）。
- 运行：seed 42 × 6 局 × {自然 + steer: 元素球/野兽猛犸/鱼毒/亡灵溢出}（4 体系 engine 卡均为真实 ID, steer 真实生效）。
- 原始报告：`evidence/simulation-season14/runs/ceiling-gap-mvr-seed42/ceiling-gap-42.md`。

### 结果（名次越小越好）
| G | 自然 | 元素球 | 猛犸 | 鱼毒 | 亡灵溢出 |
|---|---|---|---|---|---|
| 0 | 2 | 7 | 8 | 6 | 6 |
| 1 | 1 | 1 | 4 | 4 | 3 |
| 2 | 1 | 1 | 2 | 2 | 3 |
| 3 | **8** | **3** | 8 | 7 | 8 |
| 4 | 1 | 7 | 8 | 8 | 2 |
| 5 | 1 | 3 | 1 | 4 | 4 |
- 自然平均 2.33（6 局 4 次前二）→ **聚合缺口很小**；盲目全局 steer 大多劣化（发散/窄池有成本）。
- **例外 = G3**：自然崩盘(8) → 元素球 steer=3、鱼毒=7：**缺口只出现在"自然轨迹失败"局**, 非全局恒有。

### 可行性结论（决定方案走向）
1. **方法可行**：模板 + 确定性 steer 反事实链路端到端跑通（M3 方法成立, 可扩展）。
2. **全局"朝体系 X 引导"（聚合口径）证伪**：默认对手环境里自然策略已接近可达上限,
   无脑 commit = 窄池/发散成本 > 收益（与 R577 类结论一致）。**不要做全局 steer。**
3. **缺口是条件性的 = 破局窗口**：仅自然失败/停滞局存在（G3 +5 名）。→ **重定向**：
   组合层只在 **rescue/pivot 条件触发**时干预（复用 powerTrend/desperate/血线检测），
   平时只写 Shadow 记录不改变返回。
4. **口径**：样本小（n=6 单 seed 流, 默认对手）；扩展需 多 seed + 分桶（全样本 vs
   崩盘/停滞子集）+ 更强/拟真对手环境, 否则缺口被环境优势掩盖。

### 据此修订 P1/P2（实施时生效）
- P1 报告**分桶双口径**：全样本平均 + 崩盘/停滞子集；崩盘定义 = 血≤15 ∥ rank 落后 ∥ powerTrend 连续下滑。
- P2 `CompositionPlanner` 输出 `mode: rescue | optimize | shadow`：rescue 才可能影响返回, 其余只记录。

## 十、全面动态性审查与完善（2026-09-07）

> 静态模板 + 可达性 + rescue 触发是骨架, 但战棋是强动态博弈。逐类核对"游戏内动态情况",
> 发现 12 项遗漏/弱化。原则:**可解析的动态(池/时钟/预算/对手观测)进规则层; 整局联合动态
> (level×buy×comp 同时优化)留给 Phase4 价值学习+rollout, 不在 P2 硬塞超预算优化。**

### 动态遗漏清单（编号 → 修改 → 落地/优先级）
| # | 游戏内动态 | 现有缺口 | 修改 | 落地 | 优先级 |
|---|---|---|---|---|---|
| D1 | **获取通道异质**：核心卡不只靠商店——发现(三连奖励升1星池/技能/法术)、伙伴、饰品产出 | P_reach 只按商店超几何 → 低估高本核心 | P_reach = 商店P + 发现通道×权重 + 三连路径(对子已握则升)；通道参数版本化 | P1 模型扩展 | 建议 |
| D2 | **升本×成型的联合**：5/6星核心要 5/6本才进池 | 把升本与成型分开决策, 视野错 | CeilingEstimator 输入 `plannedTierHorizon`（当前+规则级计划 R0 曲线推）；破局动作含"提前升本开窗"且受血线约束 | P2 | **必做** |
| D3 | **经济爆发回合**：金上限10、靠理财/卖板/技能制造"爆买回合"；commit 常发生在那天 | "看到就买"模型忽略负担能力 | 可达性加 affordability/流动性(金+可卖资产+在途经济)；窗口按"可爆买回合"计时 | P1/P2 | 建议 |
| D4 | **对手动态**：对手买走你的引擎→共享池稀薄(可观测)；领先者成型→要**反制出口**而非继续成型 | V(route) 无 contest；路线集无 anti-formation | V 减 contest(同引擎对手数加权, 战斗可观测)；路线集加反制出口(复用 counter_tags/R708)；rescue 分"补成型 vs 转 tech" | P2 | 建议 |
| D5 | **血量时钟**：伤害随回合与存活星递增；危险=剩余回合 vs 还需回合, 非血≤15 | 静态触发误判(早/晚) | 触发改 clock = 预计剩余回合(血/期望伤) < 成型还需回合；commit/rescue 带截止（deadline-aware stopping） | P2 | **必做** |
| D6 | **阶段目标切换**：Early=曲线/经济, Mid=commit, Late=上限+反制; 早期谈"成型率"是错目标 | 无显式阶段目标 | 按 TurnPhaseEngine 阶段出目标：Early 仅 shadow / Mid commit+rescue / Late counter+上限替代；模板加阶段适用域 | P2 | 建议 |
| D7 | **发散宽度应自适应**：K 随不确定性/健康/时间/池余下降, commit 后 K=1 | K 常数 | K = f(不确定性, 健康, 阶段)；DPP 阈值化触发不每回合重算 | P3 | 可选 |
| D8 | **对子/三连的期权价值**：对子不绑体系=转型流动性；三连奖励=免费定向高本 | 只把对子当体系内进度 | CompositionState 加资产维度(对子集/三连进度/发现计数)；对子优先级含通用价值不唯体系论 | P2 | 可选 |
| D9 | **记忆动态**：见过&放过的核心(被抢概率↑)、已买已卖/已死引擎不算拥有 | 只记"观测过" | CompositionMemory 记 {seenAndPassed, ownedNow, ownedLost, opponentSeen}；P_reach 贝叶斯更新池估计 | P3 | 建议 |
| D10 | **实时预算与重入**：GetBestAction 一回合多次调用(1365/1376/1387 史例)、~ms、结果须幂等 | 在线重计算/翻转风险 | 同回合状态指纹→缓存结论；先规则后组合 early-exit；DPP/rollout 仅离线/Shadow；审查清单加"重入幂等" | P2 | **必做** |
| D11 | **验证动态**：模拟器默认对手弱→gap 被掩盖; 真实 lobby 更凶 | P1 只用模拟器采样状态 | P1 增**真实决策点桶**：回放语料(登记册, 同 build) 的 mid-game 状态做同 seed 反事实（离线路评链已具备），分桶上报 | P1 | **必做** |
| D12 | **成型率直接度量**：名次会掩盖成型差异 | 无"成型率/成型回合"指标 | 增 M5：固定视野 P(核心齐)+成型回合分布 (policy 对照, 条件化 mid/late) | 全阶段 | 可选 |

### 修订汇总（替换前文对应条目）
- §九"崩盘定义" 由 `血≤15` 升格为 **D5 clock 判定**（血/期望伤 < 成型还需回合），血≤15 仅作兜底。
- P1 分桶从 2 桶 → **3 桶**：全样本 / 崩盘-clock 子集 / 真实状态子集（D11）。
- P2 `CompositionPlanner`：mode + **阶段目标（D6）** + **contest/反制感知（D4）** + 幂等缓存（D10）。
- 代码审查清单追加：①重入幂等 ②rescue 双向验证（崩盘局命中 vs 稳态误伤, 留帧）③contest 观测口径。
- 验收 M 系列追加 M5（D12）。M1 扩展校验多通道可达性（D1）。

### 结构性边界（诚实声明）
- "全局动态最优（level×buy×comp 联合）"不能靠规则穷尽；本方案把**可解析动态**落规则层(P2),
  **不可解析的整局联合动态**明确留给 Phase4（rollout 标签价值学习 + 束搜索/深度规划）。
  P2 若试图一次覆盖 D2+D3+D4+D6 会变成超参数泥潭 → 分步：先 D2/D5/D10/D11（骨架正确+工程安全），
  再 D1/D3/D4/D6/D9（模型细化），D7/D8/D12 后置。

# 组合记忆与上限/破局推理 — GitHub 调研结论与方案规划

日期：2026-09-07
前置：用户判定"算法缺乏有效记忆、无法关联机制/卡牌，复杂局面想不出快速提战力、够到上限的破局动作"；
配套设计见 `2026-08-31-rollout-understanding-core.md` 的 "2026-09-08 增补：组合记忆 + 上限/破局推理层"。
本文件 = GitHub/业界调研结论 + 结合本项目现状/资源的落地规划。

## 〇、结论摘要

1. **公开开源里没有可直接抄的"战棋全流程强 agent"**（商店运营+经济+战斗+阵容上限一体）。
   BG 开源项目只覆盖三类：战斗模拟器、战绩/数据工具、实验性 RL/LLM 教练，均未到可部署强度。
   → "组合记忆/破局推理"在业界也没有现成解，**必须自建**（组合推理层），没有偷懒捷径。
2. 但有**一批模式与数据资产可借鉴**：DeepBattler（LLM 教练的产品/状态组织）、
   SAGA / AgenticSTS（跨回合目标记忆 + 终局复盘归因的架构模式）、TFT MuZero（价值学习方向）、
   bgknowhow / xinyao27（数据管线）、战斗模拟器（可做我们 CombatResolver 的校验基准）。
3. **我们的确定性全流程模拟器是稀缺资产**（8 人经济+商店+机制，且 2026-08-31 修复了卡表污染
   保证同 seed 可复现）——业界开源模拟器要么只做战斗、要么过时/实验性，无人拥有我们这条
   "可复现全流程 → rollout 反事实"的验证链。规划要围绕保住并用足这条链。
4. 落地路径 = 先在确定性模拟器**量化"策略离可达上限差多少"（ceiling-gap 证据）**，
   再进插件 Shadow（C-12 候选），全程遵守数据边界与门禁（Shadow / legacy 兜底 / 300 场景 / 代码审查）。

## 一、GitHub/业界调研（证据，均为 2026-09-07 检索核实）

### 1.1 Hearthstone 战棋模拟器类（战斗/结算为主，无运营决策开源实现）
| 项目 | 语言/活跃 | 是什么 | 借鉴点 | 为何不直接用 |
|---|---|---|---|---|
| [twanvl/hearthstone-battlegrounds-simulator](https://github.com/twanvl/hearthstone-battlegrounds-simulator) (60★) | C++, 2019 | 战斗结算模拟器 | 战斗结算口径对照 | 只覆盖战斗，无商店/经济/运营；旧卡池 |
| [yossielimelech/BGSimulator](https://github.com/yossielimelech/BGSimulator) (25★) | C#, 2020 | "DL simulator"（即本项目早期 HANDOVER 提到的 BGSimulator 参考） | 无（已核实为实验性战斗向） | 2020 后停更；无全流程 |
| [janvdvegt/Ghastcoiler](https://github.com/janvdvegt/Ghastcoiler) (7★) | Python, 2026-04 仍维护 | Python 战斗模拟器 | 无 | 战斗向 |
| [utilForever/battlegrounds-rs](https://github.com/utilForever/battlegrounds-rs) (12★) | Rust, 2022 | Rust 战棋模拟器 + RL | 无 | 停更 |
| [LaunchGemini/Neural-BGSimulator](https://github.com/LaunchGemini/Neural-BGSimulator) | C#, 2024 | 面向 DL/RL 的模拟器 | 无 | 0★ 实验性 |

### 1.2 Agent/学习方向（均未达强度，验证"无现成强 agent"）
| 项目 | 说明 |
|---|---|
| [miaostudy/BobsDaddy](https://github.com/miaostudy/BobsDaddy) | RL agent 尝试玩战棋（1★，实验性） |
| [esterRozen/SuperAutoPetsAI](https://github.com/esterRozen/SuperAutoPetsAI) | 自走棋（Super Auto Pets）RL，小型 |
| [SaeedAnas/TFTMuZeroAgent](https://github.com/SaeedAnas/TFTMuZeroAgent) | MuZero 打 TFT——**唯一接近"自走棋价值学习"的方向参考**；仍未达可部署强度 |
| [galacticglum/csc111-course-project](https://github.com/galacticglum/csc111-course-project)、[GurvanR/BGReimplementationAndRL](https://github.com/GurvanR/BGReimplementationAndRL) | 课程/个人实验，从零重写+RL，无结果级产出 |

### 1.3 LLM 教练 / 数据 / 工具类（产品与信息架构参考，非决策引擎）
| 项目 | 说明 |
|---|---|
| [william-Dic/DeepBattler](https://github.com/william-Dic/DeepBattler) (303★) | **LLM 战棋教练/伴侣**（C#）——证明"战棋 AI 教练"有社区需求，竞品定位参考；我们实测 LLM 判分不可靠 + 离线约束，故只借鉴其"把局面整理给上层/解释层"的信息架构，**不做 LLM 决策** |
| [xinyao27/hearthstone-battlegrounds-tools](https://github.com/xinyao27/hearthstone-battlegrounds-tools) (296★) | 战绩统计 + "最拿手英雄"数据分析（TS）——数据管线形态参考 |
| [IBM5100o/HDT_BGrank](https://github.com/IBM5100o/HDT_BGrank) (71★) | C# HDT 插件（2026-08 仍活跃）——印证 HDT 插件生态 |
| [O-Nemet/bgknowhow](https://github.com/O-Nemet/bgknowhow) | BG 数据/网站（JSON 提供方）——外部 comp/数值数据源候选（需版本核对纪律） |
| [MrBridgeHQ/tft-mcp](https://github.com/MrBridgeHQ/tft-mcp) | TFT 的 MCP server：把 comps/装备/排行数据喂给 AI——**"组合数据作为数据层喂给上层"的架构与我们 Phase 0 一致** |
| [utilForever/RosettaStone](https://github.com/utilForever/RosettaStone) | 常规天梯 Hearthstone C++ 模拟器 + RL（非战棋）——历史参照，非战棋 |

### 1.4 方法与文献（记忆 + 复盘模式，来自通用长程游戏 AI 研究）
| 来源 | 借鉴点 |
|---|---|
| [SAGA: Scene-Aware, Goal-Evolving Agents for Long-Horizon Strategy Planning](https://arxiv.org/abs/2606.29932) | **场景感知 + 目标随局面进化 + 终局结构化复盘（GameAnalyst 归因成败）** —— 正是我们要的"跨回合目标记忆 + 对局后归因反哺"模式 |
| [AgenticSTS: Bounded-Memory Long-Horizon LLM Agents](https://arxiv.org/abs/2607.02255) | 有界记忆的组织方式（显式状态+检索+遗忘边界）——为 CompositionMemory 的容量/老化设计提供参考 |

## 二、与本项目现状对照

### 已有资产（可直接复用）
- 确定性 8 人全流程模拟器（tools/simulation，含经济/商店/机制/战斗；2026-08-31 修复卡表污染 = 同 seed 可复现前提）+ C# CombatSimulator。
- 插件侧 v1.2.19：规则瀑布 + classifier（角色/经济/放大器）+ SynergyEngine + CompGuideCatalog + MainlineStrategyPlanner + StrategicMemory + MCTS + **成熟 shadow 基建**（UnifiedActionScorer、C-01~C-11 候选、V 系列证据、回放-反馈闭环、分层审计纪律）。
- 工具：rollout_probe（阶段 1 确定性反事实）、Power.log 行为分析、离线路评工具链、回放登记册。
- 数据：comp_strategies / CompGuideCatalog / META_STRATEGY_LIBRARY（版本化攻略库，需标注 build/日期）、回放语料（标注 build）。

### 缺口（代码证据，详见 08-31 计划 09-08 增补）
- 记忆 = 单卡观测集合 + 3 回合战力趋势，**不是组合进度状态机**。
- 关联停在 卡↔角色/种族 浅层；无 "机制×卡×局势→破局窗口" 组合算子。
- 决策主路径 = 规则瀑布 early-return；**无"本板面上限 vs 转型上限"模型** → 无破局依据。

## 三、方案规划（分阶段，每阶段给产出/验证/门禁/不依赖项）

### Phase 0 — 资产盘点与"版本化组合模板"规范化（插件/模拟器外，1-2 天）
- 把 comp_strategies / CompGuideCatalog / META_STRATEGY_LIBRARY + 社区攻略（标注来源/日期/版本）
  统一成 `composition_templates.json`：契约头（schemaVersion/season/gameBuild/source/capturedAt/sha256）
  + 每模板 {compId, 引擎卡, 成型终局模板, 终局战力上限估计, 关键缺口类型, 触发/启动条件, 反制关系}。
- 全部用**机制字段**表达，不依赖新赛季精确数值（数值位可标 unknown/待校准）。
- 产出：json + 覆盖校验脚本（fail-closed）。验证：与真实 final_boards（回放）匹配率报告。
- 门禁：无模拟器/插件行为改动；数据边界合规。

### Phase 1 — 模拟器侧 ceiling-gap 度量原型（证据优先，2-4 天，本会话可做）
- 扩展 `tools/simulation/rollout_probe.js`：对一批真实/模拟决策点（T4/T5/T7/T10 等）对照三条轨迹——
  ①当前策略自然轨迹 ②组合模板指引轨迹（缺口→近 2 回合搜索：买缺口引擎/攒对子等三连/升本窗口/法术生成器触发）③（可选）最优参考。
- 产出：ceiling-gap 报告（逐决策点 / 按阶段 / 按体系聚合：名次差 = 破局缺口）。
- 验证：确定性检查（已具备）+ 与 V 系列/C-06~C-11 结论交叉（条件化、防截断）。
- 定位：只量化缺口大小与形态，**不声称真实强度提升**（模拟器量级）。

### Phase 2 — 组合推理层实现（C# 插件 Shadow，3-5 天，实现会话）
- LLD：CompositionState（组合进度状态机：追 X 体系/引擎在场度/缺口/对子三连进度/距启动回合）
  / CeilingEstimator（模板 × 状态 → 上限/缺口清单）
  / BreakthroughSearch（缺口 → 本回合+近 2 回合可达动作，**置于规则瀑布前**，瀑布降级为硬约束+安全网）。
- 工程：合成夹具单测 + 代码审查（双向验证误判，留帧证据）+ 回归 100% + legacy 零改动；
  作为 **C-12** 接入现有 shadow 基建。
- 验证：shadow 实机 V 系列口径对比；300 场景 + owner 门禁；用户实测反馈闭环后才可见。

### Phase 3 — CompositionMemory + 终局复盘归因闭环（2-3 天，实现会话）
- 扩展 StrategicMemory 为结构化组合进度（跨回合一致，不每回合从零推断）。
- 对局结束复盘（SAGA GameAnalyst 模式）：归因"哪个阶段板面停滞、离可达上限差多少、缺什么杠杆"，
  沉淀案例库（标注 build/日期/版本），反哺规则/评分与模板数值校准。
- 验证：归因与 V 系列结论一致；玩家动作=观测非标签（不违反铁律）。

### Phase 4 — 通用内核价值学习 + 适配层（远期，与 Phase 2/3 并行可推进）
- 08-31 计划阶段 2/3：rollout 标签训练机制特征价值函数（MuZero 思路仅参考不采用）；
  适配器（英雄/曲线/流派）参数化微调 = 新赛季快速适应。
- 门禁：Shadow + legacy 兜底；跨赛季迁移测试；无真实新赛季权威数据不发布。

## 四、采纳 / 不采纳清单

**采纳**：组合模板数据化（版本化、机制字段）· 跨回合目标记忆 + 阶段语义 · 终局复盘归因闭环 ·
价值学习方向（轻量、本地）· DeepBattler 的信息架构参考 · bgknowhow/MrBridgeHQ 的"数据层喂上层"模式 ·
战斗模拟器做 CombatResolver 校验基准（可选）。

**不采纳**：LLM 做决策判分（不可靠/成本/离线/隐私，AGENTS 纪律）· 大规模 RL（无公开强 agent +
算力与本地约束）· MCTS 单独扛（已有且经证明不够）· 机械对齐社区攻略数值（版本失真，须标注）。

## 五、资源与分工

- 本会话（分析/工具链）：Phase 1 ceiling-gap 原型（模拟器侧，不动插件/部署）。
- 实现会话（插件侧）：Phase 0/2/3（LLD + Shadow C-12 + 部署 SOP + 记录回填）。
- 数据：回放登记册语料（标注 build）；组合模板数值待 36.4.2/新赛季权威卡表到齐后校准（当前标 unknown）。

## 六、风险与缓解

- 模板数值依赖权威新赛季数据（缺失）→ 上限估计先做**结构/缺口型**（不依赖精确数值），数值标 unknown 降置信。
- ceiling-gap 是模拟器量级 ≠ 真实强度 → 只作定位与优先级依据，不做上线声明。
- 单反事实样本小 → Phase 1 扩展多 seed 统计分布后再进 Phase 2。
- 与并行迭代冲突 → 全程走 ISSUE_TRACKER 闭环 + 代码审查 + 单测 + 100% 回归（AGENTS 纪律）。

## 七、参考链接（调研来源）
- https://github.com/william-Dic/DeepBattler
- https://github.com/yossielimelech/BGSimulator
- https://github.com/twanvl/hearthstone-battlegrounds-simulator
- https://github.com/janvdvegt/Ghastcoiler
- https://github.com/silverlight6/TFTMuZeroAgent （亦见 https://github.com/SaeedAnas/TFTMuZeroAgent）
- https://github.com/esterRozen/SuperAutoPetsAI
- https://github.com/xinyao27/hearthstone-battlegrounds-tools
- https://github.com/O-Nemet/bgknowhow
- https://github.com/MrBridgeHQ/tft-mcp
- https://arxiv.org/abs/2606.29932（SAGA）· https://arxiv.org/abs/2607.02255（AgenticSTS）

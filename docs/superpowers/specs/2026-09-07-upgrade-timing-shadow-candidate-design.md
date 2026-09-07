# C1 升级决策条件化 — Shadow 候选规格 (2026-09-07)

> 状态: **Shadow 规格(仅设计, 不实施不转正)** — 对应 ISSUE_TRACKER I-20260907-22 C1。
> 依据: 2026-09-07 round324-326 三口径帧精查 + I-20260906-15(O1) 机制核验 + alignment 129 局。
> 纪律: 玩家动作=观测非标签; 效果断言需 300 真实 version-matched 场景 + Shadow + owner approval;
> 转正前可见策略恒为 legacy-v1.1.0 决策链 + 编排层现行规则。版本数据源: 36.4.2(gamerhub 249896)。

## 1. 背景与问题

- 升级(升酒馆)是战棋中后期"滚雪球节奏"的核心杠杆之一: 时机错=亏节奏/亏铸币;
  时机对=早一回合的铸币上限/卡池窗口优势。
- **分歧证据(三口径终裁, round324-326)**:
  1. 60s 招募窗口: 引擎在 70% 的"玩家升本级回合"给出过 Upgrade 建议(105/151);
  2. level 帧(玩家实际点升的前 ~3s)精确 best=Upgrade 仅 51/151;
  3. 5本→6本: 262 次引擎推 LEVEL 玩家跳过中 166 次在 5本(满板 173/金全够) —
     执行升6 n41 avg2.80 vs 跳过 n161 avg3.11(相关性, 非因果)。
- **解读**: 引擎升级"意愿"不弱(70% 给过), 差异=**时机/回合组合**——引擎按"逐帧最优"给
  Upgrade, 但缺: ①先买/先做核心的排序语境(STABILIZE 先买出口) ②刚升本冷却 ③5本成型
  质量流(满板/核心齐/金充足)时升6 的推判应让位于战力/三连准备。

## 2. 候选规则(Shadow 内实现并记录, 不改变可见推荐)

仅在 Shadow 层计算 would-be 推荐并与 legacy 决策记录对比:

### C1a 先买后升排序(StabilizeBuy→Upgrade 出口扩展)
- legacy STABILIZE(先买)出口仅在"买后金≥升费"时给 Upgrade 建议。
- Shadow: 前置条件扩展——当 ①商店存在应买核心/高战力卡 且 ②买后剩余金 ≥ 升费 时,
  would-be = BuyMinion(先买) 而非直接 Upgrade; 若买后金<升费且无其他可买 → Upgrade。

### C1b 刚升本冷却(防连推)
- 升本后 <2 回合(或 <3 个招募回合)内不再将 Upgrade 置顶(含 R4/R3/R-triple/R8 出口,
  与 I-20260906-15 o1ShapedFlow 守卫同族; o1ShapedFlow 已实现于 env BOBCOACH_O1_SUPPRESS,
  本规格将其纳入统一 Shadow 评估面)。

### C1c 5本成型质量流(升6语境, 关联 O1)
- 5本满板 + 核心方向卡≥2 + 金≥10 + mid/late → would-be 抑制 Upgrade→6 的"逐帧最优",
  建议质量动作(三连准备/换血/buff), 保留"终局形态已齐或卡池必要"时的升6。

### C1d 两步前瞻纳入升级
- 现有二步前瞻(secondStep)纳入升级分支: 若 买X→下回合升 优于 立即升, would-be=买X。

## 3. Shadow 数据采集设计

- 帧级记录: {turn, tier, gold, liveGold, boardPower, boardFull, coreCards, lastUpgradeTurn,
  legacyBest, shadowWouldBe, agree, phase} 追加到现有 shadowScores 回放通道
  (alignment_pairs.jsonl 同源, 供 129+ 局离线对比)。
- 对比指标: would-be 与 legacy 分歧帧中, 各自决策在模拟器/后续回合的实际收益
  (升级是否发生/铸币窗口/名次)——描述性, 先于 A/B。

## 4. 门禁(转正前置)

1. 工程就绪: 确定性/性能/兼容/隐私/fallback/Shadow 记录 ✓(实现后验证);
2. 效果门禁: ≥300 真实 36.4.2 version-matched 场景, Shadow vs legacy 决策对比 + 人工审查;
3. owner approval(approval keyset 当前空 → 只能报工程就绪 / blocked-by-season-data / 待 A/B)。
- 模拟器 A/B(6seed×N 局)可用时先行裁决 C1a~C1d 组合面。

## 5. 非目标 / 边界

- 不推断新赛季卡/数值; 不调 legacy 权重; 不改升级费用曲线(官方值);
- 无证据的"更好升级"口号不成立——本规格只定义可验证的分歧规则与采集, 结论留待门禁。
## 6. 语料探针证据(2026-09-07, 129 局全局 recommend 事件)
- **C1b 冷却违规面**: 引擎 best=Upgrade 共 3117 帧, 其中 458(14.7%) 在刚升本后 <2 回合内; **后期(T≥8 且 tier=5) 1921 帧中 201 帧(10.5%)在冷却内**(≈1.6 帧/局) → C1b 命中面=后期 5本"连升压力"帧, 每局有限但持续存在
- **细化(C1b 语境化)**: 早期(T≤7)连续升本=合法速本节奏, 冷却不适用; 冷却规则仅后期(T≥8/tier≥5)生效——与 O1(5本成型升6)同一目标帧面, 二者应合并评估(Shadow 同源采集)
- 探针脚本: knowledge-check/_c1b_probe.js / _c1b_probe_late.js (证据侧, 可复用)

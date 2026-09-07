# C5 后期停滞转型(危险停滞检测) — Shadow 候选规格 (2026-09-07)

> 状态: **Shadow 规格(仅设计, 不实施不转正)** — 对应 ISSUE_TRACKER I-20260907-22 C5。
> 依据: 2026-09-07 round324-326 帧精查 + 129 局 alignment/impact + _HOTSPOT_DIGEST。
> 纪律: 玩家动作=观测非标签; 效果断言需 300 真实 version-matched 场景 + Shadow + owner approval;
> 可见策略恒为 legacy-v1.1.0 决策链 + 编排层现行规则。数据源: 36.4.2(gamerhub 249896)。

## 1. 背景与问题

- "停滞"(数回合战力/阵容无实质提升)在后期高压局=慢性死亡: 每回合被多扣血, 无法滚雪球。
- **分歧/模式证据(round324-326)**:
  1. needTradeUp 34986 帧仅 6 次(0.02%) — 引擎几乎从不触发"需要换牌转型";
  2. STALL(停滞意图)75 回合, 73% 出现在 top4 局 — 高分位局里玩家也常停滞(常态?);
  3. bottom-STALL(低名次+停滞)20 帧中引擎无转型引导(推荐 Sell 仅 1 次) — 高压/劣势局引擎没给出"动起来"的方案;
  4. 掉血热点(impact): 单回合掉血≥5 及后续回合仍掉血(强弱构建)是滚雪球失败的主因。
- **解读**: 引擎缺一个"劣势+连续停滞 → 主动转型/止损"的显式检测与建议面; 现有规则只在
  STABILIZE/逐帧最优里隐式处理, 劣势局容易进入"守成式停滞"。

## 2. 候选规则(Shadow 内记录, 不改变可见推荐)

### C5a 危险停滞检测器(Detector)
- 输入: ①名次/血量劣势(rank>5 或 hp≤阈值且递减) ②连续停滞回合数(战力增量≈0 且无核心收集)
  ③本回合掉血≥5 或连续掉血 ④needTradeUp 信号(核心方向缺失/板面战力低于均势)。
- 输出: {dangerousStall: bool, stallTurns, suggestedTransition: "换血/转型方向"|""} — 仅 Shadow 记录。

### C5b 转型引导(Shadow would-be)
- 危险停滞命中时 would-be 建议(与 legacy 对比, 不生效):
  1. 卖弱换强/换血(板面战力置换方向, 复用 sell 质量门控但放宽容忍);
  2. 转型方向建议: 若当前 comp 无核心且商店出现他族核心 → would-be=买入新核心起手;
  3. 有 buff/三连窗口时的优先动作(不再 FreezeShop 守成)。

### C5c 不干预边界
- top4 高分位常态停滞(73% 在 top4 局)不触发引导(避免打扰优势局玩家自己节奏);
- 仅在 rank>5 或 hp 劣势 + 连续停滞 ≥2 回合时检测。

## 3. Shadow 数据采集

- 帧级: {turn, rank, hp, stallTurns, powerDelta, needTradeUp, dangerousStall,
  legacyBest, shadowWouldBe, agree} → 追加 shadow 回放通道(alignment 同源)。
- 离线评估: dangerousStall 命中帧中, 玩家实际做法的后续回合掉血/名次 vs would-be 决策的
  模拟对比(描述性, 先于 A/B)。

## 4. 门禁(转正前置)

1. 工程就绪(确定性/性能/fallback/Shadow 记录);
2. 效果门禁: ≥300 真实 36.4.2 场景(重点=劣势停滞局), Shadow vs legacy + 人工审查;
3. owner approval(keyset 空 → 只报工程就绪/blocked-by-season-data/待 A/B)。
- 模拟器 6seed A/B 可用时先裁决 C5a/b 组合(特别: 转型引导在模拟环境的收益)。

## 5. 非目标 / 边界

- 不推断新赛季内容; 不改 legacy 权重; 不打扰优势局(top4 常态停滞豁免);
- "危险停滞→转型"仅在被数据支持的高压/劣势场景生效(Sample 门槛 ≥10 命中才出结论)。

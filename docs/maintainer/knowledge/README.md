# BobCoach 策略知识库

> 用途: 将历史迭代收集的总结经验和数据分门别类存放, 便于后续优化修正。
> 来源: OPTIMIZATION_LOG.md + USER_FEEDBACK_ISSUES.md + 各轮测试分析。

## 目录

| 分类 | 内容 | 关键文件 |
|---|---|---|
| 01-hero-training | 英雄专项特训: 排名差异分析、节奏画像、技能机制 | `01-hero-training/hero-rank-gap-20260821.md` |
| 02-mechanism-fidelity | 测试环境机制与真实一致性: 已验证偏差与修复 | `02-mechanism-fidelity/fidelity-deviations.md` |
| 03-strategy-patterns | 优质策略收集: 真实玩家行为模式、可复用操作 | `03-strategy-patterns/player-patterns.md` |
| 04-experiments | 实验记录: 每轮 A/B 的假设、结果、结论 | OPTIMIZATION_LOG.md(权威) + 摘要 |
| 05-data-assets | 数据资产索引: 训练集/画像/报告的存放位置 | `05-data-assets/data-inventory.md` |

## 使用原则

1. **数据驱动**: 所有结论附数据来源(回放ID/报告路径/局数样本)。
2. **机制以真实为准**: 技能机制有不完全写在文本的, 以回放+用户实测为准, 不猜。
3. **实验可复现**: 每轮实验记录 seed/参数/对比基线, 可重跑。
4. **沉淀循环**: 每轮进化结束, 把新经验归入对应分类。

## 索引

- [英雄排名差异榜(2026-08-21)](01-hero-training/hero-rank-gap-20260821.md)
- [机制偏差清单](02-mechanism-fidelity/fidelity-deviations.md)
- [玩家策略模式](03-strategy-patterns/player-patterns.md)
- [数据资产清单](05-data-assets/data-inventory.md)

_维护: 每次进化轮次结束后更新。_
- [04-experiments/R283-288-hero-gates.md](04-experiments/R283-288-hero-gates.md) — R283-R288 英雄门控实验总结(托奇/钩牙门控有效, cookie锅/塔维什否决, 嵌套bug教训)

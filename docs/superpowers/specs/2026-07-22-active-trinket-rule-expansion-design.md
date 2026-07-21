# 已装备饰品本地规则扩展设计

**日期：** 2026-07-22
**状态：** 已批准执行
**规则事实版本：** HDT 1.53.5 / HearthDb build 245258

## 目标

在不恢复 Firestone 饰品统计数据、不显示饰品报价推荐的前提下，扩大 BobCoach 对已装备饰品确定性效果的理解。报价推荐和已装备效果继续使用完全独立的输入与启停条件：`TrinketRecommendationsVisible = false` 只控制报价 UI，不能阻断 `ActiveTrinkets`、本地规则、行动评分或战斗模拟。

## 本轮范围

本轮只增加能由精确 CardId 和当前本地模型完整表达的开战效果：

| CardId | 饰品 | 本地规则 |
| --- | --- | --- |
| `BG30_MagicItem_542` | Emerald Dreamcatcher | 开战时，把己方所有龙的攻击力设为己方战团当前最高攻击力。 |
| `BG35_MagicItem_702` | Stegodon Portrait | 开战时，按战场顺序给己方最左侧两个野兽圣盾。 |
| `BG30_MagicItem_441` | Tinyfin Onesie | 开战时，己方最左侧随从获得己方手牌中最高生命随从的攻击力和生命值。 |
| `BG35_MagicItem_754` | Dramaloc Sticker | 开战时，己方所有鱼人获得己方手牌中最高攻击随从的攻击力。 |

以上语义来自本机 HDT 1.53.5 构建参考中的 HearthDb 快照。CardId 必须区分大小写；未知 CardId 保守忽略效果，只进入每局限频诊断。

## 数据流

```text
HDT 已装备实体 CardId
  -> TrinketEffectRegistry（精确版本化注册）
  -> ActiveTrinketContext（不可变、来源无关）
     -> GetCardSynergyScore（购买/保留价值）
     -> ApplyStartOfCombat(ownerBoard, ownerHand)
        -> CombatSimulator 双方独立上下文
```

`CombatSimulator` 已持有双方手牌，唯一接口调整是将各自手牌传给对应的 `ActiveTrinketContext`。任何效果不得读取对手手牌或应用到对手战场。

## 评分边界

Emerald Dreamcatcher、Stegodon Portrait 和 Dramaloc Sticker 分别为龙、野兽和鱼人增加有限的本地协同分。Tinyfin Onesie 不引入按文本或任意阈值猜测的购买分；其确定性收益通过开战模拟反映。现有 Eyepatch、Cowrie、Anvil、Slamma 评分保持不变。

## 生产边界回归

测试必须额外固定以下事实：

- `ExtractActiveTrinkets` 在 `TrackGold` 前执行，使 Cowrie 费用规则先于资源计算生效。
- 商店和手牌的 `GrantsStats` 只调用 `TrinketEffectRegistry.IsStatGrantingTavernSpell(e.CardId)`，不得使用中英文关键词推断硬规则。
- 未知已装备饰品不在提取阶段被本地事实源过滤，并通过 `ExtractorLog` 按精确 CardId 每局限频记录。
- 所有行为测试只构造合成战场、手牌和 CardId 列表，不读取 Firestone 数据或用户缓存。

## 非目标

- 不恢复或新增饰品报价统计请求、缓存、解析或自动重试。
- 不改变饰品报价推荐服务与 UI 的默认隐藏状态。
- 不实现需要跨回合计数、随机结果、发现选择或模糊文本解释的饰品。
- 不读取、迁移或删除用户历史缓存；不运行完整 VM 验收。

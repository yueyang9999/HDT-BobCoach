# 已装备饰品本地规则扩展 Phase 2 设计

**日期：** 2026-07-22
**状态：** 已授权执行
**规则事实版本：** HDT 1.53.5 / HearthDb build 245258

## 目标

在不恢复 Firestone 饰品统计、不显示饰品报价推荐的前提下，扩大 Bob Coach 对已装备饰品确定性战斗效果的本地覆盖。报价推荐和装备效果继续使用完全独立的输入与启停条件；`TrinketRecommendationsVisible = false` 只控制报价 UI。

## 本轮规则

本轮只实现精确 `CardId`、无需随机数或跨回合历史、且当前 `CombatUnit` 模型能完整表达的开战效果：

| CardId | 饰品 | 本地规则 |
| --- | --- | --- |
| `BG30_MagicItem_301` | Eternal Portrait | 己方 Eternal Knight 获得嘲讽和复生；目标只接受 `BG25_008`、`BG25_008_G`。 |
| `BG30_MagicItem_310` | Rivendare Portrait | 己方 Titus Rivendare 当前生命与最大生命翻倍；目标只接受 `BG25_354`、`BG25_354_G`。 |
| `BG30_MagicItem_902` | Holy Mallet | 己方最左和最右随从获得圣盾；单随从只处理一次。 |
| `BG30_MagicItem_962` | Training Certificate | 按修改前攻击力选择最低的两个随从，将其当前攻击、当前生命和最大生命翻倍；并列按战场顺序稳定选择。 |
| `BG30_MagicItem_970` | Valorous Medallion | 己方全体获得 +2/+2，并同步最大生命。 |
| `BG30_MagicItem_970t` | Greater Valorous Medallion | 己方全体获得 +6/+6，并同步最大生命。 |
| `BG32_MagicItem_360` | Baleful Incense | 己方最左和最右亡灵获得复生；单个亡灵只处理一次。 |

所有匹配区分大小写。未知、错拼或不同大小写的 ID 保守忽略效果并进入现有限频诊断。

## 结算与隔离

`ActiveTrinketContext.ApplyStartOfCombat` 继续在英雄、场上随从和手牌的开战效果之后执行。每条规则只读取和修改所属方战团；选取类效果先从修改前状态确定目标，再写入效果，避免前一个目标的修改影响后一个目标。

组合规则按注册表中的固定顺序结算。Eternal Portrait 和 Rivendare Portrait 采用精确随从 `CardId`，不根据名称、种族或模糊文本猜测目标。生命修改同时更新 `Health` 和 `MaxHealth`。

## 暂缓范围

`BG30_MagicItem_972` Karazhan Chess Set 暂缓。它需要通过 `CombatContext.SpawnToken` 完成深复制、战团插入、`AllUnits` 登记和所属方召唤效果联动；当前 `ApplyStartOfCombat(ownerBoard, ownerHand)` 没有战斗上下文。单独改造该接口并补齐召唤生命周期测试后再纳入，不能只向列表追加浅复制。

不实现随机、发现、生成、依赖跨回合历史或可能已由 HDT 实体属性体现的常驻光环。测试只使用合成战团和固定 ID，不读取 Firestone 数据、历史缓存或用户数据。

## 版本与文档

本轮规则集版本为 `hdt-1.53.5-hearthdb-2026-07-22-r2`。README、数据来源、隐私、NOTICE、架构和依赖文档统一记录 15 个精确 ID 的本地覆盖以及来源无关边界。

# 环境基线 + 个人回放校正：算法智能化升级

Date: 2026-08-12
Status: Implemented and deployed

## 结论

不能只靠个人数据来提升算法。个人数据反映的是“这个玩家怎么打”，不能代表当前版本整体环境，也覆盖不了英雄、种族、饰品和黑暗之赐的组合空间。

算法智能应该来自三层数据：

1. 环境基线：官方补丁、卡池、机制规则、公开 meta 和核心组件。
2. 局势推理：当前阵容、手牌、酒馆、血量、动态铸币、对手信息。
3. 个人回放校正：把真实动作和推荐做差异分析，找出算法不理解玩家思路的地方。

当前实现的优先级是：先让算法知道“当前版本什么流派强、核心卡是什么”，再让回放记录能定位算法是否沿主线思考。

## 第一性原理决策模型

酒馆战棋的单回合决策不是孤立操作，而是一局资源分配的局部实现：

```text
版本卡池与机制
  -> 当前环境流派基线
  -> 本局可执行主线
  -> 核心组件节点 / 经济节点 / 战力节点 / 生存节点
  -> 动态铸币与后续回合资源投影
  -> 候选动作排序
  -> 最小 UI 提示
```

因此，指导层必须保留“主线策略”这一中间表示，而不是只输出单个 `BuyMinion`、`Upgrade` 或 `SellMinion`。

## 数据来源

- 官方 Patch 36.2：Dark Gifts、Activate、Lockbox、Fishbait、移除/新增/回归随从。
- 官方 Developer Insight：黑暗之赐逐回合随从星级表、43 个黑暗之赐窗口和限制条件。
- 36.2.1 Hotfix：Charisma 6-9、Steady Growth max 5、Battle Scars / Death's Embrace / Spell Siphon max 10。
- 社区 meta 快照（2026-08-11）：野兽龙虾流 T0、亡灵魅影宰割 T0、元素雷暴 T1、机械/野猪/龙/纳迦整体弱于核心环境。
- Firestone：本机 30 局 XML 已抽取 1516 个唯一 CardId，本地中文卡库覆盖完整。
- BobCoach global replay：以 `global_*.jsonl` 记录逐回合状态、动作、推荐和战斗事件。

## 已落地改动

### 1. 环境基线

新增：

- `E:\HDT-BobCoach-evidence\season14\generated\registries\season14_meta_baseline.json`
- `src\BobCoach\Core\SeasonMetaBaseline.cs`

`CompGuideCatalog` 现在会加载环境基线，覆盖当前 guide tier，并允许用显式 CardId 覆盖核心卡列表。野兽龙虾流已补上 `BG36_201`（深潜狮子鱼），这是玩法文本中提到但没有进入核心卡列表的 Activate 组件。

### 2. 黑暗之赐规则更新

`dark_gift_registry.json` 和 `official_dark_gift_registry.json` 已同步 36.2.1：

- Charisma: 6-9
- Steady Growth: max 5
- Battle Scars / Death's Embrace / Spell Siphon: max 10

`DarkGiftDecisionService` 新增早期持有和最后一次机会逻辑：

- 回合 3-5，阵容和血量不危急时，普通期望值不点。
- 回合 9 及以后，如果还有最后一次使用机会，提高开启优先级。

### 3. 回放可审计主线计划

`GlobalReplayRecorder` 的 `recommend` 事件新增 `mainlinePlan`：

```json
{
  "e": "recommend",
  "mainlinePlan": {
    "archetype": "野兽龙虾流",
    "tier": "T0",
    "missingCoreCards": ["囤食土狼", "逐亡陆行鸟"],
    "shouldBuyCoreFirst": true,
    "nodePriority": "CoreNode"
  }
}
```

这样离线分析可以直接看到：推荐动作是否符合当前主线、缺失核心卡是什么、当前节点是核心节点还是普通节点。

## 验证

- `test_season14_registries.ps1` PASS，断言 375 项。
- `test_phase2_decision_services.ps1` PASS。
- `test_global_replay_recorder.js` PASS。
- `test_release_build_contract.js` PASS，compile=238。
- `test_ui_lifecycle.js` PASS，243 项。
- `npm run test:contracts` PASS。
- `npm run test:behavior` PASS。
- `npm run test:package` PASS。
- HDT 正式基线已切换为当前本机 `1.55.5.0`，Release 构建不再需要 `-AllowNewerHdt`。

## 剩余风险

- 当前环境基线主要来自官方规则和一篇社区 meta 文，样本量有限。需要再用 Firestone/HSReplay 风格统计交叉验证。
- Firestone 没有公开稳定的英雄/流派胜率 API，当前只能靠卡片 JSON、本机 XML 和公开网页快照。
- “大量不同玩家对战”在个人开发条件下不可行，替代方案是使用公开 meta 统计做环境基线，再用自己的回放做差异校正。
- `mainlinePlan` 需要几局真实对局验证实际 JSONL 内容，并和 Firestone XML 对拍。
- 主线策略层已经可以识别核心节点和缺卡，但还没有把“T0 主线缺失核心”的紧迫度动态绑定到升本/刷新/出售/黑暗之赐的完整资源链上。

## 下一步

1. 把 `season14_meta_baseline.json` 的更新做成可重复脚本，记录 source、capturedAt、sha256。
2. 扩展现有 replay analyzer，输出 `mainlinePlan` 与最终名次、实际购买、核心卡抓取时序的差异报告。
3. 用 30 局 Firestone 回放先做环境校正；再用 30-100 局本地回放做个人打法校正。
4. 只有经过真实对局和回放对拍后，才把高置信度主线调整提升为玩家可见提示。

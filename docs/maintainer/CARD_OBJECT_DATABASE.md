# Card Object Database

`CardObjectDatabase` 是本项目统一卡牌事实入口，运行期只读。

## 职责

- 聚合 minion、tavern spell、activate、trinket 等 Season14 注册表。
- 对已知 CardId 返回结构化 `CardObjectFact`，包含：
  - 中英文名
  - 类型、星级、费用、身材
  - 种族、机制、文本
  - 是否可发动、发动费用
- 未知 CardId 一律返回 `null`，不泄露 raw ID。

## 接入位置

- `DecisionEngine.GetCardObjectFact(cardId)`
- `DecisionEngine.LoadResources()`

## 规则

- 新增机制只需要扩展 registry/fact source，不新增 `DecisionEngine` 分支。
- 数据加载失败时 fail-closed，算法回退到现有 HearthDb/规则路径。

## “能使”机制

- `StatGiftRuleEvaluator` 负责识别“能使随从获得属性值的酒馆法术/元素”生成卡，以及“额外获得”增强卡。
- `DecisionEngine` 在商店评分中同时考虑生成卡与增强卡的互相价值，并读取增强卡文本中的 `+N/+M` 计算强度。
- 该规则仍由 CardId + HearthDb 文本派生，无需为每个新赛季卡硬编码单独分支。

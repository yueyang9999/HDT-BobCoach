# T35 Second-Pass Adversarial Review

Date: 2026-08-13

Branch: `codex/season14-algorithm-dark-gift-capture`

## 结论

本次复检确认 T35 主线修复已落地，但发现两个会让“错误控制”失效的高风险缺口，并已修复：

1. 插件层仍有两条卖后买判断保留旧的资源/核心牌“星级即可替换”规则。
2. 塞纳留斯早期技能的高优先级会无条件压过 T2-T5 的关键升本、三连和主线核心购买。

修复后完整 `npm test` PASS。当前 DLL 已重新部署到五个本地 HDT 插件目录，等待真实对局复验。

## 已修复问题

### 插件层卖后买门控

- `BobCoachPlugin.IsShopScoreWorthAfterSell`
- `BobCoachPlugin.IsShopMarkerWorthAfterSell`

这两个方法之前仍使用：

```csharp
shopPower >= soldPower * 0.75 || shop.Tier >= sold.Tier
```

这会绕过 `DecisionEngine` 和 `DecisionVisualizer` 已经收紧的规则，继续生成“卖弱牌买高星资源牌”的 UI 路径。现已统一为：

- 明确战力提升：`shopPower >= soldPower * 1.05`
- 或场上被卖随从确属 dead weight：`soldPower <= boardAvg * 0.65`

不再允许只凭商店星级触发卖后买。

### 塞纳留斯早期技能过度优先

原实现中 `TryGetHighPriorityHeroPlaybookAction` 在 `ActionValueBias >= 0.8` 时直接返回 `UseHeroPower`。塞纳留斯 T1-T5 的 Critical phase 满足该阈值，因此 T2-T5 有可支付升本或核心/三连购买时也会被技能抢掉。

新增 `ShouldDeferHeroPlaybookForVisiblePriority`：

- 主线计划要求先买核心或商店存在核心/三连时，延后技能。
- T2-T5 可支付且安全的升本窗口存在时，延后技能。
- 危险升本、主线阻止升本时不阻断技能。

价值评分路径和 MCTS 保留判断也使用同一守卫，避免被普通评分绕过。

## 回归测试

- `T35HeroPlaybookLiveBehavior` 新增 `CenariusDoesNotOverrideAffordableUpgrade`：T2 可升本时不再返回 `UseHeroPower`。
- `test_ui_lifecycle.js` 新增插件卖后买路径断言，覆盖两个旧宽松分支。
- UI 生命周期：264 PASS，1 个既有 warning。
- T35 live behavior：6 PASS。
- 完整 `npm test`：PASS。

## 仍存在的风险与未解决问题

### 英雄玩法数据

- `hero_playbook_registry.json` 目前只包含 8 个 curated overlay，不是 116 个英雄的完整文件。运行时先用 `hero_skill_registry.json` 构建 116 个事实 profile，再叠加 overlay，功能上成立，但文件名和文档容易造成“完整英雄攻略”的误解。
- 7 个官方 36.2 改动英雄只记录了 `strategyCoverage=curated` 和来源 URL，没有实际策略 phase。这不能被视为完整攻略；更准确的标签应是 `facts-only` 加 provenance，或补足可验证的策略阶段。
- 部分英雄技能类型仍通过文本推导。unknown/passive 文本若没有命中触发词，可能被推导为 Active；枚举和 UI 有二次防护，但事实层仍需人工核验。

### 用户历史反馈仍未根本解决

- 阿凯：后期主线已明确为野兽时仍可能推荐亡灵/无关牌，需继续用真实对局复验主线收敛。
- 寄生之魂：黑暗之赐词条选择偏好、黄金残片下轮铸币规划和场上发动机制仍未完成。
- 芬利：动态金币分配、卖南海卖艺者囤币、赢家面包滚雪球和“不卖火车王”等场景需要更多真实回放，不是单次规则能关闭。
- 费林：发现提示已修，但“有恶魔核心却优先升本/不买恶魔”这类联动仍取决于主线策略是否及时识别。
- 动态铸币/下轮资源分配仍是长期缺口；当前只做了局部预测，不能声称已解决长链路决策。

## 部署记录

- 新 DLL bytes：`1011200`
- 新 DLL SHA256：`BB65DDA49D4B27A65ECC601337A03E70FD0C59931B300262DDA4EB2DF5340382`
- AppData 部署前归档：
  `E:\HDT-BobCoach-evidence\dll-archive\20260813T094219016Z-e8ea5fa4`
- 团子版部署前归档：
  `E:\HDT-BobCoach-evidence\dll-archive\20260813T094243015Z-tuannzi-pre-BB65`
- 五个本地 HDT 插件目录均已核对同一 SHA256。

## 验证边界

- 自动化测试证明确定性、门禁和回归，不证明真实对局胜率、名次或策略质量。
- 部署后的真实回放尚未产生，仍需用户实测后提供回放和 Power.log 才能继续收敛。

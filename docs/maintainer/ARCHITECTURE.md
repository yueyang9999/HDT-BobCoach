# 架构

## 边界

HDT-BobCoach 是运行在用户已安装 Hearthstone Deck Tracker (HDT) 中的 x64 `.NET Framework 4.7.2` 插件。仓库只包含插件源码、测试、确定性构建/发布工具和公开文档；不包含 HDT 二进制、用户数据、生成包或验收证据。

`src/BobCoach/BobCoach.csproj` 是生产依赖的唯一入口。它引用用户提供的 HDT、HearthDb 和 Newtonsoft.Json 程序集，且 `Private=false`，发布包不重新分发这些程序集。

## 运行路径

1. `BobCoachPlugin.cs` 接入 HDT 插件生命周期。
2. `GameStateExtractor.cs` 从 HDT 内存状态和本地 Power.log 取得需要的对局事实。
3. `Core/` 将事实解析为规则、候选动作、评估结果和可视化计划。
4. `OverlayRenderer.cs` 使用 WPF 在 HDT 宿主内呈现结果。

生产 DLL 只嵌入样式 XAML，不嵌入 JSON 数据资源。卡牌、英雄和规则事实由用户本机 HDT 提供的 HearthDb 在调用栈内解析；无法确认的事实应失败关闭。

## 数据与网络

核心决策在本机运行。插件可读取本地 HDT/游戏状态、`Power.log`、`log.config` 和 `%APPDATA%\bob-coach` 的自身数据。写入 `log.config` 必须经过 HDT 内可见的变更预览和明确确认。

当前公开版不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计。`BobCoachPlugin` 不创建或驱动外部饰品统计 updater；无已授权来源时只暴露 `SourceUnavailable` 状态，既有历史缓存不读取、不迁移、不删除。

发布线为 `0.2.0-beta.2`；`0.2.0-beta.1` preview 只作为历史包审计边界保留。当前离线包构建器拒绝 `-CurrentSeasonPreview`，不会用 beta.2 身份、DLL 或 hash 重建 beta.1 preview。

饰品报价推荐与已装备效果是两条独立调用链。报价候选的识别、本地评估、推荐服务、面板和渲染代码仍保留，但首发不显示报价选择提示，也不让该提示抢占其他推荐；`TrinketRecommendationsVisible` 只控制最终渲染，不得作为已装备效果服务的启停条件。

已装备效果采用来源无关的本地边界：

```text
GameState.ActiveTrinkets (exact CardId list)
    -> TrinketEffectRegistry
    -> ActiveTrinketContext
        -> EffectiveGameRules
        -> FeatureExtractor
        -> ActionScoring
        -> CombatSimulator
```

`GameStateExtractor` 从 HDT 状态取得 `GameState.ActiveTrinkets`（精确 `CardId` 列表），注册表只按精确、版本化的本地 `CardId` 规则解析。硬规则必须在行动枚举和资源计算前进入 `EffectiveGameRules`；协同修正进入特征与行动评分；战斗开始和召唤类效果进入双方隔离的战斗上下文。未知 ID 只记录限频诊断并保守忽略未知效果，不得用名称或模糊文本猜测合法性、费用或评分。

当前 `hdt-1.53.5-hearthdb-2026-07-22-r3` 规则集覆盖 16 个精确 ID（`BG30_MagicItem_439`、`BG35_MagicItem_921`、`BG30_MagicItem_403`、`BG30_MagicItem_540`、`BG30_MagicItem_542`、`BG35_MagicItem_702`、`BG30_MagicItem_441`、`BG35_MagicItem_754`、`BG30_MagicItem_301`、`BG30_MagicItem_310`、`BG30_MagicItem_902`、`BG30_MagicItem_962`、`BG30_MagicItem_970`、`BG30_MagicItem_970t`、`BG32_MagicItem_360`、`BG30_MagicItem_705`），依据 HDT 1.53.5 / HearthDb build 245258 维护者快照审计。第三方或本地化名称不参与规则解析。`BG30_MagicItem_705` 将升本费用减少 3，并在 fallback 路径将费用下限钳制为 0，实时 HDT 费用不重复折扣。`ApplyStartOfCombat(ownerBoard, ownerHand)` 使用修改前快照或稳定战场顺序确定目标，并由 `CombatSimulator` 分别传入攻击方和防守方自己的战团与手牌，禁止跨方读取或应用效果。

`PhaseStartOfCombat` 的顺序固定为：攻方优先英雄技能、守方优先英雄技能、攻方普通英雄技能、守方普通英雄技能、场上随从双方交替且各自从左到右、手牌效果双方交替、装备饰品、旧式饰品 handler。Reborn 不是原对象原地复活：死亡单位必须从战团移除后，以半血新状态按死亡前位置重新插入所属方；`AllUnits` 不重复登记，所属方 `ApplySummon` 恰好触发一次，且不得调用对手召唤效果。

Karazhan Chess Set 暂不进入该规则集：复制随从必须经 `CombatContext.SpawnToken` 完成深复制、位置插入、`AllUnits` 登记和所属方召唤效果联动，不能仅向战团列表追加浅复制。后续应先为饰品开战效果提供受控战斗上下文接口，再补充满场、复制隔离和 Slamma 联动测试。

这条已装备效果链不依赖 Firestone 统计、报价推荐结果或历史缓存，测试只使用合成状态和固定 ID。`TrinketStatsVerifier` 另行保留来源无关的纯校验边界；`TrinketStatsFetcher` 只保留 HearthstoneJSON 的受限通用能力，但生产插件当前没有外部饰品统计运行路径。未来适配器必须重新设计并单独审批。详见根目录的 `PRIVACY.md` 与 `DATA_SOURCES.md`。

## 目录职责

- `src/BobCoach/`: 生产插件和唯一嵌入资源。
- `tests/`: 合同、行为、离线包与安装生命周期测试。
- `tools/build/`: 构建和仓库验证工具。
- `tools/release/`: 离线包、安装、卸载与生命周期验证工具。
- `docs/user/`: 用户生命周期文档。
- `docs/maintainer/`: 维护与发布规则。

任何新增运行时网络端点、存储字段、嵌入数据或包文件，都必须先更新隐私、数据来源、包白名单和对应测试。

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

这条已装备效果链不依赖 Firestone 统计、报价推荐结果或历史缓存，测试只使用合成状态和固定 ID。`TrinketStatsVerifier` 另行保留来源无关的纯校验边界；`TrinketStatsFetcher` 只保留 HearthstoneJSON 的受限通用能力，但生产插件当前没有外部饰品统计运行路径。未来适配器必须重新设计并单独审批。详见根目录的 `PRIVACY.md` 与 `DATA_SOURCES.md`。

## 目录职责

- `src/BobCoach/`: 生产插件和唯一嵌入资源。
- `tests/`: 合同、行为、离线包与安装生命周期测试。
- `tools/build/`: 构建和仓库验证工具。
- `tools/release/`: 离线包、安装、卸载与生命周期验证工具。
- `docs/user/`: 用户生命周期文档。
- `docs/maintainer/`: 维护与发布规则。

任何新增运行时网络端点、存储字段、嵌入数据或包文件，都必须先更新隐私、数据来源、包白名单和对应测试。

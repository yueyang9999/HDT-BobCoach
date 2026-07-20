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

已披露的只读 HTTPS 候选校验路径仅限 Firestone/Zero to Heroes 聚合饰品统计及 HearthstoneJSON 精确 Build 事实。请求失败不能阻断本地基线；未验证数据不能进入生产评分或 UI 排序。详见根目录的 `PRIVACY.md` 与 `DATA_SOURCES.md`。

## 目录职责

- `src/BobCoach/`: 生产插件和唯一嵌入资源。
- `tests/`: 合同、行为、离线包与安装生命周期测试。
- `tools/build/`: 构建和仓库验证工具。
- `tools/release/`: 离线包、安装、卸载与生命周期验证工具。
- `docs/user/`: 用户生命周期文档。
- `docs/maintainer/`: 维护与发布规则。

任何新增运行时网络端点、存储字段、嵌入数据或包文件，都必须先更新隐私、数据来源、包白名单和对应测试。

# HDT-BobCoach

[中文](README.md) | [English](README.en.md)

[文档目录](docs/README.md)

[![CI](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml)

Bob Coach 是面向《炉石传说》酒馆战棋的 Hearthstone Deck Tracker (HDT) 教练插件。它在本机读取 HDT 已知的对局状态，提供选牌、阵容、站位和战斗决策辅助。

当前最新公开版本为 `0.2.0-beta.1`。仓库当前源码版本为 `1.0.0`，只处于本地发布候选阶段，尚未创建 GitHub Release，也未公开上传安装包。官方安装包只通过本仓库的 [GitHub Releases](https://github.com/yueyang9999/HDT-BobCoach/releases) 提供；请勿将源码、CI 产物、本地候选包或第三方附件视为已公开的官方安装包。当前构建器不会用 1.0.0 输入重建或伪造历史 beta.1 preview。

## 下载与安装

Windows 10 和 Windows 11 使用同一个 64 位安装包：

| 你的系统 | 下载 | 验证状态 |
| --- | --- | --- |
| Windows 11 24H2 x64 | [下载 Bob Coach 0.2.0-beta.1 安装包](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip) | beta.1 公开资产可用；1.0.0 候选尚未完成最终实机 smoke |
| Windows 10 22H2 x64 | [下载同一个 Bob Coach 0.2.0-beta.1 安装包](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip) | 技术兼容，尚未完成专用实机验收 |

[下载 beta.1 SHA-256 校验文件](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip.sha256)

**不要下载** Release 页面底部由 GitHub 自动生成的 `Source code (zip)` 或 `Source code (tar.gz)`；它们是源码快照，不是 Bob Coach 安装包。

第一次安装请打开 [中文安装教程（新手从这里开始）](docs/user/INSTALL.md)，也可直接查看或下载 [HTML 图文教程](docs/user/INSTALL.html)。想先看看插件在对局中会提示什么，可以打开 [功能展示](docs/user/FEATURES.md) 或 [离线 HTML 展示页](docs/user/FEATURES.html)。普通玩家不需要打开终端。

## 系统要求

- 1.0.0 发布候选：Windows 11 24H2 x64 最终实机 smoke 尚未完成
- 目标兼容环境：Windows 10 22H2 x64（技术兼容，尚未完成专用实机验证）
- Hearthstone Deck Tracker `1.53.5` x64
- 系统提供的 .NET Framework 4.8/4.8.1 运行时
- 标准 Windows 用户权限

插件安装后不需要 Node.js、管理员权限或在线依赖安装。HDT 和 Hearthstone 需要由用户自行合法安装。

## 安装摘要

1. 完全退出 HDT。
2. 完整解压安装 ZIP。
3. 打开 HDT，进入“选项 > 插件”，点击“打开插件文件夹”。如果按钮不可用，可手动打开 `%AppData%\HearthstoneDeckTracker\Plugins`。
4. 当前公开的 `0.2.0-beta.1` 只需复制 `BobCoach.dll`。`1.0.0` 发布后，把 `BobCoach.dll` 和 `BobCoach.dll.sha256` 一起复制到该目录根部；1.0.0 缺少校验文件或两者不匹配时会拒绝启动。随后回到插件页面启用 BobCoach。

`INSTALL.ps1` 是可选高级安装方式，用于完整性校验、自动备份和回退；普通安装不要求使用 PowerShell。升级、回滚和卸载分别见 [升级教程](docs/user/UPGRADE.md)、[回退教程](docs/user/ROLLBACK.md) 和 [卸载教程](docs/user/UNINSTALL.md)。

## 隐私与联网

对局、日志、回放和用户画像均保存在本机，不会自动上传。当前公开版不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计，也不读取、不迁移、不删除早期版本可能留下的历史缓存。首发不显示饰品报价选择提示，也不让该提示抢占其他推荐；这个显示开关只控制渲染。玩家装备饰品后，Bob Coach 仍从 HDT 对局状态识别饰品，并通过版本化本地 `CardId` 规则将确定性效果用于之后的购买、出售、刷新、升本、阵容和战斗决策。当前 `hdt-1.53.5-hearthdb-2026-07-22-r4` 本地规则集覆盖 17 个精确 CardId：`BG30_MagicItem_439`、`BG35_MagicItem_921`、`BG30_MagicItem_403`、`BG30_MagicItem_540`、`BG30_MagicItem_542`、`BG35_MagicItem_702`、`BG30_MagicItem_441`、`BG35_MagicItem_754`、`BG30_MagicItem_301`、`BG30_MagicItem_310`、`BG30_MagicItem_902`、`BG30_MagicItem_962`、`BG30_MagicItem_970`、`BG30_MagicItem_970t`、`BG32_MagicItem_360`、`BG30_MagicItem_705`、`BG30_MagicItem_972`。规则依据 HDT 1.53.5 / HearthDb build 245258 维护者快照审计；未知 ID 保守忽略效果，不按本地化名称或模糊文本猜测规则。其中 Oilcan (`BG30_MagicItem_705`) 将升本费用减少 3（实时 HDT 费用不重复折扣，缺失时使用费用下限为 0 的本地 fallback）；Karazhan Chess Set (`BG30_MagicItem_972`) 在开战饰品阶段复制当前最左随从，并通过所属方受控召唤路径执行 7 格上限和召唤联动。完整边界见 [PRIVACY.md](PRIVACY.md) 和 [DATA_SOURCES.md](DATA_SOURCES.md)。

## 数据来源与第三方权利

Firestone/Zero to Heroes 仅作为历史评估背景记录，不是当前运行时数据来源。代码保留来源无关的外部数据校验能力和 HearthstoneJSON/HearthSim hsdata 的受限适配边界，但生产插件当前不驱动外部饰品统计请求。Bob Coach 当前未接入、请求、抓取、打包或再分发 HSReplay 数据。第三方数据、统计、软件、游戏内容和商标仍受各自权利与条款约束，详见 [DATA_SOURCES.md](DATA_SOURCES.md) 和 [NOTICE](NOTICE)。

GitHub 仓库中的代码、文档、规则快照、议题、PR、镜像、fork、评论和外部链接不等于官方数据、完整数据或最新数据；内容可能过期、缺失、被修改，或与用户本机的 HDT/HearthDb build 不一致。它们不自动成为运行时数据源、第三方授权或任何权利人的背书。安装包请以对应版本的 `manifest.json`、外部 SHA-256 校验文件和版本化数据源声明为准；GitHub 自动生成的源码压缩包、CI 工件和第三方附件不是官方安装包。

## 构建与测试

仓库不包含 HDT 二进制。构建需要本机安装 .NET Framework 4.7.2 Developer Pack，并提供 HDT `1.53.5` 目录：

```powershell
$env:BOBCOACH_HDT_DIR = 'C:\path\to\HDT'
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\build_release.ps1 `
  -HdtDirectory $env:BOBCOACH_HDT_DIR `
  -OutputDirectory "$env:TEMP\bobcoach-build" `
  -Force
```

Node.js 只驱动无第三方依赖的合同测试，不属于插件运行时。维护者请先读 [BUILD.md](docs/maintainer/BUILD.md) 和 [RELEASE.md](docs/maintainer/RELEASE.md)。

## 支持与赞赏

问题请通过 GitHub Issues 提交最小复现，不要公开上传完整 `Power.log`、回放、账号信息、token 或未脱敏绝对路径。所有用户功能永久免费；赞赏必须完全自愿，不解锁功能，也不会出现在 Hearthstone 或 HDT overlay 内。当前未发布收款地址、二维码或真实赞赏链接，详见 [SUPPORT.md](SUPPORT.md)。

## 免责声明

Bob Coach 是独立社区项目，与 Blizzard Entertainment、HearthSim、HDT、Firestone、Zero to Heroes、Gamerhub 或 HSReplay 无隶属、赞助或背书关系。第三方名称仅用于说明兼容性、数据来源或未使用边界。详见 [NOTICE](NOTICE)。

## License

本项目原创代码和有权许可的原创材料以 [MIT License](LICENSE) 发布；第三方软件、数据、统计、游戏内容和商标不因本仓库许可证而被重新授权。

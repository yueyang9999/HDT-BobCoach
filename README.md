# HDT-BobCoach

[中文](README.md) | [English](README.en.md)

[文档目录](docs/README.md)

[![CI](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml)

Bob Coach 是面向《炉石传说》酒馆战棋的 Hearthstone Deck Tracker (HDT) 教练插件。它在本机读取 HDT 已知的对局状态，提供选牌、阵容、站位和战斗决策辅助。

当前公开测试版本为 `0.2.0-beta.1`。官方安装包只通过本仓库的 [GitHub Releases](https://github.com/yueyang9999/HDT-BobCoach/releases) 提供；请勿将源码、CI 产物或第三方附件视为官方安装包。

## 下载与安装

Windows 10 和 Windows 11 使用同一个 64 位安装包：

| 你的系统 | 下载 | 验证状态 |
| --- | --- | --- |
| Windows 11 24H2 x64 | [下载 Bob Coach 0.2.0-beta.1 安装包](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip) | 已完成实机验收 |
| Windows 10 22H2 x64 | [下载同一个 Bob Coach 0.2.0-beta.1 安装包](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip) | 技术兼容，尚未完成专用实机验收 |

[下载 SHA-256 校验文件](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip.sha256)

**不要下载** Release 页面底部由 GitHub 自动生成的 `Source code (zip)` 或 `Source code (tar.gz)`；它们是源码快照，不是 Bob Coach 安装包。

第一次安装请打开 **[中文安装教程（新手从这里开始）](docs/user/INSTALL.md)**。安装包可以直接解压，但解压后仍需按教程运行 `INSTALL.ps1`，不能只把 ZIP 或 DLL 放进 HDT 目录。

## 系统要求

- 已实机验证：Windows 11 24H2 x64
- 目标兼容环境：Windows 10 22H2 x64（技术兼容，尚未完成专用实机验证）
- Hearthstone Deck Tracker `1.53.5` x64
- 系统提供的 .NET Framework 4.8/4.8.1 运行时
- 标准 Windows 用户权限

插件安装后不需要 Node.js、管理员权限或在线依赖安装。HDT 和 Hearthstone 需要由用户自行合法安装。

## 安装摘要

1. 关闭 HDT。
2. 核对 ZIP 的 SHA-256，并完整解压到普通本地目录。
3. 在解压目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

安装器会验证包内哈希、DLL 版本和 x64 架构，只写入 `%APPDATA%\HearthstoneDeckTracker\Plugins`。HDT 程序目录下的 `Plugins` 不是用户插件安装位置，安装器会拒绝该路径。看到 `PASS installed` 或 `PASS upgraded` 才表示安装完成。升级、回滚和卸载分别见 [升级教程](docs/user/UPGRADE.md)、[回退教程](docs/user/ROLLBACK.md) 和 [卸载教程](docs/user/UNINSTALL.md)。

## 隐私与联网

对局、日志、回放和用户画像均保存在本机，不会自动上传。当前公开版不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计，也不读取、不迁移、不删除早期版本可能留下的历史缓存。首发不显示饰品报价选择提示，也不让该提示抢占其他推荐；这个显示开关只控制渲染。玩家装备饰品后，Bob Coach 仍从 HDT 对局状态识别饰品，并通过版本化本地 `CardId` 规则将确定性效果用于之后的购买、出售、刷新、升本、阵容和战斗决策。当前本地规则集覆盖 8 个精确 CardId，包括费用、金色合成、召唤协同和战斗开始效果；未知 ID 保守忽略效果。完整边界见 [PRIVACY.md](PRIVACY.md) 和 [DATA_SOURCES.md](DATA_SOURCES.md)。

## 数据来源与第三方权利

Firestone/Zero to Heroes 仅作为历史评估背景记录，不是当前运行时数据来源。代码保留来源无关的外部数据校验能力和 HearthstoneJSON/HearthSim hsdata 的受限适配边界，但生产插件当前不驱动外部饰品统计请求。Bob Coach 当前未接入、请求、抓取、打包或再分发 HSReplay 数据。第三方数据、统计、软件、游戏内容和商标仍受各自权利与条款约束，详见 [DATA_SOURCES.md](DATA_SOURCES.md) 和 [NOTICE](NOTICE)。

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

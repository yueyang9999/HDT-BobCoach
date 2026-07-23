# BobCoach 新手友好手动安装设计

日期：2026-07-23

## 问题

当前安装教程把 PowerShell 安装脚本作为默认入口。该方式能完成文件校验、自动备份和回退，但要求玩家打开终端、处理执行策略并输入命令，不适合不熟悉电脑的用户。

本次调整的目标是让普通玩家只需退出 HDT、找到插件目录、复制一个 DLL，并在 HDT 中启用 BobCoach。高级校验能力继续保留，但不再阻挡默认安装流程。

## 已选方案

采用“手工复制单个 DLL”为默认安装方式：

1. 完全退出 Hearthstone Deck Tracker。
2. 打开 `%AppData%\HearthstoneDeckTracker\Plugins`。
3. 从解压后的 BobCoach 安装包中复制 `BobCoach.dll` 到该目录；升级时确认覆盖旧文件。
4. 启动 HDT，在插件页面启用 BobCoach。

不要求玩家把整个解压目录复制到 HDT，也不依赖 HDT 递归发现子目录中的 DLL。

## 安装包入口

解压后的发行目录必须让玩家无需进入多层子目录即可看到 `BobCoach.dll` 和中文离线说明。包内继续保留 `INSTALL.ps1`、清单、校验和、许可证及其他发布合同要求的文件。

中文离线说明首先展示四步普通安装流程。PowerShell 脚本放入“高级安装、完整性校验与回退”章节，并明确它不是普通安装的必要步骤。

## 文档结构

- `README.md`：中文快速开始直接链接并概述四步普通安装。
- `docs/user/INSTALL.md`：作为完整中文安装权威文档，包含安装、升级、启用和常见问题。
- `tools/release/README_OFFLINE.md`：作为压缩包内随附说明，打开即能看到相同的普通安装流程。
- `README.en.md`：保持事实和默认安装方式与中文文档一致，但中文仍是仓库默认入口。

主教程使用动作句，不要求理解 PowerShell、SHA-256、程序集或架构术语。技术说明集中在高级章节，避免干扰首次安装。

## 升级与错误处理

普通升级流程要求先退出 HDT，再覆盖 `Plugins` 根目录中的旧 `BobCoach.dll`。教程提醒谨慎用户可先把旧 DLL 复制到其他位置作为备份。

常见问题只覆盖三个高频场景：

- 找不到插件目录：使用 Windows 运行窗口或资源管理器地址栏打开 `%AppData%\HearthstoneDeckTracker\Plugins`。
- HDT 插件页面未显示 BobCoach：确认 DLL 位于 `Plugins` 根目录而不是多一层文件夹，并重新启动 HDT。
- 启用失败或需要回退：退出 HDT，恢复备份 DLL；需要完整校验和自动备份时使用高级安装脚本。

安装教程不得建议删除 HDT 用户数据，不读取历史缓存，也不使用 `-RemoveUserData`。

## 测试策略

遵循 TDD，先修改发布包和公开文档合同测试并确认失败，再调整文档与必要的打包逻辑。测试至少验证：

- 中文默认安装流程不要求运行 PowerShell。
- 默认步骤明确复制 `BobCoach.dll` 到 HDT `Plugins` 根目录并在 HDT 中启用。
- 解压后的发行目录根部包含 `BobCoach.dll` 和中文说明。
- `INSTALL.ps1` 仍存在，并被描述为可选的高级校验、备份和回退方式。
- 中文 README、完整安装文档、离线包说明和英文 README 的安装事实不冲突。
- 原有精确文件白名单、manifest、SHA-256、DLL 版本与架构检查继续通过。

实现完成后运行公开文档测试、离线包构建器测试、安装生命周期测试、`npm test`、Release 构建、仓库验证、发布白名单/端点/敏感信息审计及 `git diff --check`。

## 非目标

- 不制作新的 GUI 安装器或修改 HDT。
- 不改变 BobCoach 运行时逻辑、网络边界或用户数据边界。
- 不恢复 Firestone/Zero to Heroes 请求、缓存、专用解析或自动重试。
- 不修改 `.env`、密钥、token 或 CI/CD。
- 不在本轮创建 Tag、GitHub Release 或公开上传发行包。

## 验收标准

一位不了解终端的 Windows 玩家，仅阅读中文主教程即可在几分钟内完成安装并在 HDT 中启用 BobCoach。高级用户仍可选择现有脚本完成完整性校验、自动备份、回退和离线生命周期验证。

# Bob教练 HDT 插件 0.2.0-beta.1

{{PREVIEW_NOTICE}}
> `CURRENT SEASON PREVIEW / NOT FINAL BETA / NOT A FORMAL RELEASE`
>
> 本包是当前赛季抢先验证版，不是最终 Beta，不可冒充正式版。它只供受控的普通用户安装和当前赛季真实对局验证，不代表完整 P0 发布矩阵已经通过，也不得未经授权上传或公开发布。
{{/PREVIEW_NOTICE}}

Bob教练是 Windows x64 上的 Hearthstone Deck Tracker（HDT）酒馆战棋教学插件。本包基于 HDT 1.53.5 和 .NET Framework 4.7.2 构建，不需要Node.js、Visual Studio 或管理员权限。安装过程可以离线完成；炉石登录和真实酒馆战棋对局需要联网，插件启动不依赖 BobCoach 自有远程服务。

Windows 10 22H2 仅保证技术兼容，微软的常规支持已经结束；同时支持 Windows 11 23H2/24H2 x64。暂不支持 32 位 Windows、Windows on ARM 和未知旧版 HDT。

## 安装前

1. 完全退出 Hearthstone Deck Tracker。任务管理器中不应再有 `Hearthstone Deck Tracker` 或 `HearthstoneDeckTracker` 进程。
2. 保持解压目录内 11 个文件完整，不要只复制 DLL，也不要从不同版本的包混合文件。
{{ZIP_HASH_GUIDANCE}}
3. 如果 ZIP 或 DLL 的“属性”窗口显示“此文件来自其他计算机，可能被阻止”，先对原始 ZIP 选择“解除锁定”，再重新解压。也可以在 ZIP 所在目录运行：

```powershell
Unblock-File -LiteralPath .\BobCoach-0.2.0-beta.1-current-season-preview-20260719-win-x64.zip
```

`PREVIEW ZIP HAS NO EXTERNAL SHA-256`：抢先验证包不附带最终发布用的 ZIP 外 `.sha256` 文件。`Get-FileHash` 可用于故障诊断，但不要把未经发布方确认的外部哈希当成最终发布声明：

```powershell
Get-FileHash .\BobCoach-0.2.0-beta.1-current-season-preview-20260719-win-x64.zip -Algorithm SHA256
```
{{/ZIP_HASH_GUIDANCE}}
{{RELEASE_ZIP_HASH_GUIDANCE}}
3. 如果 ZIP 或 DLL 的“属性”窗口显示“此文件来自其他计算机，可能被阻止”，先对原始 ZIP 选择“解除锁定”，再重新解压。也可以在 ZIP 所在目录运行：

```powershell
Unblock-File -LiteralPath .\BobCoach-0.2.0-beta.1-win-x64.zip
```

可在 PowerShell 中核对下载 ZIP 的 SHA-256：

```powershell
Get-FileHash .\BobCoach-0.2.0-beta.1-win-x64.zip -Algorithm SHA256
```

结果应与随 ZIP 提供的同名 `.zip.sha256` 文件一致。
{{/RELEASE_ZIP_HASH_GUIDANCE}}

解压后，`INSTALL.ps1` 会先验证包内 `SHA256SUMS.txt`、全部文件、manifest 和 DLL 版本/架构；任一校验失败都不会写入 HDT 目录。不要绕过校验，应重新取得完整 ZIP。

## 默认安装或升级

完整解压 ZIP，进入能直接看到上述 11 个文件的内层目录。Windows 11 在目录空白处右键选择“在终端中打开”；Windows 10 按住 Shift 并右键，选择“在此处打开 PowerShell 窗口”。然后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

安装、回退和卸载会显示高影响确认提示。看到确认提示时必须明确输入 `Y` 后按 Enter；直接按 Enter 会取消，本次操作不会执行。

默认目标是 `%APPDATA%\HearthstoneDeckTracker\Plugins`。如果系统提示“无法加载，因为在此系统上禁止运行脚本”，仍使用上面的完整命令；`-ExecutionPolicy Bypass` 只对这一次 PowerShell 进程生效，不修改系统执行策略、注册表或全局依赖。

如果默认 HDT 用户目录还不存在，先正常启动并退出一次 HDT，再重新安装。看到 `PASS installed` 或 `PASS upgraded` 才表示安装完成。

升级时，旧DLL会在同一Plugins目录保留为：

```text
BobCoach.dll.backup-<UTC时间>-<旧文件哈希前16位>
```

安装器不自动删除历史备份。

便携版或自定义目录 HDT 必须显式指定它自己的 `Plugins` 目录。该目录的父目录必须存在官方 `Hearthstone Deck Tracker.exe`；同时兼容历史文件名 `HearthstoneDeckTracker.exe`，但两者不得同时存在：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -PluginDirectory "D:\HDT\Plugins"
```

不要把 HDT 安装根目录本身传给 `-PluginDirectory`，路径必须以 `Plugins` 结尾。

## 在 HDT 中启用并确认保持可用

1. 启动 HDT，打开 `Options`。
2. 进入 `Tracker` 下的 `Plugins`，找到 `BobCoach 0.2.0.0`。
3. 如果开关是 `Off`，切换到 `On`；已经是 `On` 时不要重复切换。
4. 关闭 HDT，再重新启动一次，回到同一页面确认 BobCoach 仍为 `On`。
5. HDT 主界面能看到 BobCoach 的入口，且插件没有加载错误时，离线部署和重启保持验证完成。

## 联网真实对局验证

> `USER LOGIN REQUIRED / ONLINE BATTLEGROUNDS MATCH`

1. 登录炉石必须由用户本人操作；Battle.net 账号、验证码、密码、Cookie 或 token 不得交给脚本、插件或其他人。
2. 保持网络连接，进入一场真实的炉石酒馆战棋对局。不要用断网对局验证本包。
3. 在选牌、饰品或战斗阶段确认 BobCoach 界面能够显示，并能随真实对局状态更新。
4. 完成或退出对局后，确认 HDT 没有把 BobCoach 自动禁用，并记录出现问题时的准确时间、HDT/BobCoach 版本和最小错误信息。

## 回退

退出HDT后恢复最新备份：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback
```

便携版或自定义目录 HDT 恢复最新备份：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -PluginDirectory "D:\HDT\Plugins"
```

恢复指定备份：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -BackupPath "$env:APPDATA\HearthstoneDeckTracker\Plugins\BobCoach.dll.backup-..."
```

便携版或自定义目录 HDT 恢复指定备份时，备份也必须位于同一个 `Plugins` 目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -PluginDirectory "D:\HDT\Plugins" -BackupPath "D:\HDT\Plugins\BobCoach.dll.backup-..."
```

回退前的当前DLL也会被保留为新备份。回退不会删除原备份。

## Power.log与log.config

发现、饰品和部分畸变提示依赖Power.log。安装器不会修改Hearthstone的`log.config`。插件加载后，如发现配置不完整，请使用HDT中的“Bob教练”按钮查看目标路径、完整变更和用途，并在确认后由插件写入。

拒绝修改、文件无写权限或配置缺失时，插件仍应加载，但依赖Power.log的功能会失败关闭并给出提示。修改后需要重启炉石，而不只是重启HDT。卸载不会恢复或删除共享`log.config`。

## 常见问题

### 安装失败或提示完整性校验失败

- 确认 HDT 已完全退出，解压目录中正好有本说明列出的 11 个文件。
- 不要单独替换 `BobCoach.dll`、`manifest.json` 或 `SHA256SUMS.txt`，也不要混用旧包文件。
- 重新取得完整 ZIP，先解除 ZIP 的 Windows 锁定，再解压到一个新目录并重新运行安装命令。
- 默认目录不存在时，先启动并退出一次 HDT；便携版必须传入实际的 `Plugins` 路径。
- 不要通过改脚本、删哈希行或关闭 Windows 安全功能来绕过失败。

### HDT 中没有出现 BobCoach

- 在 PowerShell 中确认安装结果以 `PASS installed` 或 `PASS upgraded` 开头。
- 确认 `BobCoach.dll` 实际位于当前 HDT 使用的 `Plugins` 目录，而不是另一个 HDT 副本。
- 打开 DLL 的“属性”检查是否有“解除锁定”；若有，退出 HDT，解除锁定后重新运行安装器。
- 完全退出并重启 HDT，然后到 `Options > Tracker > Plugins` 再检查一次。
- 仍未出现时，保留安装器完整错误文字和 HDT 日志中的 BobCoach 加载错误；不要上传完整私人对局日志。

### 旧版本仍在工作或升级后异常

- 不要手工把新 DLL 覆盖到多个 HDT 目录。对当前实际使用的 `Plugins` 目录重新运行安装命令。
- `PASS upgraded` 会把旧 DLL 留为 `BobCoach.dll.backup-*`，它不会和当前 `BobCoach.dll` 同时加载。
- 重启 HDT 后仍异常时，先按下方命令回退到最新备份；如需指定版本，使用完整的 `-BackupPath`。

### PowerShell 脚本被系统拦截

- 使用本文给出的 `powershell -NoProfile -ExecutionPolicy Bypass -File ...` 完整命令。
- 该方式只影响当前进程，不需要管理员权限，也不会永久降低系统安全策略。
- 如果 Windows 或安全软件明确隔离了文件，先核对包来源，不要关闭安全软件强行运行。

## 卸载

退出HDT后，默认只删除当前`BobCoach.dll`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1
```

便携版或自定义目录 HDT：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -PluginDirectory "D:\HDT\Plugins"
```

默认保留：

- Plugins目录中的`BobCoach.dll.backup-*`；
- `%APPDATA%\bob-coach`下的配置、日志、回放和玩家档案；
- Hearthstone共享`log.config`。

需要同时删除Bob教练用户数据时，必须显式运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -RemoveUserData
```

便携版或自定义目录 HDT 同时删除 BobCoach 用户数据：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -PluginDirectory "D:\HDT\Plugins" -RemoveUserData
```

该命令仍不删除DLL备份或`log.config`。确认不再需要回退后，可在HDT退出状态下手工删除Plugins目录中的`BobCoach.dll.backup-*`。

## 隐私、数据来源和支持

插件核心在本机处理游戏状态，不自动上传日志、回放或玩家档案。完整写入路径、只读网络端点、数据来源、第三方边界和支持方式见本包中的`PRIVACY.md`、`DATA_SOURCES.md`、`NOTICE`和`SUPPORT.md`。

公开报告问题时只提供最小、已脱敏的复现材料，不要上传完整Power.log、私人回放、账号标识、密钥或含用户名的绝对路径。

## 当前已知限制

{{PREVIEW_LIMIT}}
- `current-season-preview-20260719` 是当前赛季抢先验证版，不是最终 Beta，不含最终发布用 ZIP 外 SHA-256。
{{/PREVIEW_LIMIT}}
- 自动验证覆盖包完整性、安装、升级、卸载、回退和隔离 HDT 发现；其他普通用户的显卡、缩放、HDT 皮肤及真实对局组合仍需继续收集结果。
- 炉石及真实酒馆战棋对局必须联网，登录只能由用户本人完成。
- 插件不依赖 BobCoach 自有远程服务启动；已披露的第三方只读数据源不可用时，相关增强数据会失败关闭，本地基线功能应继续工作。
- Power.log 相关功能依赖正确的 `log.config`。插件会先说明拟议变更，拒绝或写入失败不会阻止插件加载，但相应对局功能可能不可用。

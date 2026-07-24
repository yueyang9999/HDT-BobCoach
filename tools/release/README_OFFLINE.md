# Bob教练 HDT 插件 1.0.0 本地发布候选

{{LOCAL_CANDIDATE_NOTICE}}
> `LOCAL RELEASE CANDIDATE / NOT A PUBLIC GITHUB RELEASE`
>
> 本段仅适用于 `1.0.0` 本地发布候选；该包不是 GitHub 公开 Release，不可冒充已公开发布的正式包。它只供受控的本地安装和当前赛季真实对局验证，不代表完整 P0 发布矩阵或 Windows 11 24H2 最终实机 smoke 已经通过，也不得未经单独授权上传或公开发布。
{{/LOCAL_CANDIDATE_NOTICE}}

Bob教练是 Windows x64 上的 Hearthstone Deck Tracker（HDT）酒馆战棋教学插件。本包基于 HDT 1.53.5 和 .NET Framework 4.7.2 构建，不需要Node.js、Visual Studio 或管理员权限。安装过程可以离线完成；炉石登录和真实酒馆战棋对局需要联网，插件启动不依赖 BobCoach 自有远程服务。

`1.0.0` 本地候选尚未完成 Windows 11 24H2 x64 + HDT 1.53.5 最终实机 smoke。Windows 10 22H2 x64 是目标兼容环境，当前仅确认技术兼容，尚未完成专用实机验证。暂不支持 32 位 Windows、Windows on ARM 和未知旧版 HDT。

## 安装前

1. 完全退出 Hearthstone Deck Tracker。任务管理器中不应再有 `Hearthstone Deck Tracker` 或 `HearthstoneDeckTracker` 进程。
2. 完整解压 ZIP，保持包内 17 个文件完整，不要从不同版本的包混合文件。
3. 双击解压目录根部的 `安装教程.html`。它可以离线打开，四个步骤都在文字后附有对应图片。
{{ZIP_HASH_GUIDANCE}}
4. 如果 ZIP 或 DLL 的“属性”窗口显示“此文件来自其他计算机，可能被阻止”，先对原始 ZIP 选择“解除锁定”，再重新解压。也可以在 ZIP 所在目录运行：

```powershell
Unblock-File -LiteralPath .\BobCoach-0.2.0-beta.1-current-season-preview-20260719-win-x64.zip
```

`PREVIEW ZIP HAS NO EXTERNAL SHA-256`：抢先验证包不附带最终发布用的 ZIP 外 `.sha256` 文件。`Get-FileHash` 可用于故障诊断，但不要把未经发布方确认的外部哈希当成最终发布声明：

```powershell
Get-FileHash .\BobCoach-0.2.0-beta.1-current-season-preview-20260719-win-x64.zip -Algorithm SHA256
```
{{/ZIP_HASH_GUIDANCE}}
{{RELEASE_ZIP_HASH_GUIDANCE}}
4. 如果 ZIP 或 DLL 的“属性”窗口显示“此文件来自其他计算机，可能被阻止”，先对原始 ZIP 选择“解除锁定”，再重新解压。也可以在 ZIP 所在目录运行：

```powershell
Unblock-File -LiteralPath .\BobCoach-1.0.0-win-x64.zip
```

可在 PowerShell 中核对下载 ZIP 的 SHA-256：

```powershell
Get-FileHash .\BobCoach-1.0.0-win-x64.zip -Algorithm SHA256
```

结果应与随 ZIP 提供的同名 `.zip.sha256` 文件一致。
{{/RELEASE_ZIP_HASH_GUIDANCE}}

普通玩家只需按 `安装教程.html` 手动复制 `BobCoach.dll` 和相邻的 `BobCoach.dll.sha256`，不需要使用终端。需要自动校验、备份或回退时，再使用后文的可选高级安装。

## 默认安装或升级

### 第 1 步：完全退出 HDT

在 Windows 右下角通知区域找到 HDT 图标，右键选择“退出”。不要只关闭主窗口，因为 HDT 可能仍在后台运行。任务管理器中不应再有 `Hearthstone Deck Tracker` 或 `HearthstoneDeckTracker` 进程。

### 第 2 步：打开插件目录

按 `Win + R`，粘贴 `%APPDATA%\HearthstoneDeckTracker\Plugins`，然后点“确定”。如果目录不存在，先正常启动并退出一次 HDT，再重试。HDT 的用户插件目录不随程序安装位置变化；不要改用 HDT 程序安装目录下的 `Plugins`。

### 第 3 步：复制 DLL 和校验文件

回到完整解压后的安装包，把 `BobCoach.dll` 和 `BobCoach.dll.sha256` 一起复制到刚打开的 `Plugins` 根目录。两个文件必须来自同一个安装包并保持相邻；缺少校验文件或两者不匹配时，插件会拒绝启动。升级时同时替换两个文件。不要新建 `BobCoach` 子文件夹，也不要把教程、图片、脚本或其他说明文件复制进 HDT。

### 第 4 步：启动并启用 BobCoach

重新启动 HDT，打开 `Options > Tracker > Plugins`，找到 BobCoach 并勾选启用。关闭并重启一次 HDT，再回到同一页面确认 BobCoach 仍为启用状态。HDT 主界面能看到 BobCoach 入口，且插件没有加载错误时，手动安装完成。

如果插件列表没有 BobCoach，先确认两个文件都位于插件目录根部且 HDT 已完全退出。右键原始 ZIP 打开“属性”；若底部有“解除锁定”，勾选后重新解压、复制，再重启 HDT。仍无法加载时，只记录最小错误信息，不要上传完整私人对局日志。

## 安装后确认

插件页面应显示 `BobCoach 1.0.0.0`，开关为 `On`。Bob Coach 只从上述 AppData 插件目录加载 DLL；同一个版本不要复制到多个 HDT 目录。手动覆盖升级不会自动创建旧 DLL 备份，如需自动备份和回退，请使用后文的可选高级安装。

## 联网真实对局验证

> `USER LOGIN REQUIRED / ONLINE BATTLEGROUNDS MATCH`

1. 登录炉石必须由用户本人操作；Battle.net 账号、验证码、密码、Cookie 或 token 不得交给脚本、插件或其他人。
2. 保持网络连接，进入一场真实的炉石酒馆战棋对局。不要用断网对局验证本包。
3. 在选牌、饰品或战斗阶段确认 BobCoach 界面能够显示，并能随真实对局状态更新。
4. 完成或退出对局后，确认 HDT 没有把 BobCoach 自动禁用，并记录出现问题时的准确时间、HDT/BobCoach 版本和最小错误信息。

## 可选高级安装

普通玩家无需执行本节。需要校验包内 `SHA256SUMS.txt`、manifest、DLL 与 `BobCoach.dll.sha256` 的绑定、DLL 版本与 x64 架构，并在升级时自动保留旧 DLL 时，可在完整解压目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

安装、升级和回退会显示确认提示，必须明确输入 `Y` 后按 Enter；直接按 Enter 会取消。脚本只接受 `%APPDATA%\HearthstoneDeckTracker\Plugins`，不会把教程或图片部署到 HDT。看到 `PASS installed` 或 `PASS upgraded` 才表示脚本安装完成。升级时，旧 DLL 会保留为 `BobCoach.dll.backup-<UTC时间>-<旧文件哈希前16位>`。

## 回退

退出HDT后恢复最新备份：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback
```

恢复指定备份：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -BackupPath "$env:APPDATA\HearthstoneDeckTracker\Plugins\BobCoach.dll.backup-..."
```

回退前的当前DLL也会被保留为新备份。回退不会删除原备份。

## Power.log与log.config

发现、饰品和部分畸变提示依赖Power.log。安装器不会修改Hearthstone的`log.config`。插件加载后，如发现配置不完整，请使用HDT中的“Bob教练”按钮查看目标路径、完整变更和用途，并在确认后由插件写入。

拒绝修改、文件无写权限或配置缺失时，插件仍应加载，但依赖Power.log的功能会失败关闭并给出提示。修改后需要重启炉石，而不只是重启HDT。卸载不会恢复或删除共享`log.config`。

## 常见问题

### 安装失败或提示完整性校验失败

- 确认 HDT 已完全退出，解压目录中正好有本说明列出的 17 个文件。
- 不要单独替换 `BobCoach.dll`、`BobCoach.dll.sha256`、`manifest.json` 或 `SHA256SUMS.txt`，也不要混用旧包文件。
- 重新取得完整 ZIP，先解除 ZIP 的 Windows 锁定，再解压到一个新目录并重新运行安装命令。
- 默认目录不存在时，先启动并退出一次 HDT；不要改用 HDT 程序目录下的 `Plugins`。
- 不要通过改脚本、删哈希行或关闭 Windows 安全功能来绕过失败。

### HDT 中没有出现 BobCoach

- 确认 `BobCoach.dll` 与 `BobCoach.dll.sha256` 已一起复制到 AppData 插件目录根部，不在额外子文件夹中。
- 确认两个文件实际位于 `%APPDATA%\HearthstoneDeckTracker\Plugins`，且来自同一个安装包，而不是 HDT 程序目录或另一个副本。
- 打开 DLL 的“属性”检查是否有“解除锁定”；若有，退出 HDT，解除锁定后重新运行安装器。
- 完全退出并重启 HDT，然后到 `Options > Tracker > Plugins` 再检查一次。
- 仍未出现时，保留安装器完整错误文字和 HDT 日志中的 BobCoach 加载错误；不要上传完整私人对局日志。

### 旧版本仍在工作或升级后异常

- 不要手工把新 DLL 覆盖到多个 HDT 目录。对 `%APPDATA%\HearthstoneDeckTracker\Plugins` 重新运行安装命令。
- `PASS upgraded` 会把旧 DLL 留为 `BobCoach.dll.backup-*`，它不会和当前 `BobCoach.dll` 同时加载。
- 重启 HDT 后仍异常时，先按下方命令回退到最新备份；如需指定版本，使用完整的 `-BackupPath`。

### PowerShell 脚本被系统拦截

- 使用本文给出的 `powershell -NoProfile -ExecutionPolicy Bypass -File ...` 完整命令。
- 该方式只影响当前进程，不需要管理员权限，也不会永久降低系统安全策略。
- 如果 Windows 或安全软件明确隔离了文件，先核对包来源，不要关闭安全软件强行运行。

## 卸载

退出HDT后，默认同时删除当前 `BobCoach.dll` 和 `BobCoach.dll.sha256`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1
```

卸载目标固定为 `%APPDATA%\HearthstoneDeckTracker\Plugins\BobCoach.dll`，不随 HDT 程序安装位置变化。卸载器会拒绝 HDT 程序目录下的 `Plugins`。

默认保留：

- Plugins目录中的`BobCoach.dll.backup-*`；
- `%APPDATA%\bob-coach`下的配置、日志、回放和玩家档案；
- Hearthstone共享`log.config`。

需要同时删除Bob教练用户数据时，必须显式运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -RemoveUserData
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

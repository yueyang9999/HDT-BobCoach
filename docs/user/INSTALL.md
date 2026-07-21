# 安装

## 当前状态

GitHub Release 尚未获得发布授权。不要把仓库源码、CI 产物或第三方转发文件当作可安装发布包。本说明只适用于项目所有者明确授权的测试包，或未来获得授权后从本仓库 Releases 下载的完整 ZIP。

## 要求

- Windows 10 22H2 x64，或 Windows 11 23H2/24H2 x64；
- Hearthstone Deck Tracker (HDT) `1.53.5` x64；
- 系统提供的 .NET Framework 4.8 或 4.8.1 运行时；
- 标准用户权限。

安装不需要 Node.js、Visual Studio、管理员权限或 Bob Coach 自有在线服务。HDT 和 Hearthstone 由用户自行合法安装。

## 安装完整包

1. 完全退出 HDT，确认任务管理器没有 `HearthstoneDeckTracker` 或 `Hearthstone Deck Tracker` 进程。
2. 在可信来源取得完整 ZIP，并保留 ZIP 内所有文件；不要单独复制 DLL 或混用不同版本的文件。
3. 已授权正式包应同时提供 `BobCoach-<version>-win-x64.zip.sha256`。核对哈希后再解压：

```powershell
Get-FileHash .\BobCoach-<version>-win-x64.zip -Algorithm SHA256
```

4. 进入解压后的内层目录，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

5. 输入 `Y` 确认写入。看到 `PASS installed` 或 `PASS upgraded` 才表示完成。
6. 启动 HDT，在 `Options > Tracker > Plugins` 中确认 `BobCoach` 已启用；重启一次 HDT 后再次确认。

默认目标是 `%APPDATA%\HearthstoneDeckTracker\Plugins`。如果该目录不存在，先正常启动并退出一次 HDT。

HDT 的用户插件目录不随程序安装位置变化；便携版或自定义程序目录也使用上述 AppData 路径。不要把 DLL 安装到 HDT 程序目录下的 `Plugins`。`-PluginDirectory` 只接受当前 `%APPDATA%` 对应的同一目录，通常应省略。安装器会验证包完整性、清单、DLL 身份、x64 架构和目标路径，验证失败时不会写入插件目录。

## Power.log

部分功能需要 Hearthstone 的 `Power.log`。安装器不会修改 `log.config`。当插件提示配置不完整时，只能在 HDT 内通过“Bob 教练”按钮查看目标路径、完整变更和用途后，再明确确认写入。拒绝或写入失败不会阻止插件加载，但相关功能会失败关闭。

详见 [升级](UPGRADE.md)、[回退](ROLLBACK.md)、[卸载](UNINSTALL.md) 和 [故障排查](TROUBLESHOOTING.md)。

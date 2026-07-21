# 卸载

本流程适用于从本仓库 GitHub Releases 安装的 Bob Coach。默认卸载保留用户数据和回退备份。

先完全退出 HDT，再运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1
```

卸载目标固定为 `%APPDATA%\HearthstoneDeckTracker\Plugins\BobCoach.dll`，不随 HDT 程序安装位置变化。卸载器会拒绝 HDT 程序目录下的 `Plugins`。

默认只删除当前 `BobCoach.dll`。以下内容会保留：

- `BobCoach.dll.backup-*` 回退备份；
- `%APPDATA%\bob-coach` 下的本地配置、日志、回放和档案；
- Hearthstone 共享的 `log.config`。

确认不再需要本地数据时，使用显式参数：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -RemoveUserData
```

该操作需要确认，拒绝删除目录联接，也不会删除外部 `BOB_COACH_DATA_ROOT`。若曾配置该环境变量，需自行检查其实际目录。卸载后可启动 HDT，确认插件列表不再出现 BobCoach；不要通过删除共享 `log.config` 来卸载。

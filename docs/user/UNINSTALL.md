# 卸载

GitHub Release 尚未获授权发布。本流程适用于已获授权安装的本地插件。

先完全退出 HDT，再运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1
```

对于便携版或自定义 HDT：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -PluginDirectory "D:\HDT\Plugins"
```

默认只删除当前 `BobCoach.dll`。以下内容会保留：

- `BobCoach.dll.backup-*` 回退备份；
- `%APPDATA%\bob-coach` 下的本地配置、日志、回放和档案；
- Hearthstone 共享的 `log.config`。

确认不再需要本地数据时，使用显式参数：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\UNINSTALL.ps1 -RemoveUserData
```

该操作需要确认，拒绝删除目录联接，也不会删除外部 `BOB_COACH_DATA_ROOT`。若曾配置该环境变量，需自行检查其实际目录。卸载后可启动 HDT，确认插件列表不再出现 BobCoach；不要通过删除共享 `log.config` 来卸载。

# 回退

GitHub Release 尚未获授权发布。本流程只处理由已授权安装包创建的本地备份。

1. 完全退出 HDT。
2. 在原安装包目录运行，恢复最新备份：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback
```

自定义 HDT 目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -PluginDirectory "D:\HDT\Plugins"
```

恢复指定备份时，备份必须仍在同一个 `Plugins` 目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -BackupPath "$env:APPDATA\HearthstoneDeckTracker\Plugins\BobCoach.dll.backup-..."
```

回退也需要输入 `Y` 确认。当前 DLL 会先保留为一个新备份，既有备份不会被删除。成功后启动 HDT，确认插件启用且没有加载错误。

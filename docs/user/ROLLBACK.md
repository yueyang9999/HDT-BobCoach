# 回退

本流程只处理由官方 Release 安装包创建的本地备份，不会从网络下载旧版本。

1. 完全退出 HDT。
2. 在原安装包目录运行，恢复最新备份：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback
```

回退目录固定为 `%APPDATA%\HearthstoneDeckTracker\Plugins`，不随 HDT 程序安装位置变化。恢复指定备份时，备份必须仍在该目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -Rollback -BackupPath "$env:APPDATA\HearthstoneDeckTracker\Plugins\BobCoach.dll.backup-..."
```

回退也需要输入 `Y` 确认。当前 DLL 会先保留为一个新备份，既有备份不会被删除。安装器会为恢复后的 DLL 重新生成相邻的 `BobCoach.dll.sha256`，保证二者匹配。成功后启动 HDT，确认插件启用且没有加载错误。

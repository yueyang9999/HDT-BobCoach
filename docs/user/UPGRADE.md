# 升级

GitHub Release 尚未获授权发布。本流程仅适用于明确授权的测试包或未来授权的正式包。

1. 完全退出 HDT。
2. 为新包验证来源和 SHA-256，完整解压，不要从两个版本混合文件。
3. 在新包目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

4. 输入 `Y` 确认。成功时输出 `PASS upgraded`。
5. 启动 HDT，在 `Options > Tracker > Plugins` 中确认 BobCoach 已启用，并重启 HDT 一次验证保持启用。

默认目录为 `%APPDATA%\HearthstoneDeckTracker\Plugins`。便携版或自定义 HDT 使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1 -PluginDirectory "D:\HDT\Plugins"
```

升级前的 `BobCoach.dll` 会保留在同一目录，名称为 `BobCoach.dll.backup-<UTC 时间>-<哈希前缀>`。安装器不会自动清理备份。出现加载或行为异常时，按 [回退](ROLLBACK.md) 恢复，而不是手工覆盖 DLL。

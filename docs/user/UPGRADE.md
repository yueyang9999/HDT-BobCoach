# 升级

新版本只从本仓库 [GitHub Releases](https://github.com/yueyang9999/HDT-BobCoach/releases) 下载。不要使用源码、CI 产物或第三方转发文件升级。

1. 完全退出 HDT。
2. 为新包验证来源和 SHA-256，完整解压。确认 `BobCoach.dll` 与 `BobCoach.dll.sha256` 来自同一个包，不要从两个版本混合文件。
3. 在新包目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

4. 输入 `Y` 确认。成功时输出 `PASS upgraded`。
5. 启动 HDT，在 `Options > Tracker > Plugins` 中确认 BobCoach 已启用，并重启 HDT 一次验证保持启用。

升级目录固定为 `%APPDATA%\HearthstoneDeckTracker\Plugins`，不随 HDT 程序安装位置变化。不要升级 HDT 程序目录下的 `Plugins`；安装器会拒绝该路径。

升级前的 `BobCoach.dll` 会保留在同一目录，名称为 `BobCoach.dll.backup-<UTC 时间>-<哈希前缀>`。新 DLL 与相邻的 `BobCoach.dll.sha256` 会成对部署；安装器不会自动清理备份。出现加载或行为异常时，按 [回退](ROLLBACK.md) 恢复，而不是手工覆盖 DLL。

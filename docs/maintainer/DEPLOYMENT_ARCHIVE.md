# 本地部署与 DLL 归档

## 目标

HDT 真实插件目录只接受从当前源码重新构建并通过离线包完整性校验的 Release DLL。仓库或历史构建目录中的旧 `BobCoach.dll` 不得直接复制到插件目录。

真实插件目录位于：

```text
%APPDATA%\HearthstoneDeckTracker\Plugins
```

每次部署前必须关闭 HDT，且插件目录链路上不允许 junction、symlink 或 reparse point。

## 真实部署

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\software\game\HDT-V2.7.0\HDT'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\deploy_local_release.ps1
```

脚本默认行为：

1. 校验 HDT 1.56.1.0 构建引用。
2. 校验目标为 HDT AppData `Plugins` 且链路无 reparse point。
3. 校验 HDT 已退出。
4. 从当前 HEAD 执行 Release x64 构建。
5. 以 approved candidate 身份生成正式离线 ZIP 并校验包完整性。
6. 在部署前把插件目录中所有 `BobCoach.dll*` 文件归档到外部根目录。
7. 解压离线包并调用其中 `INSTALL.ps1` 完成真实替换与 sidecar 更新。

## 归档约定

外部归档根目录：

```text
E:\HDT-BobCoach-evidence\dll-archive
```

每次部署生成一个目录：

```text
<UTC 时间戳>-<HEAD 8 位 commit>
```

同秒冲突时追加数字后缀，绝不覆盖既有目录。目录内包含部署前插件目录全部 `BobCoach.dll`、sidecar 与历史 backup，并写入 `manifest.json`：

```json
{
  "schemaVersion": 1,
  "capturedAtUtc": "...",
  "commit": "...",
  "sourceDllPath": "...",
  "pluginVersion": "1.1.0",
  "pluginSha256": "...",
  "entries": []
}
```

`entries` 中当前 DLL/sidecar 标记为 `current`，历史 backup 标记为 `backup`。归档只追加、不删除，不进入 Git。

## 历史 DLL 管理

- `INSTALL.ps1` 会在插件目录中保留升级前 `BobCoach.dll.backup-*`，不得删除。
- sidecar backup 同样保留。
- 外部归档是部署前快照，插件目录历史 backup 是插件内回退资产，两者都保留。
- 需要回退时使用离线包中的 `INSTALL.ps1 -Rollback`。

## 验证

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\software\game\HDT-V2.7.0\HDT'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\run_package_tests.ps1
```

该套件会重新构建 Release DLL，并验证缺失候选、错误插件目录、运行中 HDT、归档 manifest、历史 backup 保留和不覆盖归档等契约。

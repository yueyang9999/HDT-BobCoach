# 构建与验证

## 前置条件

- Windows x64；
- .NET Framework 4.7.2 Developer Pack；
- Node.js（仅执行无第三方依赖的合同测试）；
- HDT `1.53.5.0` x64 安装目录，含 `HearthstoneDeckTracker.exe`、`HearthDb.dll` 和 `Newtonsoft.Json.dll`。

HDT 程序集由用户本地提供，不能提交或打包进仓库。

## 构建

设置 HDT 路径后执行：

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\HDT'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\build_release.ps1 `
  -HdtDirectory $env:BOBCOACH_HDT_DIR `
  -OutputDirectory "$env:TEMP\bobcoach-build" `
  -Force
```

输出目录只用于本地验证，不能提交。构建脚本会验证项目、HDT 基线、x64 架构和程序集身份。

## 测试与仓库验证

```powershell
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

`npm test` 不安装 npm 运行时依赖；它执行 Node 合同测试和 PowerShell 测试。`validate_repository.ps1` 是仓库内容、敏感数据、个人路径、大文件、发布身份与包白名单的最终检查。该脚本及 CI 尚在本次仓库准备工作中补齐前，不能把缺失的检查视为通过。

## 离线包

在干净输出目录中构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release\build_offline_package.ps1 `
  -HdtDirectory $env:BOBCOACH_HDT_DIR `
  -OutputDirectory "$env:TEMP\bobcoach-package" `
  -Force
```

包生成与本地验证不等于可发布。GitHub Release 仍需在所有验收完成后取得单独明确授权。

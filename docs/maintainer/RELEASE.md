# 发布流程

## 授权边界

当前只准备公开仓库。GitHub Release 尚未获得授权，维护者不得创建 Release、上传 ZIP、推送包哈希或将 CI 产物宣称为用户可安装版本。

## 发布前条件

1. 干净 checkout 完成 restore、Release x64 构建和全部自动化测试。
2. `tools/build/validate_repository.ps1`、包身份和精确文件白名单检查通过。
3. 生成的 ZIP 与外部 `.sha256` 完整匹配，且没有未跟踪的产物进入 Git。
4. 在一次性 Windows VM overlay 中完成离线安装、升级、回退、卸载和重装验收；基础磁盘不写入。
5. 完成 HDT 兼容性、第三方权利、隐私和数据来源复核。
6. 项目所有者给出针对该最终工件和 GitHub Release 的明确发布授权。

## 生成候选包

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\HDT'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release\build_offline_package.ps1 `
  -HdtDirectory $env:BOBCOACH_HDT_DIR `
  -OutputDirectory "$env:TEMP\bobcoach-release" `
  -Force
```

生成候选工件时保留版本、提交、SHA-256、测试结果和 VM 验收证据于项目证据存储，不提交 DLL、ZIP、日志、截图或 VM 文件。

## 发布后

仅在获授权后，上传严格包白名单生成的 ZIP 与同名 `.sha256`。发布说明必须写明版本、兼容基线、已知限制、安装文档、隐私和数据来源链接。不得使用任何第三方 Logo，不得把赞赏与功能、优先支持或游戏/HDT overlay 关联。

# 更新版本验证流程

## 目标

用与变更风险匹配的验证范围缩短日常反馈时间，同时保留发布候选版本的完整门禁。快速验证只用于开发反馈，不能替代完整自动化测试、发布包验证或必要的 Windows 实机验收。

## 兼容基线与证据边界

- 构建和 CI 基线：HDT `1.53.5.7354` x64、.NET Framework 4.7.2 目标框架。
- 已完成实机验收：Windows 11 24H2 x64 + HDT `1.53.5`。
- 目标兼容环境：Windows 10 22H2 x64；在完成专用 VM smoke test 前，不得对外表述为“已完成 Win10 实机验证”。
- GitHub Actions 的 `windows-2022` runner 只证明 Windows Server 构建与自动化测试通过，不能替代 Win10 或 Win11 最终用户环境。

HDT 版本、目标框架、运行时依赖、插件加载 API 或安装目录契约发生变化时，原有兼容性结论自动失效，必须按高风险变更重新验收。

## 先判定变更等级

| 等级 | 典型变更 | 开发中验证 | 合并前门禁 | VM 验收 |
| --- | --- | --- | --- | --- |
| L0 文档 | 不影响命令、包内容或用户行为的文字修正 | 检查链接和命令 | 仓库验证、`git diff --check` | 不需要 |
| L1 业务逻辑 | 规则、状态机、金币、Power.log 解析、策略和非安装类日志 | 合同测试 + 行为测试 | 完整 `npm test` + 仓库验证 | 最终候选版本做 Win11 smoke，不要求每次提交重跑 |
| L2 构建与包 | 项目文件、资源、版本、manifest、包白名单、构建或打包脚本 | 受影响测试 + package suite | 完整本地门禁 + CI | 最终候选版本做 Win11 安装和加载 smoke |
| L3 安装链路 | AppData 路径、安装、升级、回退、卸载、重装、失败恢复 | package suite | 完整本地门禁 + CI | 在受影响系统完成完整安装生命周期验收 |
| L4 兼容性 | HDT API/版本、.NET/依赖、进程识别、系统权限或 OS 行为 | 完整自动化测试 | 完整本地门禁 + CI | Win10 22H2 与 Win11 24H2 均需 smoke |

变更同时命中多个等级时，按最高等级执行。测试代码、fixture 或门禁本身发生变化时，至少按 L2 处理，不能只运行被修改后的单个测试来证明其正确性。

## 第一阶段：开发中快速反馈

仅当变更未触及构建、包、安装链路、HDT API、运行时依赖或 OS 行为时执行：

```powershell
npm run test:contracts
npm run test:behavior
```

优先先运行与改动直接相关的行为测试，再执行以上两套快速验证。当前 `package.json` 尚未定义 `test:quick`，因此文档使用现有命令，不能假定 `npm run test:quick` 可用。

快速验证失败时立即停止扩大验证范围，先定位并修复根因。不得通过注释测试、放宽断言或添加绕过标记获得通过结果。

## 第二阶段：合并前完整门禁

所有影响代码、测试、构建、包或用户文档的更新，在准备提交或发起合并前执行：

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\HDT'
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

`npm test` 必须完整执行 contract、behavior 和 package suites。`validate_repository.ps1` 必须继续覆盖敏感信息、个人路径、大文件、发布身份、包内容和精确白名单。任一命令失败都不能进入候选包阶段。

2026-07-20 的参考耗时为：本机完整 `npm test` 约 7 分钟，其中 package suite 约 4.75 分钟；GitHub CI 单次约 18 至 19 分钟。该数据只用于规划，不是超时阈值或通过标准。

## 第三阶段：候选包与发布验收

只对准备发布的最终提交生成候选包，不对每次开发提交重复完整 VM 生命周期：

1. 在干净 checkout 上重跑第二阶段全部门禁并等待 GitHub CI 通过。
2. 按 [RELEASE.md](RELEASE.md) 生成一次正式候选 ZIP，记录版本、提交、大小和 SHA-256。
3. 核对 ZIP 实际文件、`manifest.json` 和发布白名单完全一致。
4. 根据变更等级执行下方 VM 矩阵，不复用已经被前一候选版本写入的 overlay。
5. 验收通过后正常关闭 HDT 和 Windows，复核封存基盘与原始 vars 未被写入。
6. 保留必要日志、截图和哈希到项目证据存储；证据不得进入 Git。
7. GitHub Release 仍需项目所有者针对最终提交和候选工件单独明确授权。

### VM 验收矩阵

| 候选版本变化 | Windows 11 24H2 | Windows 10 22H2 |
| --- | --- | --- |
| 仅 L1 业务逻辑 | 最小 smoke | 完成首轮 Win10 基线后，可不在每个版本重复 |
| L2 构建或包内容 | 安装、启动、日志 smoke | 包含 OS 相关内容时执行 |
| L3 安装链路 | 完整安装、升级、回退、默认卸载、重装 | 安装路径、权限或系统行为可能受影响时执行 |
| L4 HDT/.NET/OS 兼容性 | 必须执行 | 必须执行 |

在首次公开声明支持 Windows 10 前，无论候选版本属于哪个等级，都必须至少补做一次 Win10 22H2 x64 smoke test。HDT 基线版本升级后，Win10 和 Win11 的 smoke 都必须重新执行。

### 最小 HDT smoke

1. 使用普通 Windows 用户安装，确认 DLL 位于 `%APPDATA%\HearthstoneDeckTracker\Plugins\BobCoach.dll`。
2. 启动 HDT，确认进程名与安装器检查一致。
3. 确认 HDT 日志包含 `Loading BobCoach` 和 `Enabled BobCoach`。
4. 确认 BobCoach 日志包含 `BobCoach ready`，并检查本次变更对应的核心行为。
5. 正常关闭 HDT，确认进程退出且没有新的错误日志。

### 安装链路完整验收

在最小 smoke 基础上增加：

1. 验证离线安装、同版本重装、升级、回退和失败恢复。
2. 执行默认卸载，确认只删除插件 DLL，并保留 `%APPDATA%\bob-coach` 用户数据。
3. 不使用 `-RemoveUserData` 进行常规发布验收；任何用户数据删除测试必须使用纯合成数据，并另行取得明确删除授权。
4. 重装同一候选包，再次确认 HDT 加载、启用和插件 ready 日志边界。

## 失败处理

- 自动化测试、CI、包白名单、hash 或 VM 日志任一失败，候选版本立即停止推进。
- 修复后从该变更对应等级的第一项门禁重新开始；不得只重跑最后一个失败步骤。
- 候选 ZIP 内容变化后必须生成新 hash，并把它视为新的待验收工件。
- VM 验收失败时保留失败 overlay 和最小证据，不覆盖成功基线，也不修改封存基盘。

## CI 与自动化优化边界

当前 CI 同时监听所有 `push` 和 `pull_request`，同一功能分支可能执行两套相同的完整任务。后续可以独立实施以下优化，但在配置真正合并前不能把它们计入现有能力：

- 仅对 `main` 的 push 和所有 pull request 执行完整 CI，避免功能分支 push 与 PR 重复；
- 增加 concurrency cancellation，取消同一 PR 已过期的运行；
- 增加正式的 `test:quick` 脚本，封装 contract + behavior；
- 让完整测试、Release 构建和离线包构建复用一次可信构建产物，减少重复编译；
- 将 VM smoke 自动化为安装、启动、日志断言、正常关闭和默认卸载检查。

优化不能削弱发布候选版本的 package suite、精确包白名单、hash、用户数据保留或必要 OS smoke 门禁。

## 离线饰品数据审计

来源无关的离线审计工具必须使用显式输入，不得设置用户缓存或仓库历史数据文件的默认路径。自动化验证只使用合成 fixture：

```powershell
node .\tools\build\audit_trinket_registry.js `
  --registry <synthetic-registry.json> `
  --authority <synthetic-authority.json>

node .\tools\build\generate_trinket_stats_diff_report.js `
  --active <synthetic-verified-snapshot.json> `
  --shadow <synthetic-shadow.jsonl> `
  --registry <synthetic-registry.json>
```

未提供 `--output` 时报告只写入 stdout；需要文件时必须显式传入 `--output <path>`。不得在发布验证中把这些工具指向实时用户 `%APPDATA%`、迁移或清理历史缓存，也不得把显式外部输入视为已获批准的数据源。对应契约由 `tests/test_offline_trinket_audit_tools.js` 覆盖。

每次规则事实复核必须记录 HDT/HearthDb build，只接受精确、大小写敏感 `CardId` 和当前引擎可完整表达的语义；不能完整表达的饰品继续失败关闭。维护者快照、卡牌文本和原始第三方响应不得复制进 Git 或发布 ZIP。

beta.2 包构建器不得携带 beta.1 preview DLL 的 size/hash。`tests/test_offline_package_builder.ps1` 必须同时验证正式 ZIP 白名单、hash、原子替换，以及 `-CurrentSeasonPreview` 仅返回历史边界错误。

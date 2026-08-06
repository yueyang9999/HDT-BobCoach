# 构建与验证

## 前置条件

- Windows x64；
- .NET Framework 4.7.2 Developer Pack；
- Node.js（仅执行无第三方依赖的合同测试）；
- HDT `1.54.0.7376` x64 官方发布目录，含 `HearthstoneDeckTracker.exe`、`HearthDb.dll` 和 `Newtonsoft.Json.dll`。

HDT 程序集由用户本地提供，不能提交或打包进仓库。`hdtBaselineVersion` 是官方 `v1.54.0` 发布资产写入的精确程序集版本 `1.54.0.7376`。

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

`npm test` 不安装 npm 运行时依赖；它执行 Node 合同测试和 PowerShell 测试。`validate_repository.ps1` 是仓库内容、敏感数据、个人路径、大文件、发布身份与包白名单的最终检查。CI 在精确 HDT `1.54.0.7376` 基线上执行仓库验证、全部测试、Release 构建和正式离线包构建；独立的 latest compatibility 任务还会在每次 push、pull request 及每日定时运行时解析、校验并构建官方最新稳定版，但不生成正式发布包。每日定时运行会跳过正式基线和离线包任务。任一门禁缺失或失败都不能视为通过。

日常更新应先按 [更新版本验证流程](UPDATE_VALIDATION.md) 判定变更等级：开发中可使用与风险匹配的快速反馈，合并前仍需执行本节完整门禁；只有最终候选版本才进入对应的 VM 验收矩阵。

## 算法证据门禁

算法数据、模型候选和验收报告必须位于仓库外且不能在系统盘。推荐布局如下；这些目录不进入构建输出、安装包或 Git：

```text
<BOBCOACH_DATA_ROOT>/
  inputs/decision-events.jsonl
  labels/scenarios-v1.jsonl
  probes/fallback-scenarios-v1.jsonl
  datasets/pairs-v1.jsonl
  splits/train.jsonl
  splits/validation.jsonl
  splits/holdout.jsonl
  protocols/season-2026-next-effect-v1.json
  models/candidates/
  evidence/linear-v1/
    baseline-predictions.jsonl
    candidate-predictions.jsonl
    latency.json
    safety-probes.json
  reports/linear-v1-acceptance.json
```

工程自测不需要真实数据，只验证生产组合面、确定性输出和 evaluator 的失败关闭：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_algorithm_benchmark_contract.ps1
node .\tests\test_algorithm_evaluation.js
```

`-SelfTest` 使用 fake policy 和合成状态，不代表策略提升。当前 `train_pairwise.js` 的 CLI 只校验外部、版本匹配输入，随后以 `blocked-by-season-data` 退出；它不会生成候选模型。不得把其单元测试内存结果当作生产候选。

仓库内 `tools/model/owner_approval_trusted_keys.json` 是 owner approval 的固定公钥信任根，Task 12 保持空 keyset，因此当前任何证据都不能通过效果门禁。未来只有单独批准的变更才能加入 Ed25519 公钥；owner approval 本身必须是外部 detached-signature artifact，benchmark 和 evaluator 都不能生成它、替换生产信任根或由调用方注入公钥。签名 payload 必须精确绑定 protocol 文件及 canonical checksum、holdout 文件及 canonical checksum、候选模型 payload checksum，label provenance、Shadow comparison、severe-error review 三份 manifest 的完整文件 checksum，以及 latency、safety evidence 的完整文件 checksum。evaluator 还会将同一次读取所得 scenarios、baseline、candidate、latency、safety 内容的 canonical checksum 与评估输入逐项核对。任一签名、checksum、版本 identity 或场景集合不匹配时，evaluator 必须保持 `blocked-by-season-data`、`metrics: null`。

以下真实证据命令仅在至少 300 个去重、真实、可信、版本匹配的专家标注高影响场景、代表性 Shadow 对比和人工 severe-error review 全部具备后运行。`labels/scenarios-v1.jsonl` 每行必须携带一致的 `benchmarkId`、schema、build/season/card-pool identity；完整游戏不能跨 partition。真实性、版本匹配、Shadow 代表性和严重错误复核不得由场景文件内的布尔字段自证，必须分别来自外部 manifest，并与 holdout 精确场景集合及对应 baseline/candidate evidence checksum 对账。阈值必须在打开 holdout 结果前由所有者批准并冻结。所有 CLI 数据路径参数均相对于 `DataRoot`，不得传绝对路径或经过 junction/symlink。

```powershell
$dataRoot = [IO.Path]::GetFullPath($env:BOBCOACH_DATA_ROOT)

# 生成训练 pair；当前训练 CLI 仍会失败关闭且不产出模型。
node .\tools\model\build_pairs.js --data-root $dataRoot `
  --scenarios "labels\scenarios-v1.jsonl" `
  --output "datasets\pairs-v1.jsonl"
node .\tools\model\train_pairwise.js --data-root $dataRoot `
  --input "datasets\pairs-v1.jsonl"

# 只有另行批准并已存在的 checksum-valid 外部候选才可进入 benchmark。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\model\run_policy_benchmark.ps1 `
  -DataRoot $dataRoot `
  -Scenarios "labels\scenarios-v1.jsonl" `
  -SafetyProbes "probes\fallback-scenarios-v1.jsonl" `
  -CandidateModel "models\candidates\linear-v1.json" `
  -EvaluationProtocol "protocols\season-2026-next-effect-v1.json" `
  -OutputDirectory "evidence\linear-v1" `
  -HdtDirectory $env:BOBCOACH_HDT_DIR

node .\tools\model\evaluate_policy.js `
  --data-root $dataRoot `
  --scenarios "labels\scenarios-v1.jsonl" `
  --baseline-evidence "evidence\linear-v1\baseline-predictions.jsonl" `
  --candidate-evidence "evidence\linear-v1\candidate-predictions.jsonl" `
  --latency-evidence "evidence\linear-v1\latency.json" `
  --safety-evidence "evidence\linear-v1\safety-probes.json" `
  --evaluation-protocol "protocols\season-2026-next-effect-v1.json" `
  --owner-approval "approvals\season-2026-next-effect-v1.approval.json" `
  --label-provenance "provenance\season-2026-next-labels-v1.json" `
  --shadow-comparisons "provenance\season-2026-next-shadow-v1.json" `
  --severe-error-reviews "provenance\season-2026-next-severe-errors-v1.json" `
  --output "reports\linear-v1-acceptance.json"
```

当前缺少合格真实证据，因此效果状态保持 `blocked-by-season-data`、`metrics: null`。evaluator 仅在至少 300 个真实、可信、版本匹配标注场景、代表性 Shadow 对比和人工 severe-error review 全部具备后计算指标；无论结果如何都不返回 activation decision。新策略继续保持 Shadow，`legacy-v1.1.0` 保持玩家可见推荐和回退。未来即使效果门禁通过，嵌入模型和切换玩家可见策略也分别需要新的所有者批准与发布变更。

### 新赛季清单

```text
1. Update state extraction and season mechanic semantics.
2. Mark unknown mechanics unsupported until tests define coverage.
3. Keep unified ranking in shadow mode.
4. Label current-season disagreements in high-impact categories.
5. Freeze owner-approved effect thresholds before opening the current-season holdout, then run the effect-acceptance interface.
6. Keep the candidate in Shadow regardless of effect status; embedding and visible takeover each require later, separately approved work.
7. While data is incomplete or any review fails, retain blocked-by-season-data, keep the report external, commit only reusable tools/docs, and leave the model resource absent.
8. Roll back by selecting LegacyOnly and retaining the last approved model.
```

新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。

## 离线包

在干净输出目录中构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release\build_offline_package.ps1 `
  -HdtDirectory $env:BOBCOACH_HDT_DIR `
  -OutputDirectory "$env:TEMP\bobcoach-package" `
  -Force
```

包生成与本地验证不等于可发布。GitHub Release 仍需在所有验收完成后取得单独明确授权。

真实 HDT 插件部署与外部 DLL 归档见 [本地部署与 DLL 归档](DEPLOYMENT_ARCHIVE.md)。

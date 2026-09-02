# HDT-BobCoach 本地工作树与 E 盘分区管理（2026-08-13）

> 状态：已完成盘点与分区实施；git 提交按本方案分批执行（需用户确认）。
> 原则：不删除任何数据、不破坏 git 历史、移动全部可逆（同卷元数据移动）。

## 一、现状诊断

### 1.1 git 工作树

- 最后提交：2026-08-10（`e8ea5fa`），分支 `codex/season14-algorithm-dark-gift-capture`
- **166 项未提交改动**（97 修改 + 69 未跟踪）：8-10 至今 T30-T40 全部工作
  堆积在单一工作树，无主题分区：
  - `tests/` 64 项、`src/BobCoach/` 56 项、`docs/` 32 项、
    `tools/` 8 项、根文件 6 项
- 风险：无法区分"已确认的历史工作"与"进行中的工作"；发布/回退无据可依。

### 1.2 E 盘目录（30+ 顶层，与本项目直接相关 14 个）

| 目录 | 大小/文件 | 实际角色 | 问题 |
|---|---|---|---|
| `HDT-BobCoach` | 0.04GB | 开发 git 仓库 | ✅ 保持 |
| `HDT-BobCoach-evidence` | 2.4GB / 5035 | 运行证据 + 语料 + **3 个 git worktree 挂载** | ✅ 保持（不可移动） |
| `claude_project_hdt` | 20GB | **活跃运行数据**：插件 replay 实时写入
  `local-data\bob-coach\user-data\replays\` + 旧源码仓库 | ✅ 保持（移动会破坏插件数据路径） |
| `claude_project_hdt-p0` | **123GB** | 历史干净克隆（7 套 × 2.4k 文件）+ **VM 验证环境 11984 文件** | 📦 归档登记（体积大，物理不动） |
| `c盘文件迁移` | **47GB** | C 盘迁移备份（app-data/Codex 旧工作区，T37 模拟器源码来源） | 📦 归档登记 |
| `HDT-BobCoach-backups` | 11GB / 1933 | 正式备份库（BACKUP_INDEX + SHA256SUMS） | 📦 移入归档区 |
| `HDT-BobCoach-release` / `-release-candidate` / `-build` / `-validation` / `-domestic-download` | ~0.05GB | 发布产物散落 5 处 | 📦 合并入发布区 |
| `HDT-BobCoach-path-contract-*`（2 个） | 空 | 契约测试历史残留（测试动态生成该名） | 🗑 移入归档区 |
| `HDT-BobCoach-article` / `-social-media` | 56 文件 | 内容物料 | 📦 移入归档区 |
| `claude_project` / `claude_project_hdt-private-state` | 0.3GB / 9 文件 | 更早历史工作区/快照 | 📦 移入归档区 |
| 视频 / xunlei / 夸克下载 / docker / AI素材 / GamePP / tabbit下载 / VS / tmp / .pnpm-store | 180GB+ | 与项目无关 | ⚠️ 建议单独清理（非本项目范围） |

## 二、分区设计（四工作区）

```
E:\HDT-BobCoach\           开发区   git 仓库（源码+文档+测试）——不变
E:\HDT-BobCoach-evidence\  证据区   运行证据/语料/归档 DLL/git worktree——不变
E:\HDT-BobCoach-release\   发布区   （新建）发布产物统一收纳
E:\HDT-BobCoach-archive\   归档区   （新建）备份/物料/历史快照统一收纳
```

设计依据：
1. **开发/发布分离**：源码改动与发布产物不再互相干扰，发布区可整体备份/清理。
2. **证据独立**：证据区含 git worktree（`release-candidates/*/checkout`、
   `verification/*/checkout`），物理移动会破坏 worktree 注册，必须原地保留。
3. **归档只读**：历史克隆/VM/备份统一收纳，入口单一，便于日后整体迁移或清理
   （如 p0 的 123GB 可整体搬到冷存储）。
4. **活跃数据不动**：`claude_project_hdt` 是插件 replay 的实时写入路径
   （文档多处引用 `local-data\bob-coach\user-data\replays\`），移动即破坏。

## 三、实施记录（2026-08-13）

### 3.1 发布区合并（同卷移动，瞬间完成）

```text
E:\HDT-BobCoach-release\
  ├─ release\            ← E:\HDT-BobCoach-release（7 文件）
  ├─ release-candidate\  ← E:\HDT-BobCoach-release-candidate（161 文件）
  ├─ build\              ← E:\HDT-BobCoach-build（3 文件）
  ├─ validation\         ← E:\HDT-BobCoach-validation（4 文件）
  └─ domestic-download\  ← E:\HDT-BobCoach-domestic-download（15 文件）
```

### 3.2 归档区合并

```text
E:\HDT-BobCoach-archive\
  ├─ backups\            ← E:\HDT-BobCoach-backups（11GB，含 SHA256SUMS）
  ├─ article\            ← E:\HDT-BobCoach-article（38 文件）
  ├─ social-media\       ← E:\HDT-BobCoach-social-media（18 文件）
  ├─ path-contract-*\    ← 2 个空残留目录
  ├─ claude_project\     ← E:\claude_project（0.29GB 历史工作区）
  └─ claude_project_hdt-private-state\ ← 历史快照（9 文件）
```

### 3.3 登记不动（体积大或活跃，仅文档登记）

| 路径 | 原因 | 后续建议 |
|---|---|---|
| `E:\claude_project_hdt` | 插件 replay 活跃写入 | 长期：插件数据根迁移到 evidence 后归档 |
| `E:\claude_project_hdt-p0`（123GB） | VM/克隆证据，体积大 | 可整体搬到冷盘/压缩 |
| `E:\c盘文件迁移`（47GB） | C 盘迁移备份（含旧 Codex 工作区） | 确认无引用后归档/压缩 |

## 四、git 工作树梳理（166 项改动 → 分批提交建议）

> 本会话不执行提交（AGENTS.md 硬约束）。以下为建议批次，用户确认后执行。

| 批次 | 主题 | 内容 | 建议分支 |
|---|---|---|---|
| A | **RNG 种子化**（独立小改动） | `src/BobCoach/Core/DeterministicRng.cs`（新）、MCTSSearch.cs、CombatSimulator.cs、CardPoolSampler.cs、CombatContext.cs、OpponentModel.cs、BobCoach.csproj、tests/test_release_build_contract.js、test_algorithm_features.ps1、test_algorithm_observed_actions.ps1、test_active_trinket_effects.ps1、test_season14_registries.ps1 | `codex/rng-seeding` |
| B | **T40 模拟器与验收路径**（测试环境，未跟踪目录除外） | `tools/simulation/`（rules/、README、文档）、`tools/evaluate/`（L1 工具链）、docs/maintainer/2026-08-13-effect-acceptance-path-v2.md、2026-08-13-skills-assessment.md | `codex/season14-simulator-l1` |
| C | **T30-T39 历史未提交工作** | src 其余 50 项、tests 其余 60 项、docs 其余 30 项（T34-T36 决策修复、T35 文档） | 按功能拆 2-3 个提交 |
| D | **杂项** | .gitignore（.codegraph）、CHANGELOG.md、README 双语、release_identity.json | 随批次 C 或独立 |

注意事项：
- `tools/simulation` 与 `tools/evaluate` 属未跟踪测试目录（AGENTS.md 明确），
  其中 **rules/、L1 工具链等新文件**建议保留在仓库（属于可发布工具）或维持
  未跟踪状态——需用户拍板。
- 提交前对每批跑 `npm test` + `git diff --check`（verification-before-completion）。

## 五、后续动作

1. 用户确认批次 A-D 后分批提交（建议先 A：独立、已验证 npm test PASS）。
2. 决定 `tools/simulation` / `tools/evaluate` 是否纳入版本管理。
3. 无关大目录（视频/下载/docker 等）建议单独清理，不在本项目范围。
4. 归档区整体冷备计划（p0 123GB + 迁移 47GB）可另行安排。

# HDT-BobCoach Repository Rules

## Scope

This repository contains only the distributable BobCoach plugin for Hearthstone Deck Tracker (HDT), its tests, release tooling, and public documentation.

## Directory Contract

- `src/BobCoach/`: production plugin source and embedded runtime data required by the plugin.
- `tests/`: automated tests and test-only fixtures. Fixtures must be synthetic, minimal, and free of user data.
- `tools/build/`: deterministic build and validation tools.
- `tools/release/`: packaging, manifest, checksum, and release verification tools.
- `tools/migrate/`: one-time allowlist migration helpers retained for provenance and auditability.
- `src/BobCoach/Core/Decision/`: new decision contracts, ranking, confidence, planning, fallback, and explanation runtime code.
- `src/BobCoach/Resources/DecisionModels/`: checksum-validated approved models embedded into the plugin; candidate and rejected models are prohibited.
- `tests/fixtures/algorithm/`: synthetic, minimal algorithm schemas and fixtures only; no replay-derived states or labels.
- `tools/model/`: deterministic offline model tooling: Node.js standard-library-only data preparation/training/evaluation plus the C# 7.3 policy benchmark harness compiled by its PowerShell runner; no downloaded runtime or package dependencies.
- `docs/README.md`: Chinese-first documentation index and authority map for users, maintainers, contributors, policy, and historical records.
- `docs/user/`: installation, upgrade, rollback, uninstall, privacy, and troubleshooting documentation.
- `docs/maintainer/`: architecture, dependency, build, release, and maintenance documentation.
- `docs/design/`: approved repository and product design decisions.
- `docs/superpowers/specs/`: dated, owner-approved design specifications.
- `docs/superpowers/plans/`: dated implementation plans.
- `.github/`: GitHub templates, funding links, dependency automation, and CI workflows.

## Naming

- Product and repository name: `HDT-BobCoach`.
- Plugin assembly and runtime identity: preserve the identity defined by the production project and release manifest.
- Documentation plans and design records use `YYYY-MM-DD` dates.
- Build artifacts use semantic versions and must never use ambiguous names such as `latest-final.zip`.

## Public Content Allowlist

Only commit files required to build, test, package, document, or maintain the HDT plugin. Production source migration must follow the dependency graph rooted at `BobCoach.csproj`, release scripts, and tests.

Do not commit:

- replay XML, raw logs, recordings, crash dumps, user profiles, credentials, or personal paths;
- screenshots, except for cropped and privacy-cleaned final installation-guide images under `docs/user/images/install/` and cropped, privacy-cleaned, compressed final feature-showcase images under `docs/user/images/features/`; raw screenshots, validation screenshots, user-data screenshots, and screenshots outside those exact directories remain prohibited;
- `.env` files, secrets, tokens, passwords, signing material, or private endpoints;
- `.debate`, `.mcp`, `sessions`, `local-data`, VM images, ISO files, validation evidence, caches, or generated packages;
- the historical Python analysis pipeline, Electron overlay, `BobObserver`, or unrelated experiments;
- build outputs such as `bin/`, `obj/`, `artifacts/`, `TestResults/`, coverage output, or packaged ZIP files.

## Engineering Rules

- Keep the plugin usable offline after installation. Do not add mandatory network dependencies.
- Preserve the privacy contract: gameplay data stays local unless a future feature receives explicit design approval.
- All user-facing functionality remains free. Voluntary support must not unlock features or appear inside Hearthstone or the HDT overlay.
- Do not use Blizzard, Hearthstone, HDT, HearthSim, or other third-party logos in funding or promotional material.
- Prefer deterministic scripts with explicit inputs and outputs. Release packaging must enforce an exact file allowlist and publish a SHA-256 checksum.
- Add or update tests for behavior changes. Do not bypass failures with suppression flags or commented-out code.

## Next-Season Algorithm Data Boundary

Until reliable, complete, version-matched next-season data is available, algorithm work must follow a conservative data-insufficiency policy:

- Implement only season-independent infrastructure: stable candidates, hard constraints, versioned features, Shadow comparison, model validation, confidence, coverage, OOD detection, deterministic planning, explanations, and legacy fallback.
- Do not invent or infer next-season cards, heroes, spells, trinkets, mechanics, card pools, balance values, composition strength, refresh value, economy curves, weights, or labeled scenarios.
- Mark unknown build, season, card pool, entity, and mechanic inputs explicitly as `unknown`; reduce confidence and fall back to `legacy-v1.1.0` when the decision gate requires it.
- Keep every uncalibrated new policy in `Shadow`. `legacy-v1.1.0` remains the player-visible policy and runtime fallback until effect acceptance passes.
- Treat player actions as observation events only, never as correctness labels or training labels.
- Synthetic and prior-season data may test interfaces, boundaries, determinism, performance, and regressions, but may not support claims about next-season decision quality.
- Claims of improved accuracy, win rate, placement, or decision quality require real, trustworthy, version-matched labeled scenarios. Without sufficient evidence, report only engineering readiness or `blocked-by-season-data`.
- Data-dependent work must implement its interfaces, gates, and fallback path, then remain `blocked-by-season-data`; do not fabricate substitute data.
- Prefer low-cost, locally maintainable implementation. Do not add cloud inference, paid APIs, heavyweight model runtimes, or unnecessary dependencies.

Each algorithm task report must answer:

1. Is the change independent of next-season data?
2. Which assumptions currently lack evidence?
3. How does the system degrade when data is missing?
4. Which tests prove engineering correctness only, not strategy superiority?
5. What calibration remains after real next-season data becomes available?

Algorithm acceptance has two separate gates:

- Engineering acceptance: determinism, performance, compatibility, privacy, fallback, and Shadow recording.
- Effect acceptance: at least 300 trustworthy, version-matched labeled scenarios, Shadow comparison, and human review.

Before effect acceptance passes, the required conclusion is: `新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。`

## Season Data Contract

- All generated registries, datasets, and model artifacts carry contract headers (`schemaVersion`, `season`, `gameBuild`, `cardPoolIdentity`, `source`, `capturedAt`, `sha256`) per `docs/maintainer/SEASON_DATA_CONTRACT.md`; tools implement them in `tools/model/contracts.js`.
- C# rule registries load definitions through data-backed classes (e.g. `TrinketDefinitionRegistry`) with fail-closed coverage checks; do not add new per-card hardcoded rules. Decision behavior (`Resolve`/policy output) stays unchanged until real, version-matched season data lands.
- Before the 2026-08-05 content refresh, do not read or migrate production data files under `%APPDATA%\bob-coach\data\`; generate and test only against explicit synthetic data roots (e.g. `BOB_COACH_DATA_ROOT` in disposable temp directories).
## Simulator Environment Patch Version Control (2026-08-28 固化, 用户明确要求, 违反即投诉)

> 背景: R588 曾用旧补丁回放（firestone build 248348/231720）的卡牌出现频率修改模拟池
> （cards.json 的 is_battlegrounds_pool/tier flag），被用户否决——**非本赛季回放数据会污染模拟池**。
> 此规则为不可违反的硬约束。

1. **主数据源唯一权威**: 模拟器卡池/机制数据（`E:/HDT-BobCoach-evidence/simulation-season14/season14-data/`）
   必须与**主数据源最新单人酒馆模式**对齐:
   - 标准版本 = **gamerhub 36.2.2.249896（build 249896）**（见 knowledge-base/STANDARD_DATASOURCE_GAMERHUB.md）
   - 改动前必须确认所依据的数据源 build 与当前标准一致; 标准版本升级时同步迁移 season14-data
2. **回放数据必须先核对版本再使用**:
   - 任何回放（firestone XML / HDT 日志 / Power.log）使用前必须读取 `<Game buildNumber="...">`（或等价版本字段）
     与日期; build ≠ 当前标准版本 → 该回放**只允许用于行为/运营/决策模式分析**（升级节奏、购买习惯、
     英雄技能使用等不依赖卡池的统计），**禁止用于**:
     - cards.json 的 is_battlegrounds_pool / tier / 卡池成员修改
     - SharedPool 等引擎池/机制权重校准
     - 任何会改变模拟器环境数据（卡池、概率、机制参数）的改动
   - 混用不同 build 回放做统计时必须在产物中标注各 build 的数量
3. **产物版本标注纪律**: 所有分析报告/对比 JSON/知识库条目必须标注其数据源的 build/日期;
   模拟器运行报告已含 rulesSha256/mechanismRegistrySha256（simulate_games.js），保持
4. **卡池/机制改动流程（强制）**: 改模拟池或机制前, 记录三要素——
   ①改了哪个文件 ②依据的数据源及其 build（必须=标准版本）③若用了回放样本, 列出样本 build 分布;
   完成后同 seed 重现验证（防止数据不一致漏改）
5. **违规示例（禁止, R588 教训）**:
   - 用旧补丁回放调整 cards.json 池 flag / 卡牌 tier
   - 用旧补丁回放推断"池成员"直接改 SharedPool / 引擎机制
   - 不标注 build 就混用不同版本回放做卡池统计
6. **数据提取/分析/总结工作流（2026-08-28 用户纪律）**: 任何提取/分析/总结任务,
   先查 `E:/HDT-BobCoach-evidence/simulation-season14/knowledge-base/WORKFLOWS.md`——
   有现成工作流直接用, 没有再总结新的并登记; 禁止每次从头造流程。
   关键工作流速查: 版本核对(_probe_builds.js) / 最新偏好提取(Power.log, _parse_powerlog_pref.js) /
   商店分布对比(_analyze_shop_log.js) / 6seed A/B(_run_rXXX_ab.js) / 偏好×模拟对比(_analyze_pref_sim.js) /
   救助验证(_rescue_verify.js) / 迭代记录(ITERATION_LEARNINGS.md) / 交接(HANDOVER)。

## Validation

Before considering a change complete, run the repository validation commands documented in `docs/maintainer/BUILD.md`. At minimum, validation must cover:

- clean restore and Release build;
- automated tests;
- release identity and package allowlist checks;
- sensitive-file, personal-path, replay-data, and large-file scans;
- `git diff --check`.

For release candidates, additionally verify offline install, upgrade, rollback, uninstall, and reinstall in disposable VM overlays. Never modify sealed VM base disks.

## Git And Release

- Development branches use the `codex/` prefix.
- Do not rewrite public history or force-push.
- GitHub Release publication requires explicit final approval even when repository pushes are authorized.
- Keep the former repository private and archived as a read-only history source; do not copy its full history into this repository.

## Tool Discipline (2026-08-31 修订: 按环境区分, DSH 原生 read 优先)

- **DSH 环境(默认): 原生 read 工具直接使用**(2026-08-31 实测稳定, 大文件/报告 30+ 次无失败)。
  - `文件已变更`/`EIO` 报错 = 文件被并发修改(fs-observation-policy 保护, 正常现象) →
    **重读一次再操作即可, 不是 bug, 不要因此禁用 read**。
  - 乱码 = 文件非 UTF-8(如 GBK) → 改用 pwsh Get-Content 或 tools/read_file.ps1。
- **Codex/run_code 环境(旧规则保留)**: 顶层只能调用 run_code; 无原生 read 时用
  powershell -File tools/read_file.ps1 兜底(需 FullLanguage, 受限沙箱下 [System.IO.File]:: 被拒)。
- 编辑: 先 read/Get-Content 确认锚点再 edit(old_string 精确匹配); 搜索 grep; 写文件 write。

## Deployment Discipline (2026-08-21 固化, 用户投诉: 部署时强杀运行中的 HDT 干扰对局)

- **HDT 正在运行时, 严禁自动 Stop-Process + 自动重启 HDT。** 用户可能正在对局中, 强杀 = 直接打断游戏。这是用户明确投诉过的错误(R230/R232/R233 部署时犯过)。
- 部署前必须先检查 Get-Process HearthstoneDeckTracker:
  - 若在运行 → 暂停部署, 先问用户(是否关闭后部署?)或等用户打完, 绝不自动杀进程。
  - 若未运行 → 可部署, 部署完成后不自动启动 HDT(让用户自己决定何时开)。
- 部署完成后验证 3 目录哈希一致(HDT 未运行时无文件锁, 验证最可靠)。
- 若之前误杀了 HDT, 重新启动前先确认用户不在对局中(可通过 HDT 日志/对局记录判断)。

## Release Deployment SOP（2026-09-03 固化, 用户要求: 部署流程固定成规则文档, 每次自动执行不需要提醒; 重点固化 SHA 文件对应关系）

### 0. 前置: 用户实测反馈闭环
- 用户实测 → 给具体反馈(回合/卡牌/UI现象) → 证据定位根因(回放+Power.log) → 修复 → 单测 → 才部署。
- 没有反馈, 禁止盲目调权重/重构; 所有行为改动必须有回放/Power.log 证据支撑。

### 1. HDT 进程纪律（不可违反）
- HDT 运行时严禁自动杀进程/自动重启; 部署前 Get-Process HearthstoneDeckTracker 必须为空。
- HDT 在运行 → 暂停部署, 请用户手动关闭后再部署; 部署完成后不自动启动 HDT。

### 2. 标准部署命令
```powershell
# 构建(校验 HDT 目录 exe 基线版本)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\build_release.ps1
# 部署: 归档旧版 → 同步 5 目录 DLL+sidecar → 逐一 SHA 核验
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\deploy_local_release.ps1
```

### 3. SHA 文件对应关系（核心规则, 每次部署必须逐目录核验, 不允许凭记忆）
- **每个插件目录只允许一对文件**: `BobCoach.dll` + `BobCoach.dll.sha256`。
- **sidecar 文件格式(必须精确)**: 内容 = `<64位大写SHA256>` + 两个空格 + `BobCoach.dll`, 行尾 CRLF。
  - 缺 `  BobCoach.dll` 后缀或大小写不对 → HDT 完整性校验 FAILED, 插件拒绝启动(2026-08-10 D盘历史事故)。
  - 示例: `7AF4A5569D408F1CA7A00A581E94B0844FF046288F178F603634FBE58E2E9E55  BobCoach.dll`
- **sidecar 的哈希必须 = 同目录 BobCoach.dll 的 SHA-256**, 严禁从不同版本安装包混放配对。
- **部署目标 5 目录(必须同步同一版本 + 同一哈希)**:
  1. `%APPDATA%\HearthstoneDeckTracker\Plugins\`
  2. `%LOCALAPPDATA%\HearthstoneDeckTracker\app-1.54.1\Plugins\`
  3. `%LOCALAPPDATA%\HearthstoneDeckTracker\app-1.54.0\Plugins\`
  4. `D:\software\game\HDT-V2.7.0\HDT\Plugins\`
  5. `D:\software\game\HDT团子版V2.5.9\HDT\Plugins\`
- 核验命令(逐目录执行, 两者哈希必须相等):
```powershell
Get-FileHash '<目录>\BobCoach.dll' -Algorithm SHA256 | Select-Object -ExpandProperty Hash
Get-Content '<目录>\BobCoach.dll.sha256'   # 应输出: <同一SHA256>  BobCoach.dll
```

### 4. 版本与归档
- 每次正式部署前 bump 版本号并全链路同步: release_identity / ReleaseAssemblyInfo.cs / package.json / CHANGELOG / NOTICE / README / INSTALL.html / 测试断言。
- 版本号与 SHA 由脚本从 release_identity.json 派生, 不手写。
- 部署脚本自动把部署前各目录全部 BobCoach.dll* 归档到证据库 dll-archive(时间戳+SHA), 保留可回滚。

### 5. 部署后验证（每次必做, 贴输出）
- 5 目录 DLL 版本一致 + SHA 全等; sidecar 格式含 `  BobCoach.dll` 后缀。
- 归档 manifest 生成成功。
- 记录: 新版本号 + SHA256 前16位 + 归档路径(ITERATION_LEARNINGS.md / HANDOVER)。

### 6. 回滚
- 旧版 DLL 从归档目录找回(含 backup 时间戳+旧SHA)。
- 回滚 = 复制旧 `BobCoach.dll`+`BobCoach.dll.sha256` 回 5 目录, 并重新逐目录核验配对。

## Cleanup

- Generated files belong outside the repository or in ignored directories and must be removable without affecting source.
- Algorithm tools require an explicit external data root outside the repository. Raw replays, labels, datasets, candidate models, acceptance reports, and validation evidence are never written to the repository and must be removable without changing source.
- Validation evidence stays in the project evidence store, not Git.
- Before adding a new top-level directory, update this file with its purpose, naming, and cleanup policy.


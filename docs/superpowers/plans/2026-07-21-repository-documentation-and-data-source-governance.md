# Repository Documentation and Data Source Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立中文优先的统一文档目录，修正当前发布状态冲突，并把实际数据来源、处理边界和待确认授权记录成可审计声明。

**Status:** 2026-07-21 已实施并完成仓库验证；未执行提交、push、PR、Release 或 VM 操作。

**Architecture:** 保留所有现有文件路径，以 `docs/README.md` 作为统一导航层，以根目录政策文件作为合规权威层，以合同测试锁定关键入口和风险声明。文档事实以生产代码为依据；本轮不改变插件运行逻辑、网络行为、打包逻辑或 CI。

**Tech Stack:** Markdown、Node.js 内置模块合同测试、PowerShell 仓库验证、Git 静态检查。

## Global Constraints

- 不移动或删除现有文件，不删除用户数据或验收证据。
- 不运行或清理 VM，不使用 `-RemoveUserData`。
- 不修改插件运行逻辑、网络请求行为、打包逻辑或 CI/CD 配置。
- 不提交、push、合并 PR 或创建 GitHub Release，除非项目所有者另行明确授权。
- `README.md` 保持中文优先；Windows 10 和 Windows 11 使用同一安装包，但只有 Windows 11 24H2 x64 已完成实机验收。
- MIT 只覆盖项目原创代码和项目有权许可的原创材料，不重新许可第三方数据、软件、游戏内容或商标。
- Firestone / Zero to Heroes 的公开应用许可必须标记为待书面确认；免责声明不能替代授权。
- HSReplay 当前保持不接入、不请求、不抓取、不打包、不再分发。
- 外部数据来源失败不能阻止本地基线；未验证数据不能进入生产评分或 UI 排序。

## File Structure

- Create: `docs/README.md` — 中文优先的全仓库文档导航和权威边界说明。
- Modify: `AGENTS.md` — 登记统一目录和设计规格目录的职责。
- Modify: `README.md` — 增加统一文档目录入口，不复制维护流程。
- Modify: `README.en.md` — 增加统一目录入口并说明目录以中文分类。
- Modify: `DATA_SOURCES.md` — 作为数据来源、处理和权限状态的权威登记表。
- Modify: `NOTICE` — 修正已经公开 Release 与旧“未授权发布”声明的冲突。
- Modify: `tests/test_public_documentation_contract.js` — 锁定目录入口、来源端点、授权状态和 Release 表述。
- Existing spec: `docs/superpowers/specs/2026-07-21-repository-documentation-and-data-source-governance-design.md` — 已确认设计，不在实施中重写。

---

### Task 1: 先建立文档规则与失败合同

**Files:**
- Modify: `AGENTS.md`
- Modify: `tests/test_public_documentation_contract.js`

**Interfaces:**
- Consumes: 已确认设计规格和现有 `npm run test:contracts` 流程。
- Produces: `docs/README.md` 的目录契约，以及后续文档必须满足的自动化断言。

- [x] **Step 1: 先更新仓库目录契约**

在 `AGENTS.md` 的 `Directory Contract` 中加入：

```markdown
- `docs/README.md`: Chinese-first documentation index and authority map for users, maintainers, contributors, policy, and historical records.
- `docs/superpowers/specs/`: dated, owner-approved design specifications.
```

保留现有 `docs/superpowers/plans/` 规则，不修改其他工程纪律。

- [x] **Step 2: 为统一目录和政策声明添加合同测试**

在 `tests/test_public_documentation_contract.js` 中读取：

```javascript
const docsIndex = read("docs/README.md");
const dataSources = read("DATA_SOURCES.md");
const notice = read("NOTICE");
```

把 `docs/README.md` 加入现有文档内容循环之外的独立断言，并添加以下精确合同：

```javascript
assert.ok(chineseReadme.includes("[文档目录](docs/README.md)"), "Chinese README must expose the documentation index");
assert.ok(englishReadme.includes("[Documentation index](docs/README.md)"), "English README must expose the documentation index");

for (const heading of ["普通用户", "维护者", "贡献者", "政策与合规", "设计与实施历史", "包内文档模板"]) {
    assert.ok(docsIndex.includes(`## ${heading}`), `documentation index must contain ${heading}`);
}

for (const link of [
    "user/INSTALL.md",
    "maintainer/BUILD.md",
    "../CONTRIBUTING.md",
    "../DATA_SOURCES.md",
    "design/公开产品仓库治理设计_2026-07-20.md",
    "superpowers/plans/2026-07-21-chinese-download-guidance.md",
    "../tools/release/README_OFFLINE.md",
]) {
    assert.ok(docsIndex.includes(`](${link})`), `documentation index must link ${link}`);
}

for (const text of [
    "https://static.zerotoheroes.com/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json",
    "https://api.hearthstonejson.com/v1/<build>/enUS/cards.json",
    "公开应用许可待书面确认",
    "当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据",
    "2026-07-21",
]) {
    assert.ok(dataSources.includes(text), `data source registry must contain ${text}`);
}

assert.ok(notice.includes("v0.2.0-beta.1"), "NOTICE must acknowledge the existing public release");
assert.ok(notice.includes("separate explicit owner authorization"), "NOTICE must require authorization for each future release");
assert.ok(!notice.includes("GitHub Release publication is not authorized"), "NOTICE must not retain the obsolete release statement");
```

- [x] **Step 3: 运行合同测试，确认因文档尚未实施而失败**

Run:

```powershell
npm run test:contracts
```

Expected: FAIL，首个失败应指出 `docs/README.md` 不存在或 README 缺少文档目录入口；不得通过放宽或删除断言绕过失败。

- [x] **Step 4: 审阅 Task 1 diff**

Run:

```powershell
git diff -- AGENTS.md tests/test_public_documentation_contract.js
git diff --check
```

Expected: 只出现目录契约和合同测试改动，`git diff --check` 无输出。

### Task 2: 建立统一文档目录与 README 入口

**Files:**
- Create: `docs/README.md`
- Modify: `README.md`
- Modify: `README.en.md`

**Interfaces:**
- Consumes: Task 1 定义的目录分类和链接合同。
- Produces: 普通用户、维护者、贡献者、政策和历史资料的稳定入口。

- [x] **Step 1: 创建中文优先的统一文档目录**

`docs/README.md` 使用标题 `# HDT-BobCoach 文档目录`，开头明确：

```markdown
本页是仓库文档的统一入口。普通用户应优先阅读安装、升级和故障排查文档；`docs/design/` 与 `docs/superpowers/` 记录历史设计和实施过程，不替代当前操作指南。
```

按以下六个二级标题列出所有现有文档：

```markdown
## 普通用户
## 维护者
## 贡献者
## 政策与合规
## 设计与实施历史
## 包内文档模板
```

链接规则：

- 普通用户：逐项链接 `user/INSTALL.md`、`user/UPGRADE.md`、`user/ROLLBACK.md`、`user/UNINSTALL.md`、`user/TROUBLESHOOTING.md`。
- 维护者：逐项链接 `maintainer/ARCHITECTURE.md`、`maintainer/BUILD.md`、`maintainer/DEPENDENCIES.md`、`maintainer/RELEASE.md`、`maintainer/UPDATE_VALIDATION.md`。
- 贡献者：链接 `../CONTRIBUTING.md`、`../SECURITY.md`、`../AGENTS.md`。
- 政策与合规：链接 `../LICENSE`、`../NOTICE`、`../DATA_SOURCES.md`、`../PRIVACY.md`、`../SUPPORT.md`。
- 设计与实施历史：逐项链接 `design/`、`superpowers/specs/` 和 `superpowers/plans/` 中当前所有 Markdown 文件，并说明它们是历史记录。
- 包内文档模板：只链接 `../tools/release/README_OFFLINE.md`，说明它由构建脚本按预览包或正式包条件生成，不是仓库发布状态的权威来源。

- [x] **Step 2: 在中文 README 增加稳定入口**

在 `README.md` 的语言切换行下方加入：

```markdown
[文档目录](docs/README.md)
```

不改变下载地址、系统兼容状态和中文安装教程入口。

- [x] **Step 3: 在英文 README 增加稳定入口**

在 `README.en.md` 的语言切换行下方加入：

```markdown
[Documentation index](docs/README.md) (Chinese-first, with categorized links to all current documents)
```

不把英文 README 改回默认入口，也不复制完整目录。

- [x] **Step 4: 运行公共文档合同，确认只剩政策登记断言失败**（Tasks 2-3 同批写入，未单独保留中间失败状态；最终合同已覆盖全部断言。）

Run:

```powershell
node .\tests\test_public_documentation_contract.js
```

Expected: FAIL 应来自 `DATA_SOURCES.md` 或 `NOTICE` 的新断言，不再因 `docs/README.md`、分类标题或 README 入口失败。

- [x] **Step 5: 审阅 Task 2 diff 和链接目标**

Run:

```powershell
git diff -- README.md README.en.md docs/README.md
git diff --check
```

Expected: 下载和系统验证措辞保持不变；`git diff --check` 无输出。

### Task 3: 建立数据来源登记并修正发布声明

**Files:**
- Modify: `DATA_SOURCES.md`
- Modify: `NOTICE`

**Interfaces:**
- Consumes: `src/BobCoach/Core/TrinketStatsFetcher.cs`、`TrinketStatsUpdater.cs` 和 `TrinketStatsStore.cs` 的生产事实。
- Produces: 数据来源、处理、权限和授权状态的唯一公开登记，以及准确的 Release 状态声明。

- [x] **Step 1: 将 DATA_SOURCES.md 扩充为来源登记表**

保留现有 MIT 和不打包第三方内容的原则，在文首增加：

```markdown
最后复核日期：2026-07-21

本登记只描述已核对的技术行为和公开资料，不构成法律意见，也不代表已经取得所有第三方许可。免责声明不能替代权利人要求的授权。
```

为 Bob Coach 原创材料、用户安装的 HDT/HearthDb/Hearthstone、Firestone / Zero to Heroes、HearthstoneJSON / HearthSim hsdata、HSReplay 分别建立三级标题。每节用固定字段记录：`所有者与参考`、`用途`、`请求与发送内容`、`响应使用`、`本地缓存与保留`、`是否进入 Git/DLL/ZIP`、`许可或权限依据`、`署名要求`、`授权状态`、`失败行为`。

Firestone 节必须包含：

```markdown
- 精确端点：`https://static.zerotoheroes.com/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json`
- 请求：HTTPS GET；User-Agent 为 `BobCoach/0.2 trinket-stats-readonly`；不发送 Power.log、回放、账号、对手、用户档案或设备标识。
- 限制：响应上限 5 MiB，超时 15 秒；常规检查间隔 6 小时；失败后依次等待 15 分钟、1 小时、6 小时。
- 官方参考：[Firestone Developer resources](https://github.com/Zero-to-Heroes/firestone/wiki/Developer-resources)。公开资料要求发布成果时署名，并要求公开应用联系作者。
- 授权状态：**公开应用许可待书面确认**。公开上线前应取得并归档许可；如无法取得，应停用该运行时来源并保留本地基线。
```

HearthstoneJSON 节必须包含：

```markdown
- 端点模板：`https://api.hearthstonejson.com/v1/<build>/enUS/cards.json`
- 请求：HTTPS GET；不发送日志、回放、账号、对手、用户档案或设备标识。
- 限制：响应上限 64 MiB，超时 45 秒。
- 官方参考：[HearthstoneJSON](https://hearthstonejson.com) 与 [HearthSim hsdata](https://github.com/HearthSim/hsdata)。这些资料说明事实来源和生成方式，但不能自动视为 Bob Coach 获得第三方游戏数据再许可。
- 授权状态：事实来源已披露；第三方数据权利仍受各自权利和条款约束。
```

缓存与失败边界必须明确记录默认目录 `%APPDATA%\bob-coach\data\trinket-stats\`，列出 `active.json`、`previous.json`、`candidate.json`、`health.json`，并声明未验证数据不进入生产评分或 UI 排序；失败时使用同 Build 已验证缓存或本地基线。

HSReplay 节必须原样包含：

```markdown
当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据。
```

- [x] **Step 2: 修正 NOTICE 的 Release 状态**

保留第三方权利和包内容边界，把过期的：

```text
The source repository is public, but GitHub Release publication is not
authorized.
```

替换为准确声明：

```text
The source repository is public and v0.2.0-beta.1 has been published as a
GitHub Release. Every future GitHub Release, upload, or public distribution
still requires separate explicit owner authorization after the applicable
validation and third-party review are complete.
```

- [x] **Step 3: 运行公共文档合同并确认通过**

Run:

```powershell
node .\tests\test_public_documentation_contract.js
```

Expected:

```text
PASS public documentation contract
```

- [x] **Step 4: 检查源代码事实与文档一致**

Run:

```powershell
git grep -n -E "zerotoheroes|hearthstonejson|5 \* 1024 \* 1024|64 \* 1024 \* 1024|FromSeconds\(15\)|FromSeconds\(45\)|active.json|previous.json|candidate.json|health.json" -- src/BobCoach/Core
```

Expected: 命中 `TrinketStatsFetcher.cs`、`TrinketStatsUpdater.cs` 和 `TrinketStatsStore.cs`，端点、上限、超时和缓存名称与登记一致。

### Task 4: 全仓库文档审计与完整验证

**Files:**
- Verify only: all tracked Markdown and policy files
- Verify only: repository tests and validator

**Interfaces:**
- Consumes: Tasks 1-3 的文档与测试改动。
- Produces: 可复核的链接、测试、仓库扫描和 diff 结果。

- [x] **Step 1: 检查受版本控制 Markdown 的相对链接**

使用 Node.js 内置 `fs` 和 `path` 扫描已跟踪及未跟踪但未被忽略的 Markdown 文件。Git 文件名使用 NUL 分隔并关闭路径转义，以正确处理中文路径；扫描时忽略 fenced code 示例以及 `http://`、`https://`、`mailto:` 和纯锚点链接。对其余会被渲染的链接去掉锚点和查询参数后，按源文件目录解析并检查存在性。

Run:

```powershell
@'
const fs = require("fs");
const path = require("path");
const cp = require("child_process");
const output = cp.execFileSync("git", ["-c", "core.quotepath=false", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.md"], { encoding: "utf8" });
const files = output.split("\0").filter(Boolean);
const missing = [];
for (const file of files) {
  const text = fs.readFileSync(file, "utf8").replace(/^```[^\n]*\n[\s\S]*?^```\s*$/gm, "");
  for (const match of text.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
    let target = match[1].trim().replace(/^<|>$/g, "");
    if (/^(https?:|mailto:|#)/i.test(target)) continue;
    target = decodeURI(target.split("#", 1)[0].split("?", 1)[0]);
    if (!target) continue;
    const resolved = path.resolve(path.dirname(file), target);
    if (!fs.existsSync(resolved)) missing.push(`${file} -> ${target}`);
  }
}
if (missing.length) { console.error(missing.join("\n")); process.exit(1); }
console.log(`PASS ${files.length} Markdown files have resolvable rendered relative links`);
'@ | node -
```

Expected: 输出以 `PASS` 开头并报告扫描到的 Markdown 文件数，无缺失目标。

- [x] **Step 2: 扫描过期或越权声明**

Run:

```powershell
git grep -n -E "GitHub Release publication is not authorized|已获.*Firestone.*授权|Firestone.*已授权|HSReplay.*(接入|抓取|使用)" -- "*.md" NOTICE
```

Expected: 不命中过期 Release 声明或虚构授权；若历史设计记录包含当时状态，只在其明确标注为历史上下文时保留，并在审计记录中说明。

- [x] **Step 3: 运行完整自动化测试**

Run:

```powershell
npm test
```

Expected: 合同测试、行为测试和包测试全部 PASS。

- [x] **Step 4: 运行仓库验证器**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
```

Expected: 敏感文件、个人路径、大文件、发布身份和包白名单检查全部 PASS。

- [x] **Step 5: 完成 diff 与删除审计**

Run:

```powershell
git diff --check
git diff --name-status
git status --short --branch
```

Expected: `git diff --check` 无输出；没有 `D` 状态；仅出现计划内文档、规则和合同测试改动；设计规格与实施计划保持未提交。

- [x] **Step 6: 汇报剩余外部风险**

最终结果必须明确说明：

- 文档结构和自动化门禁已经完成到什么程度；
- Firestone 公开应用许可仍需项目所有者取得并归档书面确认；
- HearthstoneJSON/hsdata 的事实来源披露不等于数据再许可；
- 没有执行 VM、提交、push、Release 或用户数据清理；
- 任何后续公开发布仍属于单独授权动作。

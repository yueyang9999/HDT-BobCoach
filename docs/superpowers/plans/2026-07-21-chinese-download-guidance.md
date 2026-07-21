# Chinese Download Guidance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the default Chinese repository page and installation guide tell Windows 10 and Windows 11 users exactly which official package to download, while preserving the verified compatibility boundary.

**Architecture:** Keep one signed release asset for both supported x64 Windows versions and expose it through explicit system-labelled links. Protect the user-facing wording with a dependency-free Node.js contract test, then update the existing GitHub Release description only after the repository change is merged and separate publication approval is granted.

**Tech Stack:** GitHub-flavored Markdown, Node.js built-in `assert`/`fs`/`path`, PowerShell repository validation, GitHub CLI.

## Global Constraints

- `README.md` remains the default Chinese GitHub landing page; `README.en.md` remains the English alternative.
- Windows 11 24H2 x64 and Windows 10 22H2 x64 both download `BobCoach-0.2.0-beta.1-win-x64.zip`.
- Windows 11 24H2 x64 is physically verified; Windows 10 22H2 x64 is technically compatible but has not completed dedicated physical validation.
- Use the link text `中文安装教程（新手从这里开始）` for `docs/user/INSTALL.md` on the Chinese landing page.
- State beside every download block that GitHub's generated `Source code (zip)` and `Source code (tar.gz)` archives are not installer packages.
- Do not change installer scripts, the plugin DLL, package contents, checksums, version `0.2.0-beta.1`, release assets, or CI configuration.
- Do not push, merge, or modify the public GitHub Release without the user's explicit approval for that action.

---

### Task 1: Add a public documentation contract

**Files:**
- Create: `tests/test_public_documentation_contract.js`
- Modify: `tests/run_contract_tests.ps1`

**Interfaces:**
- Consumes: `README.md`, `README.en.md`, and `docs/user/INSTALL.md` as UTF-8 Markdown.
- Produces: A dependency-free contract test that fails when package mapping, compatibility status, source-archive warning, or the Chinese tutorial entry regresses.

- [ ] **Step 1: Write the failing contract test**

Create `tests/test_public_documentation_contract.js` with these exact assertions:

```javascript
"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");
const chineseReadme = read("README.md");
const englishReadme = read("README.en.md");
const installGuide = read("docs/user/INSTALL.md");
const packageName = "BobCoach-0.2.0-beta.1-win-x64.zip";
const packageUrl = `https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/${packageName}`;

for (const [name, content] of [
    ["README.md", chineseReadme],
    ["README.en.md", englishReadme],
    ["docs/user/INSTALL.md", installGuide],
]) {
    assert.ok(content.includes(packageName), `${name} must name the official package`);
    assert.ok(content.includes(packageUrl), `${name} must link directly to the official package`);
    assert.ok(content.includes("Windows 11 24H2 x64"), `${name} must identify Windows 11`);
    assert.ok(content.includes("Windows 10 22H2 x64"), `${name} must identify Windows 10`);
    assert.ok(content.includes("Source code (zip)"), `${name} must reject the generated ZIP source archive`);
    assert.ok(content.includes("Source code (tar.gz)"), `${name} must reject the generated tar source archive`);
}

assert.ok(
    chineseReadme.includes("[中文安装教程（新手从这里开始）](docs/user/INSTALL.md)"),
    "Chinese README must expose the beginner installation guide",
);
assert.ok(chineseReadme.includes("已完成实机验收"), "Chinese README must preserve Win11 verification status");
assert.ok(chineseReadme.includes("尚未完成专用实机验收"), "Chinese README must preserve Win10 limitation");
assert.ok(englishReadme.includes("physically verified"), "English README must preserve Win11 verification status");
assert.ok(
    englishReadme.includes("not completed dedicated physical validation"),
    "English README must preserve Win10 limitation",
);
assert.ok(installGuide.startsWith("# Bob Coach 中文安装教程\n"), "install guide must have a recognizable Chinese title");

console.log("PASS public documentation contract");
```

- [ ] **Step 2: Register the contract test**

Add `"test_public_documentation_contract.js",` immediately after `"test_clean_checkout_contract.js",` in `tests/run_contract_tests.ps1`.

- [ ] **Step 3: Run the focused test to verify it fails**

Run:

```powershell
node .\tests\test_public_documentation_contract.js
```

Expected: exit code `1`; the first failure reports that `README.md` does not directly link to the official package.

- [ ] **Step 4: Commit the failing contract test**

```powershell
git add tests/test_public_documentation_contract.js tests/run_contract_tests.ps1
git commit -m "test: protect public download guidance"
```

### Task 2: Clarify downloads and the Chinese installation entry

**Files:**
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `docs/user/INSTALL.md`

**Interfaces:**
- Consumes: The exact package name, release tag, and compatibility wording enforced by Task 1.
- Produces: Matching Chinese and English download blocks plus a clearly titled Chinese installation guide.

- [ ] **Step 1: Add the Chinese download block**

In `README.md`, insert `## 下载与安装` directly after the public-beta introduction. Use a three-column table with one row for each system. Both rows must link to:

```text
https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/BobCoach-0.2.0-beta.1-win-x64.zip
```

The Windows 11 row must say `已完成实机验收`; the Windows 10 row must say `技术兼容，尚未完成专用实机验收`. Below the table, link the checksum and add:

```markdown
**不要下载** Release 页面底部由 GitHub 自动生成的 `Source code (zip)` 或 `Source code (tar.gz)`；它们是源码快照，不是 Bob Coach 安装包。

第一次安装请打开 **[中文安装教程（新手从这里开始）](docs/user/INSTALL.md)**。安装包可以直接解压，但解压后仍需按教程运行 `INSTALL.ps1`，不能只把 ZIP 或 DLL 放进 HDT 目录。
```

Keep `## 系统要求` after this block. Replace the old `## 安装` section with a compact `## 安装摘要` that retains the command, `%APPDATA%\HearthstoneDeckTracker\Plugins` boundary, and links to the upgrade, rollback, and uninstall documents without duplicating the download decision.

- [ ] **Step 2: Add the equivalent English download block**

In `README.en.md`, insert `## Download and Install` in the same location and use the same direct ZIP/checksum URLs. State `Physically verified` for Windows 11 and `Technically compatible; dedicated physical validation not completed` for Windows 10. Include the same source-archive warning and retain `docs/user/INSTALL.md` as the detailed guide link.

- [ ] **Step 3: Make the Chinese installation guide self-identifying**

Change the first line of `docs/user/INSTALL.md` to:

```markdown
# Bob Coach 中文安装教程
```

Immediately after `## 当前状态`, add the same two-row system mapping and direct ZIP/checksum links. State explicitly that the ZIP can be extracted normally, but installation completes only after `INSTALL.ps1` reports `PASS installed` or `PASS upgraded`. Add the same `Source code (zip)` and `Source code (tar.gz)` warning before `## 要求`.

- [ ] **Step 4: Run the focused contract test to verify it passes**

Run:

```powershell
node .\tests\test_public_documentation_contract.js
```

Expected: exit code `0` and `PASS public documentation contract`.

- [ ] **Step 5: Check local Markdown targets**

Run a PowerShell parser over `README.md`, `README.en.md`, and `docs/user/INSTALL.md`; resolve every non-HTTP, non-anchor Markdown target relative to its containing file.

Expected: every local target exists; output reports the checked-link count and no missing path.

- [ ] **Step 6: Commit the user-facing documentation**

```powershell
git add README.md README.en.md docs/user/INSTALL.md
git commit -m "docs: clarify Windows download and installation"
```

### Task 3: Run the complete repository gates

**Files:**
- Verify: all Task 1 and Task 2 changes

**Interfaces:**
- Consumes: The public documentation contract and final Markdown content.
- Produces: Evidence that the branch remains buildable, policy-compliant, and free of formatting errors.

- [ ] **Step 1: Run all automated tests**

```powershell
$env:BOBCOACH_HDT_DIR = 'E:\HDT-BobCoach-evidence\dependencies\HDT-1.53.5\build-reference'
npm test
```

Expected: exit code `0`; contract, behavior, and package suites pass.

- [ ] **Step 2: Run repository validation**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
```

Expected: exit code `0` with no sensitive-file, personal-path, replay-data, large-file, release-identity, or package-allowlist violations.

- [ ] **Step 3: Check whitespace and branch state**

```powershell
git diff --check origin/main...HEAD
git status --short --branch
```

Expected: no whitespace errors and a clean `codex/add-bilingual-readme` worktree ahead of its remote branch only by intentional commits.

### Task 4: Update the existing public Release after merge

**Files:**
- Modify externally: GitHub Release `v0.2.0-beta.1` description only

**Interfaces:**
- Consumes: The merged `main` installation-guide URL and the unchanged release assets.
- Produces: A system-labelled Release download section that points Win10 and Win11 users to the same official ZIP.

- [ ] **Step 1: Obtain explicit approval**

Stop and ask for approval to modify the public Release. Do not infer this approval from permission to commit, push, or merge.

- [ ] **Step 2: Update the Release description**

Keep the existing release notes and artifact record. Replace `## 下载` with two system-labelled links to the same official ZIP, add a separate checksum link, link `中文安装教程（新手从这里开始）` to `https://github.com/yueyang9999/HDT-BobCoach/blob/main/docs/user/INSTALL.md`, and retain the warning that `Source code (zip)` and `Source code (tar.gz)` are not installer packages.

- [ ] **Step 3: Verify the public result**

```powershell
gh release view v0.2.0-beta.1 --repo yueyang9999/HDT-BobCoach --json body,assets,url
```

Expected: both system labels map to `BobCoach-0.2.0-beta.1-win-x64.zip`; the checksum is separate; the tutorial link targets `main`; exactly the two existing official assets remain unchanged.

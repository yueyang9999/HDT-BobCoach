# GitHub Repository Data Disclaimer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 明确声明 GitHub 仓库中的代码、文档、规则快照、议题、PR、镜像、fork、评论和外部链接不是第三方官方数据或背书，也不保证完整、及时或未经修改。

**Architecture:** 以根目录 `DATA_SOURCES.md` 作为事实边界的权威登记，在双语 `README`、`PRIVACY.md` 和 `NOTICE` 中提供一致的用户可见摘要。通过 `tests/test_public_documentation_contract.js` 固定关键句，避免免责声明在后续更新中被弱化。

**Tech Stack:** Markdown、Node.js assertion contract tests、PowerShell repository validation。

## Global Constraints

- 不恢复 Firestone/Zero to Heroes 请求、缓存、专用解析或自动重试。
- 不读取用户历史缓存，不使用 `-RemoveUserData`。
- 不修改 `.env`、密钥、token、CI/CD，不创建 PR、Tag、GitHub Release 或公开发布。
- GitHub 仓库内容不表述为 Blizzard、HDT、HearthDb、HearthstoneJSON、HearthSim、GitHub 或其他第三方的官方数据、完整数据、最新数据、授权或背书。
- 继续使用 ASCII 标点和现有 UTF-8 文档编码；所有文档链接保持有效。

---

### Task 1: Add the failing documentation contract

**Files:**
- Modify: `tests/test_public_documentation_contract.js`

**Interfaces:**
- Consumes: Existing README, `DATA_SOURCES.md`, `PRIVACY.md`, and `NOTICE` text loaded by the contract test.
- Produces: Assertions requiring the GitHub repository-data disclaimer in Chinese and English public documents.

- [x] **Step 1: Write the failing test**

Add assertions that require:

```js
for (const [name, content, statements] of [
  ["README.md", chineseReadme, ["GitHub 仓库中的代码、文档、规则快照、议题、PR、镜像、fork、评论和外部链接不等于官方数据、完整数据或最新数据"]],
  ["README.en.md", englishReadme, ["GitHub repository code, documentation, rule snapshots, issues, pull requests, mirrors, forks, comments, and external links are not official, complete, or current third-party data"]],
  ["DATA_SOURCES.md", dataSources, ["GitHub 仓库数据免责声明"]],
  ["PRIVACY.md", privacy, ["GitHub 仓库中的内容不代表运行时会读取用户 GitHub 账户、私有仓库或历史缓存"]],
  ["NOTICE", notice, ["GitHub repository contents are not official third-party data"]],
]) {
  for (const statement of statements) {
    assert.ok(content.includes(statement), `${name} must state: ${statement}`);
  }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `node tests/test_public_documentation_contract.js`

Expected: FAIL because the new GitHub repository-data disclaimer statements are not present yet.

### Task 2: Update the authoritative source and user-facing summaries

**Files:**
- Modify: `DATA_SOURCES.md`
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `PRIVACY.md`
- Modify: `NOTICE`

**Interfaces:**
- Consumes: The failing statements from Task 1 and the existing third-party/data-source boundaries.
- Produces: Consistent Chinese and English GitHub repository-data disclaimer language.

- [x] **Step 1: Add the authoritative `DATA_SOURCES.md` section**

Add a `## GitHub 仓库数据免责声明` section after the general principles stating that repository code, docs, local rule snapshots and maintainer notes are project materials; GitHub issues, PRs, mirrors, forks, comments and linked pages are untrusted discussion or references, not runtime inputs or official third-party datasets; content may be stale, incomplete, modified, unavailable, or inconsistent with local HDT/HearthDb builds; release users must rely on the package manifest, external SHA-256 file and version-specific source statement; the project does not automatically read a user's GitHub account, private repositories, tokens, local history cache, or repository contents at runtime.

- [x] **Step 2: Add concise bilingual README summaries**

Extend `README.md` and `README.en.md` data-source/disclaimer sections with the same facts and link to `DATA_SOURCES.md`.

- [x] **Step 3: Align privacy and third-party notice**

Add the runtime non-access boundary to `PRIVACY.md`, and add a concise third-party/legal boundary to `NOTICE` without claiming official ownership, completeness, freshness, authorization, or endorsement.

### Task 3: Verify, commit, and push

**Files:**
- No additional files.

**Interfaces:**
- Consumes: Updated public documentation and contract assertions.
- Produces: A clean commit on `codex/add-bilingual-readme` and a normal push to its origin branch.

- [x] **Step 1: Run focused tests**

Run: `node tests/test_public_documentation_contract.js`

Expected: `PASS public documentation contract`.

- [x] **Step 2: Run repository checks**

Run: `git diff --check` and `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1`.

Expected: both commands exit successfully with no whitespace or repository-policy violations.

- [ ] **Step 3: Review and commit**

Run: `git diff --stat; git diff -- docs/superpowers/plans/2026-07-22-github-repository-data-disclaimer.md tests/test_public_documentation_contract.js DATA_SOURCES.md README.md README.en.md PRIVACY.md NOTICE; git add docs/superpowers/plans/2026-07-22-github-repository-data-disclaimer.md tests/test_public_documentation_contract.js DATA_SOURCES.md README.md README.en.md PRIVACY.md NOTICE; git commit -m "docs: clarify GitHub repository data disclaimer"`

Expected: one commit is created and contains only the plan, contract, and disclaimer documentation.

- [ ] **Step 4: Push without force**

Run: `git push origin codex/add-bilingual-readme`

Expected: normal fast-forward push succeeds; no PR, tag, release, or force push is created.

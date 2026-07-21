# Bilingual README Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide equivalent Chinese and English repository introductions with accurate data-source, support, and MIT licensing boundaries.

**Architecture:** Keep `README.md` as the Chinese GitHub landing page and add `README.en.md` as its English counterpart, with reciprocal language links. Reuse the existing authoritative policy documents and update them only where HSReplay's non-use and the support-entry status need to be explicit.

**Tech Stack:** GitHub-flavored Markdown, PowerShell repository validation, Node.js contract tests.

## Global Constraints

- Only modify the public `yueyang9999/HDT-BobCoach` repository; the private archived `HDT-BObcoash` repository remains read-only.
- Keep `README.md` as the default Chinese landing page and add `README.en.md`.
- State only actual runtime sources: Firestone/Zero to Heroes and HearthstoneJSON/HearthSim.
- State that HSReplay is not currently integrated, fetched, packaged, or redistributed.
- Keep the existing MIT license and clarify that it does not relicense third-party data, software, game content, statistics, or trademarks.
- Do not add `.github/FUNDING.yml`, placeholder links, or personal payment QR codes without a real approved support destination.

---

### Task 1: Add reciprocal bilingual repository introductions

**Files:**
- Modify: `README.md`
- Create: `README.en.md`

**Interfaces:**
- Consumes: Verified product, compatibility, installation, privacy, build, support, disclaimer, and license facts from `README.md` and root policy documents.
- Produces: Two equivalent GitHub landing documents linked by `README.md` and `README.en.md`.

- [ ] **Step 1: Add the language selector to the Chinese README**

Insert directly below `# HDT-BobCoach`:

```markdown
[中文](README.md) | [English](README.en.md)
```

- [ ] **Step 2: Add the English README**

Create `README.en.md` with the same sections and verified claims as the Chinese README:

```markdown
# HDT-BobCoach

[中文](README.md) | [English](README.en.md)

## System Requirements
## Installation
## Privacy and Network Access
## Data Sources and Third-Party Rights
## Build and Test
## Support and Voluntary Contributions
## Disclaimer
## License
```

Translate meaning rather than sentence structure. Preserve version `0.2.0-beta.1`, HDT `1.53.5` x64, the Windows 11 verification claim, the Windows 10 limitation, `%APPDATA%\HearthstoneDeckTracker\Plugins`, and all existing commands exactly.

- [ ] **Step 3: Verify reciprocal links and section parity**

Run:

```powershell
Select-String -Path README.md,README.en.md -Pattern 'README\.md|README\.en\.md|0\.2\.0-beta\.1|HearthstoneDeckTracker\\Plugins|DATA_SOURCES\.md|SUPPORT\.md|MIT'
```

Expected: both files contain the language links, version, plugin path, policy links, and MIT reference.

- [ ] **Step 4: Commit the bilingual README change**

```powershell
git add README.md README.en.md
git commit -m "docs: add bilingual repository readme"
```

### Task 2: Make third-party and support boundaries explicit

**Files:**
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `DATA_SOURCES.md`
- Modify: `NOTICE`
- Modify: `SUPPORT.md`

**Interfaces:**
- Consumes: Runtime endpoints in `src/BobCoach/Core/TrinketStatsUpdater.cs` and the existing MIT `LICENSE`.
- Produces: Consistent source, non-affiliation, non-relicensing, and support-entry statements for users and maintainers.

- [ ] **Step 1: Add concise source disclosure to both README files**

The Chinese README must say that Firestone/Zero to Heroes and HearthstoneJSON/HearthSim are the current read-only sources, and that HSReplay is not currently used. The English README must provide the same facts and link to `DATA_SOURCES.md` and `NOTICE`.

- [ ] **Step 2: Record HSReplay's non-use in the authoritative source policy**

Add an explicit row to `DATA_SOURCES.md`:

```markdown
| HSReplay | 当前未接入 | 不请求、不抓取、不打包、不再分发；未来接入前必须单独复核其条款和许可 |
```

Extend the non-affiliation sentence to include HSReplay without calling it a current data source.

- [ ] **Step 3: Extend the third-party notice**

Add HSReplay to `NOTICE`'s non-affiliation list and state that Bob Coach does not currently fetch, bundle, or redistribute HSReplay data. Keep the existing Firestone/Zero to Heroes and HearthstoneJSON wording unchanged except where needed for grammar.

- [ ] **Step 4: Clarify the current support-entry status**

Keep `SUPPORT.md` as the support and voluntary-contribution page. State that no payment address or QR code is currently published, and that a real external destination must pass privacy, replacement, payment, refund, and tax review before publication.

- [ ] **Step 5: Check source and license accuracy**

Run:

```powershell
Get-ChildItem README.md,README.en.md,DATA_SOURCES.md,NOTICE,SUPPORT.md | Select-String -Pattern 'Firestone|Zero to Heroes|HearthstoneJSON|HearthSim|HSReplay|MIT|二维码|QR code'
```

Expected: Firestone and HearthstoneJSON are described as current sources; HSReplay is always described as not currently used; MIT is limited to original project material; no real QR code or payment link appears.

- [ ] **Step 6: Commit the policy clarification**

```powershell
git add README.md README.en.md DATA_SOURCES.md NOTICE SUPPORT.md
git commit -m "docs: clarify source and support boundaries"
```

### Task 3: Run repository validation

**Files:**
- Verify: all changed documentation

**Interfaces:**
- Consumes: Completed documentation changes from Tasks 1 and 2.
- Produces: Evidence that links, repository contracts, sensitive-content policy, formatting, and existing tests still pass.

- [ ] **Step 1: Check relative Markdown links**

Parse local Markdown link targets in both README files and verify every non-URL target exists relative to the README location.

- [ ] **Step 2: Run automated tests**

```powershell
$env:BOBCOACH_HDT_DIR = 'E:\HDT-BobCoach-evidence\dependencies\HDT-1.53.5\build-reference'
npm test
```

Expected: exit code `0` and final package suite passes.

- [ ] **Step 3: Run repository validation**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
```

Expected: exit code `0` with no sensitive-file, personal-path, replay-data, or large-file violations.

- [ ] **Step 4: Check whitespace and final diff**

```powershell
git diff --check origin/main...HEAD
git status --short --branch
```

Expected: no whitespace errors; only the intended branch commits remain ahead of `origin/main`.

- [ ] **Step 5: Commit any validation-only corrections**

If validation required documentation corrections, stage only the five documentation files and commit them with:

```powershell
git add README.md README.en.md DATA_SOURCES.md NOTICE SUPPORT.md
git commit -m "docs: fix bilingual documentation validation"
```

If no correction was required, do not create an empty commit.

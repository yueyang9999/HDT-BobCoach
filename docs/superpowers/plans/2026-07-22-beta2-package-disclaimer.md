# Beta.2 Offline Package Disclaimer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure every generated `0.2.0-beta.2` offline package explicitly identifies itself as a local release candidate that is not a public GitHub Release.

**Architecture:** Keep `CurrentSeasonPreview` retired and preserve its historical rejection contract. Give the active beta.2 candidate notice its own template block, include that block in ordinary package builds, and prove the behavior against the extracted ZIP rather than only checking source text.

**Tech Stack:** PowerShell 5.1 package builder and package contract tests, Markdown release template.

## Global Constraints

- Do not restore `CurrentSeasonPreview`, Firestone/Zero to Heroes runtime requests, caches, dedicated parsing, or automatic retries.
- Do not read user history caches or use `-RemoveUserData`.
- Do not modify `.env`, credentials, tokens, or CI/CD configuration.
- Do not create a PR, tag, GitHub Release, or public beta.2 asset.
- Keep the exact 11-file package allowlist and all existing manifest/hash checks.

---

### Task 1: Make The Candidate Disclaimer A Generated-Package Contract

**Files:**
- Modify: `tests/test_offline_package_builder.ps1`
- Modify: `tools/release/README_OFFLINE.md`
- Modify: `tools/release/build_offline_package.ps1`

**Interfaces:**
- Consumes: the extracted `README_OFFLINE.md` created by `build_offline_package.ps1`.
- Produces: a beta.2 README containing `LOCAL RELEASE CANDIDATE / NOT A PUBLIC GITHUB RELEASE`, while `-CurrentSeasonPreview` continues to fail with its historical-boundary error.

- [x] **Step 1: Write the failing generated-package assertion**

Add the following immediately after the extracted release README is loaded:

```powershell
Assert-True ($releaseReadme.Contains(
    "LOCAL RELEASE CANDIDATE / NOT A PUBLIC GITHUB RELEASE"
)) "release README identifies the local non-public candidate"
```

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\validation-baselines\hdt-1.53.5.7354-20260722'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_offline_package_builder.ps1
```

Expected: `FAIL offline package builder contracts` because the generated release README omits the local non-public candidate notice.

- [x] **Step 3: Give the active notice an independent template block**

Rename the notice markers in `tools/release/README_OFFLINE.md` to:

```markdown
{{LOCAL_CANDIDATE_NOTICE}}
> `LOCAL RELEASE CANDIDATE / NOT A PUBLIC GITHUB RELEASE`
...
{{/LOCAL_CANDIDATE_NOTICE}}
```

Resolve that block in `build_offline_package.ps1` with:

```powershell
$readme = Resolve-ReadmeBlock $readme "LOCAL_CANDIDATE_NOTICE" (!$CurrentSeasonPreview)
```

Keep the existing early `CurrentSeasonPreview` rejection unchanged.

- [x] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2 again. Expected: exit `0` with `PASS offline package release ZIP whitelist, hashes, retired preview boundary, Force, and cleanup contracts`.

### Task 2: Keep The Public Disclosure Design Current

**Files:**
- Modify: `tests/test_public_documentation_contract.js`
- Modify: `docs/design/双语自述与第三方声明设计_2026-07-21.md`

- [x] **Step 1: Add a failing contract for the retired Firestone runtime boundary**

- [x] **Step 2: Replace the stale current-runtime statement and verify GREEN**

### Task 3: Revalidate And Rebuild The Candidate

**Files:**
- Create externally: `E:\HDT-BobCoach-release-candidate\0.2.0-beta.2-<commit>-20260722\*`
- Modify: no repository files unless a gate exposes a reproducible defect.

**Interfaces:**
- Consumes: the green package disclaimer change and HDT `1.53.5.7354` baseline.
- Produces: full local validation evidence and one exact, non-public beta.2 candidate ZIP bound to the final commit.

- [x] **Step 1: Run the complete L2 gates**

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\validation-baselines\hdt-1.53.5.7354-20260722'
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\build_release.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory 'E:\HDT-BobCoach-evidence\verification\beta2-disclaimer-final\build' -Force
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

Expected: all commands exit `0`; the known compiler warning about `CardSemanticFact.CardId` may remain, but no build or test failure is permitted.

- [ ] **Step 2: Build and audit the replacement candidate**

Build the ZIP outside Git, then verify the exact 11 entries, manifest identity, internal and external SHA-256 values, DLL identity, and extracted README disclaimer. Record the source commit, ZIP bytes, ZIP SHA-256, DLL bytes, and DLL SHA-256.

- [ ] **Step 3: Commit and push the preserved branch**

Commit only the plan, regression tests, disclosure design, template, and package-builder changes. Push `codex/add-bilingual-readme` normally, wait for CI success, and leave `main`, PRs, tags, and Releases unchanged.

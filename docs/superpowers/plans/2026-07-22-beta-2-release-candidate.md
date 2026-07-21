# BobCoach 0.2.0-beta.2 Release Candidate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved Firestone trinket data-boundary and equipped-trinket rule changes as the reproducible `0.2.0-beta.2` prerelease.

**Architecture:** Keep the binary compatibility identity at `0.2.0.0` while advancing the package and informational identity to `0.2.0-beta.2`. Treat source, tests, documentation, CI, the final ZIP, Win11 smoke evidence, and the GitHub prerelease as a single traceable release chain; any changed commit or artifact invalidates downstream evidence.

**Tech Stack:** C#/.NET Framework 4.7.2, PowerShell 5.1, dependency-free Node.js contract tests, GitHub Actions and GitHub Releases.

## Global Constraints

- Preserve all equipped-trinket recognition, local rules, decision-engine integration, recommendation services, and UI code; trinket-offer recommendations remain hidden by default.
- Do not restore Firestone runtime requests, caches, parsing, retry entry points, or use of Firestone trinket data.
- Do not read, migrate, modify, or delete existing user cache or `%APPDATA%\bob-coach` data.
- Do not modify `.github/workflows`, CI/CD configuration, HDT `1.53.5.7354`, `net472`, or `win-x64` compatibility contracts.
- Keep `assemblyVersion` and `fileVersion` at `0.2.0.0`; set package and informational versions to `0.2.0-beta.2`.
- Generated packages and validation evidence stay outside Git in `E:\HDT-BobCoach-evidence`.
- Do not use `-RemoveUserData`, do not run the full VM lifecycle matrix, and do not alter sealed VM base disks.
- A release requires green local gates, green CI, exact package allowlist/hash verification, and Win11 24H2 L2 smoke on the final merged commit.

---

### Task 1: Advance The Release Identity And Public Contract

**Files:**
- Modify: `release_identity.json`
- Modify: `package.json`
- Modify: `src/BobCoach/ReleaseAssemblyInfo.cs`
- Modify: `tests/test_release_identity.js`
- Modify: `tests/test_public_documentation_contract.js`
- Modify: `tests/test_deterministic_build_and_package.ps1`
- Modify: `tests/test_offline_package_builder.ps1`
- Modify: `tests/test_release_package_contract.js`
- Modify: `tools/release/build_offline_package.ps1`
- Modify: `tools/release/INSTALL.ps1`
- Modify: `tools/release/README_OFFLINE.md`

**Interfaces:**
- Consumes: the five-field `release_identity.json` schema and existing exact 11-file package allowlist.
- Produces: package/informational identity `0.2.0-beta.2`, binary identity `0.2.0.0`, and `BobCoach-0.2.0-beta.2-win-x64.zip`.

- [x] **Step 1: Change release contract expectations first**

Update active release assertions from `0.2.0-beta.1` to `0.2.0-beta.2` while preserving explicit historical upgrade fixtures that intentionally model beta.1. The central expected object must be:

```javascript
const expected = {
  packageVersion: '0.2.0-beta.2',
  assemblyVersion: '0.2.0.0',
  targetFramework: 'net472',
  runtimeIdentifier: 'win-x64',
  hdtBaselineVersion: '1.53.5.7354',
};
```

- [x] **Step 2: Run focused tests and verify the red state**

Run:

```powershell
node .\tests\test_release_identity.js
node .\tests\test_public_documentation_contract.js
```

Expected: both commands fail because production identity and public download links still reference `0.2.0-beta.1`.

- [x] **Step 3: Advance production and package identity**

Set `package.json.version`, `release_identity.json.packageVersion`, and `AssemblyInformationalVersion` to `0.2.0-beta.2`. Update exact installer, builder, formal-package test, and offline README contracts to beta.2; retain `AssemblyVersion("0.2.0.0")` and `AssemblyFileVersion("0.2.0.0")`.

- [x] **Step 4: Run release/package contract tests**

Run:

```powershell
node .\tests\test_release_identity.js
node .\tests\test_release_package_contract.js
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_offline_package_builder.ps1
```

Expected: each command exits `0` and reports `PASS`; preview fixture names remain valid and formal output uses beta.2.

### Task 2: Publish Accurate Beta.2 Documentation And Notes

**Files:**
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `docs/user/INSTALL.md`
- Modify: `NOTICE`
- Modify: `CHANGELOG.md`
- Modify: `.github/ISSUE_TEMPLATE/bug_report.yml`

**Interfaces:**
- Consumes: final asset name `BobCoach-0.2.0-beta.2-win-x64.zip` and the already documented Firestone/equipped-trinket boundary.
- Produces: bilingual direct-download guidance and release notes that match the candidate behavior and validation limits.

- [x] **Step 1: Update public-document expectations**

Make the contract require:

```javascript
const packageName = "BobCoach-0.2.0-beta.2-win-x64.zip";
const packageUrl = `https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.2/${packageName}`;
```

Require `NOTICE` to acknowledge both the historical `v0.2.0-beta.1` release and current `v0.2.0-beta.2` release without weakening the per-release authorization statement.

- [x] **Step 2: Update active user-facing version references**

Change the current-version prose, package names, download URLs, hash URLs, bug-report placeholder, formal offline-package examples, and active NOTICE release statement to beta.2. Do not rewrite dated design/spec/plan history or fixtures whose purpose is testing upgrade from beta.1.

- [x] **Step 3: Cut the changelog entry**

Add `## 0.2.0-beta.2 - 2026-07-22` describing: retirement of Firestone trinket runtime data; hidden trinket-offer recommendations; independent equipped-trinket rules in legality/economy/scoring/combat decisions; source-independent synthetic validation; and documentation/privacy/dependency boundary updates.

- [x] **Step 4: Verify documentation contracts and stale active references**

Run:

```powershell
node .\tests\test_public_documentation_contract.js
git grep -n -E "0\.2\.0-beta\.1|v0\.2\.0-beta\.1"
```

Expected: documentation contract passes; remaining beta.1 matches are limited to the historical changelog/design/plans and intentional upgrade fixtures.

### Task 3: Run Local L2 Release Gates And Review

**Files:**
- Create externally: `E:\HDT-BobCoach-evidence\verification\beta-2-premerge\*.log`
- Modify: no repository files unless a verified failure requires a scoped fix.

**Interfaces:**
- Consumes: Task 1 and Task 2 working tree.
- Produces: evidence that the exact commit is eligible to push and review.

- [x] **Step 1: Run full automated tests**

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.53.5\build-reference'
npm test
```

Expected: contract, behavior, and package suites all pass.

- [x] **Step 2: Run Release build and repository audits**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\build_release.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory 'E:\HDT-BobCoach-evidence\verification\beta-2-premerge\build' -Force
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

Expected: x64 `net472` Release build passes, repository allowlist/sensitive-data checks pass, and diff check exits `0`.

- [x] **Step 3: Review the complete branch delta**

```powershell
git diff --stat origin/main...HEAD
git diff origin/main...HEAD
```

Expected: no Firestone endpoint/retry/cache path remains, no user-cache access is added, no CI/CD file changed, and the display flag does not gate equipped-trinket effects.

- [x] **Step 4: Commit and push without rewriting history**

```powershell
git add release_identity.json package.json src/BobCoach/ReleaseAssemblyInfo.cs tests tools/release README.md README.en.md docs/user/INSTALL.md NOTICE CHANGELOG.md .github/ISSUE_TEMPLATE/bug_report.yml docs/superpowers/plans/2026-07-22-beta-2-release-candidate.md
git commit -m "release: prepare 0.2.0-beta.2"
git push origin codex/add-bilingual-readme
```

Expected: a normal commit and non-force push succeed; the working tree is clean.

### Task 4: Recover CI, Merge The Pull Request, And Freeze The Commit

**Files:**
- Modify externally: GitHub Actions run state and GitHub pull request.
- Modify: no repository files unless review or CI exposes a reproducible defect.

**Interfaces:**
- Consumes: pushed beta.2 feature branch and local green gates.
- Produces: green branch/PR checks and one immutable commit on `main` for packaging.

- [ ] **Step 1: Replace the stale CI run without changing workflows**

Cancel the existing run only after local `npm test` exits. Trigger a normal rerun/push run and inspect the step result:

```powershell
gh run cancel 29859205211
gh run rerun 29859205211
gh run watch 29859205211 --exit-status
```

Expected: the run completes successfully; if it hangs again, stop release progression and diagnose the exact lifecycle-test process rather than modifying `.github/workflows`.

- [ ] **Step 2: Open and validate the pull request**

```powershell
gh pr create --base main --head codex/add-bilingual-readme --title "Release BobCoach 0.2.0-beta.2" --body-file E:\HDT-BobCoach-evidence\verification\beta-2-premerge\pr-body.md
gh pr checks codex/add-bilingual-readme --watch
```

Expected: all required checks pass and the PR describes local tests, CI, Firestone retirement, equipped-trinket behavior, and Win10 limitation.

- [ ] **Step 3: Merge without force or history rewriting**

```powershell
gh pr merge codex/add-bilingual-readme --merge
git fetch origin
git rev-parse origin/main
```

Expected: PR merge succeeds and the returned `origin/main` SHA becomes the only allowed release commit.

### Task 5: Build And Verify The Final Candidate Artifact

**Files:**
- Create externally: `E:\HDT-BobCoach-evidence\verification\beta-2-final\BobCoach-0.2.0-beta.2-win-x64.zip`
- Create externally: `E:\HDT-BobCoach-evidence\verification\beta-2-final\BobCoach-0.2.0-beta.2-win-x64.zip.sha256`
- Create externally: `E:\HDT-BobCoach-evidence\verification\beta-2-final\candidate-record.txt`

**Interfaces:**
- Consumes: a clean checkout at the frozen `origin/main` merge commit.
- Produces: one exact allowlisted ZIP and external SHA-256 bound to that commit.

- [ ] **Step 1: Re-run final commit gates**

On the clean final-commit checkout, run `npm test`, `build_release.ps1`, `validate_repository.ps1`, and `git diff --check` using the same HDT directory as Task 3. Expected: all exit `0` and `git status --short` is empty.

- [ ] **Step 2: Build the formal offline package**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release\build_offline_package.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory 'E:\HDT-BobCoach-evidence\verification\beta-2-final' -Force
```

Expected: `PASS offline package`, package name beta.2, and an external `.sha256` file.

- [ ] **Step 3: Audit exact contents and identity**

Verify that the ZIP has one beta.2 root and exactly these files: `BobCoach.dll`, `README_OFFLINE.md`, `INSTALL.ps1`, `UNINSTALL.ps1`, `LICENSE`, `NOTICE`, `DATA_SOURCES.md`, `PRIVACY.md`, `SUPPORT.md`, `manifest.json`, `SHA256SUMS.txt`. Record merge SHA, byte length, SHA-256, ZIP entries, and manifest identity in `candidate-record.txt`.

### Task 6: Run Win11 L2 Smoke And Publish The Prerelease

**Files:**
- Create externally: `E:\HDT-BobCoach-evidence\verification\beta-2-final\win11-smoke\*`
- Modify externally: GitHub prerelease `v0.2.0-beta.2`.

**Interfaces:**
- Consumes: the exact candidate ZIP and frozen merge commit from Task 5.
- Produces: Win11 24H2 load evidence and the public prerelease with matching assets.

- [ ] **Step 1: Install the exact candidate in the disposable Win11 overlay**

Use the package's `INSTALL.ps1` as a normal user, without `-RemoveUserData`. Confirm `%APPDATA%\HearthstoneDeckTracker\Plugins\BobCoach.dll` matches the candidate manifest hash.

- [ ] **Step 2: Run minimum HDT and equipped-trinket smoke**

Start HDT and confirm logs contain `Loading BobCoach`, `Enabled BobCoach`, and `BobCoach ready`. Exercise the synthetic/evidence fixture for an exact known equipped-trinket `CardId`; confirm equipped effects reach later decision scoring while no trinket-offer choice prompt is rendered. Unknown IDs must be conservatively ignored with rate-limited diagnostics.

- [ ] **Step 3: Close cleanly and preserve evidence**

Close HDT normally; confirm the process exits and no new plugin error appears. Preserve only logs, hashes, and compact screenshots outside Git; do not run uninstall, rollback, full lifecycle, or any user-data removal.

- [ ] **Step 4: Create and verify the prerelease**

```powershell
gh release create v0.2.0-beta.2 E:\HDT-BobCoach-evidence\verification\beta-2-final\BobCoach-0.2.0-beta.2-win-x64.zip E:\HDT-BobCoach-evidence\verification\beta-2-final\BobCoach-0.2.0-beta.2-win-x64.zip.sha256 --repo yueyang9999/HDT-BobCoach --target <frozen-merge-sha> --prerelease --title "BobCoach 0.2.0-beta.2" --notes-file E:\HDT-BobCoach-evidence\verification\beta-2-final\release-notes.md
gh release view v0.2.0-beta.2 --repo yueyang9999/HDT-BobCoach --json url,isPrerelease,targetCommitish,assets
```

Replace `<frozen-merge-sha>` with the exact SHA recorded in Task 5. Expected: prerelease is true, target matches that SHA, exactly the ZIP and `.sha256` assets exist, downloaded ZIP hash matches the candidate, beta.2 README links return successfully, and Win10 remains documented as technically compatible but not dedicated-smoke verified.

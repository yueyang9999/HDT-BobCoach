# HDT-BobCoach Public Repository Implementation Plan

> **REQUIRED SUB-SKILL:** Use `executing-plans` to implement this plan and `finishing-a-development-branch` before completion.

**Goal:** Create a clean, public, product-only HDT plugin repository, preserve the former repository as private archived history, and verify the installable beta in disposable Windows VM overlays.

**Architecture:** A fresh Git root holds production source under `src/BobCoach`, tests under `tests`, deterministic tooling under `tools`, and public documentation under `docs`. Migration is allowlist-based from the sealed P0 repository; no old Git history, user data, replay data, Electron overlay, Python pipeline, or validation evidence crosses the boundary.

**Tech Stack:** C# 7.3 / .NET Framework 4.7.2, MSBuild, PowerShell 5.1, dependency-free Node.js contract tests, GitHub Actions, QEMU QCOW2 overlays.

---

### Task 1: Establish governance and isolated Git root

- Create `AGENTS.md`, design record, this implementation plan, ignore rules, and repository metadata.
- Initialize a fresh repository on `codex/public-repository-cleanup`.
- Verify the target contains no inherited `.git` directory or old history.

### Task 2: Migrate production source by project dependency allowlist

- Parse `BobCoach.csproj` item lists.
- Copy only referenced production source/resources into `src/BobCoach`.
- Adapt build paths without changing plugin assembly/runtime identity.
- Exclude built DLLs, logs, diagnostics, `BobObserver`, and retired Electron files.

### Task 3: Migrate tests and deterministic tooling

- Move required C#/PowerShell/Node test harnesses to `tests`.
- Move build scripts to `tools/build` and offline release scripts to `tools/release`.
- Replace legacy directory assumptions with repository-root discovery.
- Create a dependency-free `package.json`; remove Electron and generated lockfile dependency state.

### Task 4: Build public documentation and GitHub surface

- Write concise user install/upgrade/rollback/uninstall/troubleshooting documentation.
- Write maintainer build, architecture, dependency, and release documentation.
- Add README, changelog, license, notices, privacy, security, support, data sources, contribution guide, code of conduct, Issue/PR templates, Dependabot, and CI.
- Add funding configuration only when a real external support URL is known; never invent one.

### Task 5: Verify locally

- Run clean restore/build and the retained automated suites.
- Build the offline package and verify package identity, exact allowlist, SHA-256, and lifecycle tests.
- Scan tracked content for secrets, personal paths, replay/log data, forbidden directories, and large files.
- Run `git diff --check` and confirm the working tree is intentional.

### Task 6: Publish repository state, not a Release

- Create public `yueyang9999/HDT-BobCoach` and push the clean branch as the default branch.
- Verify visibility, default branch, remote tree, CI, community files, and absence of GitHub Releases.
- Change `yueyang9999/HDT-BObcoash` to private and archive it; verify both flags.

### Task 7: Disposable VM install acceptance

- Create temporary QCOW2 overlays from sealed Windows base images.
- Transfer the exact final ZIP through a read-only ISO.
- On Win11 24H2, verify offline standard-user install without Node or administrator rights and confirm HDT discovers the plugin.
- Run Win10 22H2 compatibility verification where feasible.
- Store evidence outside Git and never write sealed base disks.

### Task 8: Close out without publishing a GitHub Release

- Update the project handover with repository URLs, commit, test evidence, package hash, VM results, funding configuration status, and remaining user actions.
- Use `finishing-a-development-branch` for final verification.
- Stop before GitHub Release creation and request explicit release authorization.

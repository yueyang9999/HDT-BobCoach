# Release Readiness Priority Closeout Implementation Plan

> **Execution requirement:** Execute this plan directly after writing it. Follow test-driven development and verification-before-completion. Do not push, change repository settings or CI, create a tag/PR/Release, or publish beta.2 without fresh explicit authorization.

**Goal:** Close every locally actionable beta.2 release-readiness gap in priority order, while keeping Windows 11 24H2 and Windows 10 22H2 HDT smoke tests as explicit external release gates until matching environments are available.

**Architecture:** Preserve the offline, exact-CardId trinket boundary and fail-closed behavior. Add a trinket rule only when the pinned HDT 1.53.5 HearthDb fact is unambiguous and the existing deterministic model can express the full relevant effect. Keep combat event semantics and start-of-combat ordering covered by focused behavioral tests. Treat public GitHub state and local release-candidate identity as separate evidence sets.

**Tech stack:** C# 7-compatible HDT plugin code, PowerShell Roslyn harnesses, Node.js repository tests, Git/GitHub CLI read-only audits, existing Release build and packaging scripts.

## Execution status (2026-07-22)

- Task 1: Locally actionable checks complete. The host is build 26200 and no usable Hyper-V, VirtualBox, VMware, or QEMU guest was found. Windows 11 24H2 smoke remains a release gate; Windows 10 22H2 smoke remains required before first claiming Windows 10 support. GitHub API reports the repository as public, with `main` as the default branch and the latest `main` CI successful. Credential-free checks reached the public repository directly earlier in the closeout: HTTPS returned 200 and `git ls-remote` returned the exact `main` HEAD. A final API request with an explicitly empty `Authorization` header again returned `visibility=public` and `private=false`. The repository's configured Git proxy still points to an unavailable `127.0.0.1:6789`; a later direct-connect retry was blocked by the host network path, so this local connectivity result must not be confused with repository visibility.
- Task 2: Complete. Added exact rule `BG30_MagicItem_972` (Karazhan Chess Set), advanced the ruleset to `hdt-1.53.5-hearthdb-2026-07-22-r4`, and expanded exact rules from 16 to 17. Focused behavior tests cover case sensitivity, board cap, placement, ownership isolation, non-recursion, deep-copy isolation, and single Slamma application.
- Task 3: Reproduced as an existing `CS0649` warning in the test-side semantic model. It does not indicate a missing production identity assignment. No warning suppression or behavior-free production change was added; the warning remains a non-blocking maintenance item.
- Task 4: Complete for authoritative source documents and this handover pass. Source version remains `0.2.0-beta.2`; public Release remains `v0.2.0-beta.1`; beta.2 remains a local, unpublished candidate.
- Task 5: Complete for local verification. On final pre-sync HEAD `51a34dc356e133aaee0d9469c73882ff2dad8366`, a fresh full `npm test` completed in 228.7 seconds with exit code 0; focused trinket behavior, Release x64/net472 build, contract, behavior, package, and synthetic installer lifecycle suites passed. Repository validation then scanned 327 files successfully and `git diff --check` passed. The current DLL is 656896 bytes with SHA-256 `A92E5A56D3BAE43541FDAAE54954C014FB9D7B03B276D59113A978136652197C`. The final locally audited 11-entry ZIP remains `E:\HDT-BobCoach-release-candidate\2026-07-22-priority-closeout-r6\package\BobCoach-0.2.0-beta.2-win-x64.zip`, 281640 bytes with SHA-256 `6FD7ADBA47219F86C305E3BBB957053AF090AF0C44A181FAD4220D6DE6CAA0E3`; its external `.sha256`, exact allowlist, ten internal hashes, manifest, and DLL identity all match. The r4 and r5 ZIPs are superseded and must not be published.
- Task 6: Complete for authorized source integration. Release-content commit `2205a87b2aaea5389d97c54deb70c157c218dfa6` was fast-forwarded to `main` and pushed without force. Documentation evidence commit `51a34dc356e133aaee0d9469c73882ff2dad8366` was then fast-forwarded and pushed normally; GitHub Actions run `29917902448` completed successfully against that exact HEAD. Before this final status synchronization, local `main`, `origin/main`, the anonymous GitHub API, and the public commit endpoint all agreed on `51a34dc`; the worktree was clean. No beta.2 upload, tag, PR, GitHub Release, visibility change, or CI/CD change was made.

**Global constraints:**

- Do not restore Firestone/Zero to Heroes requests, caches, dedicated parsing, or automatic retries.
- Do not inspect historical user caches and do not use `-RemoveUserData`.
- Do not change `.env`, secrets, tokens, CI/CD, branch protection, Dependabot, or repository visibility in this implementation pass.
- Do not delete files or history, rebase, force-push, create a PR/tag/Release, upload beta.2, or publish externally.
- Preserve all unrelated worktree changes and use `apply_patch` for manual edits.

## Task 1: Record environment gates and current public state

**Modify:** this plan and the project handover document after verification.

- Confirm whether an already configured Windows 11 24H2 or Windows 10 22H2 test environment exists without installing software, enabling Windows features, or downloading an ISO.
- Read-only verify repository visibility, anonymous accessibility, default branch, latest main CI, public Releases, open PRs, security features, and branch/ruleset state.
- Record matching-environment smoke tests as unresolved release gates when no environment exists; do not substitute the Windows 11 25H2 host.

## Task 2: Re-audit `BG30_MagicItem_972` before expanding the registry

**Modify if supported:** `tests/ActiveTrinketEffectsHarness.cs`, relevant trinket-rule source files, and authoritative documentation.

- Obtain the exact card fact from the pinned HDT/HearthDb baseline or a traceable upstream source and compare it with the existing deterministic model.
- If the effect is fully expressible, add a failing exact-ID, case-sensitive behavioral test first; capture RED, implement the smallest rule, then require GREEN.
- If the effect depends on unsupported timing, targeting, choice, or state, keep it fail-closed and document the precise reason. Never infer behavior from translated display text alone.
- Keep the rule-set version unchanged unless the registry actually expands.

## Task 3: Remove the remaining `CS0649` warning without weakening identity checks

**Modify if reproduced:** `tests/CardSemanticSourceBehavior.cs`, `src/BobCoach/Core/CardSemanticFactSource.cs`, and/or the concrete HearthDb source.

- Reproduce the warning in a fresh focused or Release build and identify the compilation unit that leaves `CardSemanticFact.CardId` uninitialized.
- Add or strengthen a behavioral test proving exact identity propagation and mismatch rejection before implementation.
- Use explicit construction/initialization only when it improves the real contract; do not suppress the warning or add a compiler bypass.
- Re-run focused tests and the Release build and require zero new warnings.

## Task 4: Reconcile current documentation and handover evidence

**Modify as needed:** `README.md`, `README.en.md`, `DATA_SOURCES.md`, `PRIVACY.md`, `NOTICE`, `docs/README.md`, `docs/maintainer/ARCHITECTURE.md`, `docs/maintainer/DEPENDENCIES.md`, `docs/maintainer/BUILD.md`, `docs/maintainer/UPDATE_VALIDATION.md`, `docs/maintainer/RELEASE.md`, and the handover document.

- Ensure source version `0.2.0-beta.2`, local candidate status, public `v0.2.0-beta.1`, exact ruleset/count, offline data boundaries, and GitHub-data disclaimers agree.
- Make historical design records clearly non-authoritative where old acceptance language can be mistaken for current release evidence; retain history rather than deleting it.
- Update the handover to current HEAD/main CI and the newest validation/package evidence.

## Task 5: Run fresh release verification and package audit

- Run focused semantic and active-trinket behavior tests, `npm test`, Release x64/net472 build, `tools/build/validate_repository.ps1`, and `git diff --check`.
- Run package allowlist, endpoint/production-call-graph, sensitive-information, personal-path, replay-data, large-file, and `.github` diff audits.
- Build a new beta.2 local candidate ZIP outside the repository, verify its contents, and record size and SHA-256. Do not upload it.
- Re-check public GitHub state and anonymous accessibility after local verification.

## Task 6: Review and stop at authorization boundaries

- Inspect final status and diff and report locally completed items, unresolved environment gates, GitHub governance risks, and exact validation evidence.
- Do not commit or push in this pass without fresh authorization. Updating local `main`, changing GitHub rules/security settings, CI/Dependabot changes, or publishing beta.2 each require explicit authorization at the point of action.

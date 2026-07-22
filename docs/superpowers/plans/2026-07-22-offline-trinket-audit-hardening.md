# HDT-BobCoach Offline Trinket Audit Hardening

## Goal

Complete the pre-release hardening of the offline trinket audit boundary and combat semantics without restoring network/cache behavior or touching user data. Preserve all existing working-tree changes, produce fresh verification evidence, and prepare an authorized fast-forward merge/push.

## Scope

1. Reproduce and lock down Reborn ownership and summon-trigger semantics with a failing behavior test. Implement the smallest correction so Reborn re-entry uses the owning side's summon path exactly once, preserves the reborn state contract, and does not duplicate summon effects.
2. Reproduce and lock down `PhaseStartOfCombat` ordering with a failing test whose event log distinguishes hero power, board minions, hand start-of-combat effects, and trinkets. Implement an explicit deterministic order and keep existing behavior green.
3. Review the exact `CardId` trinket registry against the approved local data model, add only evidence-backed rules, retain conservative unknown-ID ignore plus rate-limited diagnostics, and extend focused behavior/contract coverage.
4. Resolve the beta.2 `CurrentSeasonPreview` DLL version audit by using an isolated historical beta.1 preview or approved beta.2 DLL evidence. Never edit hash/size fields to manufacture consistency.
5. Update README, data-source, privacy, notice, architecture, dependency, and validation documentation to describe the offline-only audit boundary, exact-ID rule provenance, version evidence, and release limitations.
6. Run fresh TDD/contract/behavior tests, Release x64/net472 build, repository and publication audits, diff checks, then create a repository-external release ZIP and SHA-256 record.
7. After all checks pass, create the authorized commit, fast-forward merge into `codex/add-bilingual-readme`, and push normally. Do not create PRs, tags, releases, or force-push.

## Verification per step

- Each combat behavior change: write test first, run it to confirm the expected failure, implement minimally, rerun focused test, then rerun the full suite.
- Audit tools: run synthetic-input contract tests and assert no implicit `%APPDATA%`, repository, cache, or endpoint access.
- Release evidence: record version, commit, file size, and SHA-256 outside the repository; verify package allowlist and manifest consistency.
- Final gate: `npm test`, focused trinket behavior test, Release build, `validate_repository.ps1`, sensitive/personal-path/replay/large-file/endpoint and `.github` audits, and `git diff --check`.

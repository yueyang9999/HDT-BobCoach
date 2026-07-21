# Active Trinket Rule Expansion Phase 2 Implementation Plan

> **Execution requirement:** Follow test-driven development and verification-before-completion. Do not create a PR or Release.

**Goal:** Add seven deterministic exact-ID equipped-trinket combat rules while preserving the independent hidden quote-recommendation boundary.

**Architecture:** Extend `TrinketEffectRegistry` and immutable `ActiveTrinketContext` only. Keep start-of-combat effects owner-scoped and use pre-mutation target selection where order matters. Do not add a Firestone source, cache path, retry path, or a UI enable dependency.

**Tech stack:** C# 7-compatible HDT plugin code, local HDT 1.53.5/HearthDb build 245258 reference snapshot, PowerShell Roslyn harnesses, Node.js repository tests.

## Task 1: Lock exact resolution and effects with failing tests

**Modify:** `tests/ActiveTrinketEffectsHarness.cs`

- Add constants for the seven Phase 2 trinkets and expected ruleset `hdt-1.53.5-hearthdb-2026-07-22-r2`.
- Resolve all 15 known IDs and assert stable deduplication, exact case-sensitive matching, and unknown-ID isolation.
- Add synthetic boards for exact Eternal Knight/Titus targeting, edge shields/reborn deduplication, stable lowest-Attack selection, `Health`/`MaxHealth` synchronization, combined medallions, and opponent isolation.
- Run `tests/test_active_trinket_effects.ps1` with `BOBCOACH_HDT_DIR` pointing to the approved HDT 1.53.5 build reference and record the expected RED caused by the missing Phase 2 registry rules.

## Task 2: Implement the minimal local rules

**Modify:**

- `src/BobCoach/Core/TrinketEffectRegistry.cs`
- `src/BobCoach/Core/ActiveTrinketContext.cs`

- Add exact CardId constants and case-sensitive registry switches.
- Extend the immutable context with seven internal flags; keep `Empty` and all construction sites explicit.
- Apply exact-target keyword rules, edge selection, stable lowest-Attack selection, and additive/multiplicative stat updates.
- Do not change `TrinketRecommendationsVisible`, offer recommendation services, external source code, cache paths, or combat lifecycle interfaces.
- Re-run the focused harness and require GREEN.

## Task 3: Synchronize public and maintainer documentation

**Modify:**

- `README.md`
- `DATA_SOURCES.md`
- `PRIVACY.md`
- `NOTICE`
- `docs/maintainer/ARCHITECTURE.md`
- `docs/maintainer/DEPENDENCIES.md`

- Change the ruleset version to `hdt-1.53.5-hearthdb-2026-07-22-r2`.
- Record 15 exact-ID rules and the seven new deterministic effects without implying external statistics or bundled HearthDb data.
- Preserve the statements that quote recommendations are hidden, historical caches are untouched, unknown effects fail closed, and tests use synthetic state.

## Task 4: Verify and freeze a new release candidate

- Run `npm test`.
- Run the focused active-trinket harness.
- Run `tools/build/build_release.ps1` and `tools/build/validate_repository.ps1`.
- Run `git diff --check`.
- Audit the exact 11-file package allowlist, Firestone/Zero to Heroes endpoint and production call graph, secrets/personal paths/replay data/large files, and confirm `.github` has no diff.
- Build a new candidate outside the repository, record its SHA-256 and size, and do not reuse the `f66eb291` candidate.
- Do not run full VM lifecycle validation, `-RemoveUserData`, or any user-cache operation.

## Task 5: Review, commit, and push

- Inspect `git status`, final diff, and validation evidence.
- Commit the scoped source, tests, plans, and documentation after all required checks pass.
- Push `codex/add-bilingual-readme` without force. Do not create a PR or publish a Release.
- Report the exact-environment Windows 11 24H2 + HDT 1.53.5 smoke gap as a remaining release gate unless separately satisfied or explicitly accepted.

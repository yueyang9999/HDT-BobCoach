# HDT-BobCoach Repository Rules

## Scope

This repository contains only the distributable BobCoach plugin for Hearthstone Deck Tracker (HDT), its tests, release tooling, and public documentation.

## Directory Contract

- `src/BobCoach/`: production plugin source and embedded runtime data required by the plugin.
- `tests/`: automated tests and test-only fixtures. Fixtures must be synthetic, minimal, and free of user data.
- `tools/build/`: deterministic build and validation tools.
- `tools/release/`: packaging, manifest, checksum, and release verification tools.
- `tools/migrate/`: one-time allowlist migration helpers retained for provenance and auditability.
- `src/BobCoach/Core/Decision/`: new decision contracts, ranking, confidence, planning, fallback, and explanation runtime code.
- `src/BobCoach/Resources/DecisionModels/`: checksum-validated approved models embedded into the plugin; candidate and rejected models are prohibited.
- `tests/fixtures/algorithm/`: synthetic, minimal algorithm schemas and fixtures only; no replay-derived states or labels.
- `tools/model/`: deterministic offline model tooling: Node.js standard-library-only data preparation/training/evaluation plus the C# 7.3 policy benchmark harness compiled by its PowerShell runner; no downloaded runtime or package dependencies.
- `docs/README.md`: Chinese-first documentation index and authority map for users, maintainers, contributors, policy, and historical records.
- `docs/user/`: installation, upgrade, rollback, uninstall, privacy, and troubleshooting documentation.
- `docs/maintainer/`: architecture, dependency, build, release, and maintenance documentation.
- `docs/design/`: approved repository and product design decisions.
- `docs/superpowers/specs/`: dated, owner-approved design specifications.
- `docs/superpowers/plans/`: dated implementation plans.
- `.github/`: GitHub templates, funding links, dependency automation, and CI workflows.

## Naming

- Product and repository name: `HDT-BobCoach`.
- Plugin assembly and runtime identity: preserve the identity defined by the production project and release manifest.
- Documentation plans and design records use `YYYY-MM-DD` dates.
- Build artifacts use semantic versions and must never use ambiguous names such as `latest-final.zip`.

## Public Content Allowlist

Only commit files required to build, test, package, document, or maintain the HDT plugin. Production source migration must follow the dependency graph rooted at `BobCoach.csproj`, release scripts, and tests.

Do not commit:

- replay XML, raw logs, recordings, crash dumps, user profiles, credentials, or personal paths;
- screenshots, except for cropped and privacy-cleaned final installation-guide images under `docs/user/images/install/` and cropped, privacy-cleaned, compressed final feature-showcase images under `docs/user/images/features/`; raw screenshots, validation screenshots, user-data screenshots, and screenshots outside those exact directories remain prohibited;
- `.env` files, secrets, tokens, passwords, signing material, or private endpoints;
- `.debate`, `.mcp`, `sessions`, `local-data`, VM images, ISO files, validation evidence, caches, or generated packages;
- the historical Python analysis pipeline, Electron overlay, `BobObserver`, or unrelated experiments;
- build outputs such as `bin/`, `obj/`, `artifacts/`, `TestResults/`, coverage output, or packaged ZIP files.

## Engineering Rules

- Keep the plugin usable offline after installation. Do not add mandatory network dependencies.
- Preserve the privacy contract: gameplay data stays local unless a future feature receives explicit design approval.
- All user-facing functionality remains free. Voluntary support must not unlock features or appear inside Hearthstone or the HDT overlay.
- Do not use Blizzard, Hearthstone, HDT, HearthSim, or other third-party logos in funding or promotional material.
- Prefer deterministic scripts with explicit inputs and outputs. Release packaging must enforce an exact file allowlist and publish a SHA-256 checksum.
- Add or update tests for behavior changes. Do not bypass failures with suppression flags or commented-out code.

## Next-Season Algorithm Data Boundary

Until reliable, complete, version-matched next-season data is available, algorithm work must follow a conservative data-insufficiency policy:

- Implement only season-independent infrastructure: stable candidates, hard constraints, versioned features, Shadow comparison, model validation, confidence, coverage, OOD detection, deterministic planning, explanations, and legacy fallback.
- Do not invent or infer next-season cards, heroes, spells, trinkets, mechanics, card pools, balance values, composition strength, refresh value, economy curves, weights, or labeled scenarios.
- Mark unknown build, season, card pool, entity, and mechanic inputs explicitly as `unknown`; reduce confidence and fall back to `legacy-v1.1.0` when the decision gate requires it.
- Keep every uncalibrated new policy in `Shadow`. `legacy-v1.1.0` remains the player-visible policy and runtime fallback until effect acceptance passes.
- Treat player actions as observation events only, never as correctness labels or training labels.
- Synthetic and prior-season data may test interfaces, boundaries, determinism, performance, and regressions, but may not support claims about next-season decision quality.
- Claims of improved accuracy, win rate, placement, or decision quality require real, trustworthy, version-matched labeled scenarios. Without sufficient evidence, report only engineering readiness or `blocked-by-season-data`.
- Data-dependent work must implement its interfaces, gates, and fallback path, then remain `blocked-by-season-data`; do not fabricate substitute data.
- Prefer low-cost, locally maintainable implementation. Do not add cloud inference, paid APIs, heavyweight model runtimes, or unnecessary dependencies.

Each algorithm task report must answer:

1. Is the change independent of next-season data?
2. Which assumptions currently lack evidence?
3. How does the system degrade when data is missing?
4. Which tests prove engineering correctness only, not strategy superiority?
5. What calibration remains after real next-season data becomes available?

Algorithm acceptance has two separate gates:

- Engineering acceptance: determinism, performance, compatibility, privacy, fallback, and Shadow recording.
- Effect acceptance: at least 300 trustworthy, version-matched labeled scenarios, Shadow comparison, and human review.

Before effect acceptance passes, the required conclusion is: `新算法基础设施已完成或正在完善，但由于目前没有可靠途径获取新赛季完整内容，存在关键数据缺失，实际决策提升尚未得到验证，新策略继续保持 Shadow，并由 legacy-v1.1.0 承担可见推荐和回退。`

## Validation

Before considering a change complete, run the repository validation commands documented in `docs/maintainer/BUILD.md`. At minimum, validation must cover:

- clean restore and Release build;
- automated tests;
- release identity and package allowlist checks;
- sensitive-file, personal-path, replay-data, and large-file scans;
- `git diff --check`.

For release candidates, additionally verify offline install, upgrade, rollback, uninstall, and reinstall in disposable VM overlays. Never modify sealed VM base disks.

## Git And Release

- Development branches use the `codex/` prefix.
- Do not rewrite public history or force-push.
- GitHub Release publication requires explicit final approval even when repository pushes are authorized.
- Keep the former repository private and archived as a read-only history source; do not copy its full history into this repository.

## Cleanup

- Generated files belong outside the repository or in ignored directories and must be removable without affecting source.
- Algorithm tools require an explicit external data root outside the repository. Raw replays, labels, datasets, candidate models, acceptance reports, and validation evidence are never written to the repository and must be removable without changing source.
- Validation evidence stays in the project evidence store, not Git.
- Before adding a new top-level directory, update this file with its purpose, naming, and cleanup policy.

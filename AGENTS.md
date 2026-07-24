# HDT-BobCoach Repository Rules

## Scope

This repository contains only the distributable BobCoach plugin for Hearthstone Deck Tracker (HDT), its tests, release tooling, and public documentation.

## Directory Contract

- `src/BobCoach/`: production plugin source and embedded runtime data required by the plugin.
- `tests/`: automated tests and test-only fixtures. Fixtures must be synthetic, minimal, and free of user data.
- `tools/build/`: deterministic build and validation tools.
- `tools/release/`: packaging, manifest, checksum, and release verification tools.
- `tools/migrate/`: one-time allowlist migration helpers retained for provenance and auditability.
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
- Validation evidence stays in the project evidence store, not Git.
- Before adding a new top-level directory, update this file with its purpose, naming, and cleanup policy.

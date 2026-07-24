# Changelog

All notable changes are recorded here. The project follows Semantic Versioning for package names.

## Unreleased

- Established the clean public product repository, CI, community files, and maintainer documentation.
- Added deterministic repository validation and an official HDT dependency pin for CI.

## 1.0.0 local release candidate (unpublished) - 2026-07-24

- Promoted the package and assembly identity to stable `1.0.0` / `1.0.0.0` naming.
- Preserved the tested layout v10 overlay alignment fix, including zero default horizontal shop offset and migration of the former default.
- Kept Firestone/Zero to Heroes out of the production runtime path; equipped-trinket decisions remain local and deterministic.
- Added mandatory startup verification of the adjacent `BobCoach.dll.sha256` sidecar.
- Updated install, upgrade, rollback, uninstall, packaging, and lifecycle verification so the DLL and checksum remain paired.
- Documented the known limitation that Hearthstone's Timewarp choice screen can cover the hint icon; recommendation calculation remains unaffected.

## 0.2.0-beta.2 local release candidate (unpublished) - 2026-07-22

- Retired all runtime requests, caching, parsing, and retry entry points for Firestone trinket data.
- Hid trinket-offer recommendations by default without disabling equipped-trinket recognition or local effect evaluation.
- Added versioned local `CardId` rules so equipped trinkets affect action legality, economy, card and board synergy, combat expectations, and later decision scoring independently of offer recommendations.
- Kept unknown trinkets conservative and diagnostics rate-limited, with source-independent validation using synthetic game states only.
- Updated public data-source, privacy, notice, architecture, dependency, installation, and release documentation to match the new boundary.

## 0.2.0-beta.1 - 2026-07-20

- Added the x64 `.NET Framework 4.7.2` HDT plugin build.
- Added local Battlegrounds recommendation, overlay, and decision features.
- Added consent-gated `log.config` updates and local-only diagnostic storage.
- Added the strict 11-file offline package, checksums, install, upgrade, rollback, uninstall, and reinstall flows.
- Removed embedded JSON data resources from the formal plugin DLL.

# Changelog

All notable changes are recorded here. The project follows Semantic Versioning for package names.

## 1.2.2 - 2026-09-03

- Fixed turn-one double buy guidance: the two-step action preview now enforces a dynamic-gold budget (executing the first step must leave enough gold for the second), and shop markers are truncated to what the current gold can actually cover instead of showing every affordable card.
- Fixed shop UI misalignment after tavern upgrade: the shop layout tier now adopts the new tavern tier once the upgraded shop is visible (previously it only advanced on a manual refresh, so a tier-2 shop kept rendering at tier-1 slot width).
- Fixed a severe end-of-turn sell suggestion for Flight Expert (BG34_140): it is the hand-to-battle summon engine for hand-buff murlocs; with minions in hand it is now protected from sell suggestions in both the engine and UI layers.
- Recognized the sell-to-produce economy chain (Merchant Elemental BGS_115 sells into Water Droplet, etc.): the card classifier now tags on-sell minion/token gainers as economy plus value generators, so the water-droplet triple chain players use is no longer invisible to the engine.


## 1.2.1 - 2026-09-03

- Fixed hero-power discovery UI (identity reveal EBG_Spell_037 / swap-skill discovers): Power.log choice candidates that are hero powers (TB_BaconShop_HP_* / *_HERO_*p) are now recognized and offered as a HeroPowerDiscover choice batch instead of being filtered out; recorded replay-local as heroPowerDiscover.
- Fixed shop UI starvation under Replenishing Shop (Glowing Gauntlet / Felbat Portrait, tavern always has 7 cards): high-frequency auto-refill dispatches raced the render version counter and every queued UI callback was dropped, so no buy markers appeared until a manual refresh. DispatchRender now coalesces to the latest pending snapshot on version expiry.
- Kept legacy-v1.1.0 as the deterministic visible fallback.


## 1.2.0 - 2026-09-02

- Promoted the Season14 live decision chain to production: hero skill, dark gift, hero playbook, activate, and live-gold safety wired into the visible recommendation path; legacy-v1.1.0 remains the deterministic fallback.
- Added global event-level replay recording (replays/global_*.jsonl) capturing state, recommendation scores, player actions, power-log actions, choices, and combat events per game for offline decision-diff review.
- Added shadow score serialization and decision-event recording so algorithmic recommendations can be compared against the player's actual decisions.
- Added strategic planning/cross-turn projection (StrategicPlan, StrategicMemory, SequenceSearchPlanner, StrategySequencePlanner, TurnProjector, EarlyTurnRecruitGate) and hero playbook/rule catalogs derived from version-matched Season14 data.
- Updated HDT build baseline from 1.56.1.0 to 1.57.1.0 and pinned assembly identity 1.2.0.0.
- Expanded UI lifecycle and behavior test coverage (269 UI lifecycle assertions); kept shop layout and status-strip layout behavior stable (R223 values preserved as deployed).

## Unreleased

- Established the clean public product repository, CI, community files, and maintainer documentation.
- Added deterministic repository validation and an official HDT dependency pin for CI.

## 1.1.0 - 2026-07-30

- Added an adjustable status-guidance font size with immediate preview and persisted settings.
- Added an explicit unlock mode so the status panel can be dragged in the Hearthstone window, then automatically returns to mouse passthrough when settings close.
- Persisted the custom status-panel position, added restore-default behavior, and kept the panel locked on startup.
- Updated the build and dependency contracts for Hearthstone Deck Tracker `1.55.5.0` while preserving the offline-only runtime boundary.
- Fixed shop UI timing and layout handling so recommendations remain aligned through shop refreshes, purchases, and current HDT overlay lifecycle events.

## 1.0.0 - 2026-07-28

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

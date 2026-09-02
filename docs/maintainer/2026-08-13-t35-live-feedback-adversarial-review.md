# T35 Live Feedback Adversarial Review

Date: 2026-08-13

Branch: `codex/season14-algorithm-dark-gift-capture`

## Conclusion

The fixes pass deterministic engineering gates and address the reproduced live-data failure paths. They do not establish strategy-quality improvement or effect acceptance.

## Reviewed Findings

### Cenarius Hero Power

- `BG32_HERO_001p` is now a no-target special power before action enumeration.
- The permanent max-gold simulation uses the same bounded catalog helper instead of the old cap of 11.
- The current HearthDb text and 36.2 season identity drive the playbook, not a hard-coded “always use first five turns” rule.
- Critical health, exhaustion, gold shortage, and later-than-window turns remain fail-closed.
- High-priority hero-playbook actions return before ordinary purchase, two-step, and MCTS overrides.
- The synthetic Cenarius turn-one state proves both `GetRankedCandidates` and the visible `GetBestAction` select `UseHeroPower`.

### Sell Qualification

- Resource/core shop cards can no longer justify a board sell by tier alone.
- A resource/core replacement now requires a clear board-power improvement or a genuinely dead-weight sold minion.
- The visual sell path mirrors the engine qualification.
- The final ranked selected action is revalidated by `IsSellActionValid` before rendering.

### Mystery Cube

- `BG30_MagicItem_703t` is classified as `MysteryCubeActivation` for legacy, season, and unloaded-evaluator paths.
- The activation expires past its observed turn and is excluded from scheduled calibration.
- The extractor bypasses T6/T9 scheduled type filtering only when the current lifecycle context is the mystery-cube activation.
- Before authoritative Power.log candidates arrive, entity residue and scheduled placeholders are suppressed.
- Completion clears the trinket panel and resets the panel state.

### Refresh and Freeze Glow

- `showRefresh=false` now clears the previous refresh glow immediately.
- Refresh reason labels share the same tag as the glow border, so both are removed together.
- Freeze glow and reason label follow the same clear lifecycle.

### Hero Playbook Data Boundary

- The runtime loads 116 base hero profiles from the current generated `hero_skill_registry.json` and `hero_registry.json`.
- A generated curated overlay provides source-backed phases for Cenarius and provenance for the seven official 36.2 changed heroes.
- Missing, mismatched, or `BGDUO_` data fails closed to the existing `HeroPowerEngine`.
- The plugin remains offline; no network dependency was added.

## Hostile Review Matrix

- Empty-board Cenarius with 3 gold at turn 1: `UseHeroPower` is the visible action.
- Cenarius at health below the curated threshold: the profile does not override survival.
- Cenarius after the curated window: no unconditional use.
- Full board, no replacement target: no sell.
- High-tier low-stat resource shop card: no sell on tier alone.
- Final ranked sell invalidated by engine: no visible sell.
- Mystery-cube activation at turn 9 with stale standard greater entities: entity path defers to Power.log.
- Mystery-cube completion: panel clears immediately and does not reappear next turn without a new activation.
- Frame with affordable refresh followed by zero gold and zero free refresh: glow and reason text disappear.

## Residual Engineering Risks

- The permanent max-gold simulator bound remains 20 because the existing `BG28_805` path already uses that safety bound. It is not asserted to be a game-rule maximum unless future HearthDb facts state one.
- Most heroes currently have fact-level playbook coverage; curated phases are limited to Cenarius and the seven official 36.2 changed-hero provenance entries. Full hand-authored strategy guides for every hero remain a separate data collection effort.
- The old Cenarius replay proves the reproduced failure, not the effect of the new DLL. Live validation is still required.
- The one existing `InPlay` filtering warning remains unchanged.

## Verification

- UI lifecycle: 263 PASS, 1 existing warning
- Hero power rule catalog: 31 PASS
- Hero playbook registry: 370 PASS
- Hero playbook decision service: 7 PASS
- Season14 registries: 375 PASS
- T35 synthetic visible-action test: 5 PASS
- Full `npm test`: PASS
- Repository validation: PASS, 595 files scanned
- `git diff --check`: PASS with existing LF/CRLF notices only
- Release DLL bytes: `1010176`
- Release DLL SHA256: `49C362E568B07E4700F388D5B22DF056654BB5BD72CDB4E8F6A12788B72E1FFF`

No win-rate, placement, or strategy-quality improvement is claimed.

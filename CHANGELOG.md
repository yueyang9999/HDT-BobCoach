# Changelog

All notable changes are recorded here. The project follows Semantic Versioning for package names.

## 1.2.9 - 2026-09-05

- fix: 塞纳留斯 T2 铸币上限经济技能优先(ISSUE I-20260905-01): 引擎技能决策 use=True 但 LEVEL_UP 编排无条件覆盖为升本 → 技能被吞(实测 09-05 塞纳留斯 T2 只见升酒馆提示不见主动技能); 新增 econSkillPriority 分支 — 铸币上限技能(AdvancePermanentGoldCap)可负担且未耗尽时优先于升本, 且升本不再覆盖已选技能
- fix: 身份揭晓(EBG_Spell_037 5星酒馆法术)换技能后同步(ISSUE I-20260905-04): EBG_Spell_037 选择完成走 generic ChoiceCompleted(非 PassiveChoiceCompleted — 后者仅费林随从发现), 原修复挂错事件永不触发; 改挂 OnPowerLogChoiceCompleted 捕获 SelectedCardId → SetIdentityRevealSelectedCardId, 选新技能后 state.heroPower 不再沿用旧技能

## 1.2.8 - 2026-09-04

- fix: HINTGUARD take-skip 日志节流(ISSUE I-20260904-08): 同 turn/gold/top/hint 每帧重复打 10-20 次(实测 09-04 晚 100+ 条刷屏淹没真实信号); 新增 _lastHintGuardSig 节流, 同签名只打 1 次(纯日志, 不改决策)

## 1.2.7 - 2026-09-04

- fix: 状态条最终上屏文字留痕(ISSUE I-20260904-03): UISTATUS 帧打印点早于 DispatchRender 内 "拿→X/卖→X/发动→" 追加, 用户实见的两行建议无日志帧; 新增 UIFINAL 帧在 ShowStatusStrip 前记录追加完成后真正上屏的 hint/plan/pick/upgrade/combat 全文(节流同 sig), 此后每帧实际上屏内容可回溯

## 1.2.6 - 2026-09-04

- fix: 状态条 "拿→X" 追加缺失剩余金币守卫(克罗米 T1 实测: 买3金随从后再叠 1 金拿取, gold3 不够仍两行上屏; 1.2.2 同类反馈回归) — 仅当 HintLine 为空且实时金币>=拿取费才追加, 拦截打 HINTGUARD 日志
- chore: HDT 构建基线 1.57.1.0 -> 1.57.6.0(HDT 升级); manifest/测试断言改由 release_identity.json 派生, 防版本漏同步
- chore: tools/release/README_OFFLINE.md 候选包模板同步 1.2.6 / HDT 1.57.6.0(修复 package 测试 "release README identifies the unpublished release candidate")

## 1.2.5 - 2026-09-03

- Fixed Sylvanas's Reclaimed Souls availability: the skill is only usable when a friendly minion died in the last combat (it discovers a copy of one), so the UI no longer suggests using it after a deathless combat; it also now respects the turn-3 unlock (previously T1/T2 suggestions appeared because only the tavern-tier gate was checked).
- Fixed contradictory status text during trinket picks: when the best action is PickTrinket the status strip no longer shows buy-minion suggestions that contradict the trinket panel (cross-game scan of 15 replays).

## 1.2.4 - 2026-09-03

- Fixed Akazamzarak's Prestidigitation (magic secret pick): hero-power discovers from four secrets are now recognized as a discover source (TB_BaconShop_HP_020 explicitly trusted) and the candidate cap is widened to four, so the discover panel shows instead of recording only the completed choice.
- Fixed over-wide status text for long card names: after a level-up filtered rebuild the turn-chain override is re-applied (visualizer's full-name HintLine no longer leaks), and the short-name helper truncates by display width (CJK≈2, ASCII≈1) with trailing whitespace trimmed, so names like "Decoy Conjurer" render as "Decoy Conj…" instead of a full-width row.

## 1.2.3 - 2026-09-03

- Tavern-upgrade guidance now comes first: when the current gold covers the upgrade cost (and the shop has no immediate triple ready), the turn chain opens with the upgrade instead of spending gold on minions first and silently hiding the upgrade at the end (e.g. a T7 gold=9 E.T.C. board previously saw "buy two" while an affordable level-to-4 + hero-power line existed).
- Hero-power discovery now renders the discover panel: hero-skill discovers (Sindragosa's Life Swap / Renathal etc.) reuse the normal discover render chain and clear the panel when the choice ends, instead of only recording the choice invisibly.
- Removed the misleading "→" between sequential turn actions: actions within the same gold budget are now joined with Chinese enumeration commas ("买A、买B"), and "→" is kept only inside a single item to express a true derivation (sell→buy / buy→activate). Addresses user confusion about arrow directionality.
- Two-step action preview display gate: when the first step is a purchase and the second step would need gold the player no longer has (or is a free play the engine would not run after a buy), the second step is suppressed instead of showing a second line that cannot be afforded.
- Added throttled on-screen status-text diagnostics (UISTATUS) to the local log so status-bar wording can be traced to its builder for UI feedback without per-frame log spam.

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


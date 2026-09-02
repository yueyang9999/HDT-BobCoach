# Algorithm Rule Migration Inventory

Task 3 records every direct return in `DecisionEngine.GetBestAction` before a rule is migrated. No row changes runtime ownership in this task.

| Legacy location | Trigger | Class | New owner | Differential scenario ID | Removal gate |
| --- | --- | --- | --- | --- | --- |
| `Core/DecisionEngine.cs:208` | `state == null || !state.GameActive` | Legality | Legacy fallback | `ALG-NULL-STATE` | inactive-state fallback contract |
| `Core/DecisionEngine.cs:213` | `actions.Count == 0` | Legality | Legacy fallback | `ALG-NO-ACTIONS` | empty-candidate contract |
| `Core/DecisionEngine.cs:254` | R0: `tierGap >= 2 && context != DESPERATE` selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R0-TIER-GAP-2` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:260` | R0: one-tier gap with board, health, or full-board condition selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R0-TIER-GAP-1` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:277` | R1 danger health selects highest-valued minion buy | Strategy heuristic | ActionFeatureExtractor | `ALG-R1-DANGER-BUY` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:288` | R1 danger health, cheap or full-board recovery upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R1-DANGER-UPGRADE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:302` | R1 danger health sells weakest minion for a stronger shop replacement | Strategy heuristic | ActionFeatureExtractor | `ALG-R1-DANGER-SELL` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:307` | R1 has no buy, recovery upgrade, or replacement | Safety invariant | Legacy fallback | `ALG-R1-NO-SAFE-ACTION` | fallback contract preserves null result |
| `Core/DecisionEngine.cs:316` | rated trinket action exists | Unsupported special case | Legacy fallback | `ALG-RATED-TRINKET` | rated-trinket differential coverage |
| `Core/DecisionEngine.cs:326` | R2 T2-T3 tier-one curve selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R2-T2-T3` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:341` | R2.5 T4-T5 tier-two curve skips good shop then upgrades | Strategy heuristic | ActionFeatureExtractor | `ALG-R25-T4-T5` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:353` | R2.7 T6-T7 tier-three curve selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R27-T6-T7` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:365` | R2.8 T8-T10 tier-four curve selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R28-T8-T10` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:385` | early spell `BG28_805` is affordable | Strategy heuristic | ActionFeatureExtractor | `ALG-EARLY-SPELL-805` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:391` | early spell `BG28_571` with health above 20 | Strategy heuristic | ActionFeatureExtractor | `ALG-EARLY-SPELL-571` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:397` | early spell `BG28_800` leaves three gold | Strategy heuristic | ActionFeatureExtractor | `ALG-EARLY-SPELL-800` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:403` | early spell `BG28_518` has board and leaves three gold | Strategy heuristic | ActionFeatureExtractor | `ALG-EARLY-SPELL-518` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:409` | early spell `BG28_810` affordable with exact one gold or leaves two gold | Strategy heuristic | ActionFeatureExtractor | `ALG-EARLY-SPELL-810` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:433` | R-time high-tier power gate selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-RTIME-HIGH-TIER` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:441` | R-time low-tier standard window selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-RTIME-LOW-TIER` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:450` | R3 full board and no higher-tier shop card selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R3-FULL-BOARD` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:466` | R3.5 finds weak-board replacement and sells it | Strategy heuristic | ActionFeatureExtractor | `ALG-R35-REPLACE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:486` | R4 early-window power and board threshold selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R4-EARLY-UPGRADE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:500` | R4.5 opponent-power deficit recovery selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R45-DEFICIT-UPGRADE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:532` | desperate gap with free refresh and no tech shop card selects refresh | Strategy heuristic | ActionFeatureExtractor | `ALG-TECH-FREE-REFRESH` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:543` | triple in shop and upgrade available selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-TRIPLE-UPGRADE-TIMING` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:555` | triple reward value exceeds `0.2` selects triple buy | Strategy heuristic | ActionFeatureExtractor | `ALG-TRIPLE-BUY` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:572` | R6 high-tier shop selects highest-valued minion buy | Strategy heuristic | ActionFeatureExtractor | `ALG-R6-HIGH-TIER-BUY` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:591` | R7 improving shop outside mandatory early upgrade selects best buy | Strategy heuristic | ActionFeatureExtractor | `ALG-R7-IMPROVING-SHOP` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:603` | R8 weak shop, healthy state, and upgrade reserve selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R8-WEAK-SHOP-UPGRADE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:612` | R9 same-tier shop, weak board, and two-gold reserve selects upgrade | Strategy heuristic | ActionFeatureExtractor | `ALG-R9-WEAK-BOARD-UPGRADE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:1009` | paid-refresh audit at T5 or earlier substitutes highest-tier buy | Strategy heuristic | ActionFeatureExtractor | `ALG-REFRESH-EARLY-BUY` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:1016` | paid-refresh audit at T1-T2 substitutes non-refresh action | Strategy heuristic | ActionFeatureExtractor | `ALG-REFRESH-EARLY-NONREFRESH` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:1055` | paid-refresh audit without a search objective substitutes highest-tier buy | Strategy heuristic | ActionFeatureExtractor | `ALG-REFRESH-NO-OBJECTIVE` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:1093` | sell guard declines sell and finds a minion buy | Safety invariant | Legacy fallback | `ALG-SELL-GUARD-BUY` | sell-safety differential coverage |
| `Core/DecisionEngine.cs:1095` | sell guard declines sell and finds refresh | Safety invariant | Legacy fallback | `ALG-SELL-GUARD-REFRESH` | sell-safety differential coverage |
| `Core/DecisionEngine.cs:1097` | sell guard finds material replacement | Safety invariant | Legacy fallback | `ALG-SELL-GUARD-SELL` | sell-safety differential coverage |
| `Core/DecisionEngine.cs:1158` | two-step plan exceeds best single step by `TWO_STEP_THRESHOLD` | Strategy heuristic | ActionFeatureExtractor | `ALG-TWO-STEP-OVERRIDE` | planner differential preserves legacy action |
| `Core/DecisionEngine.cs:1246` | no best action with affordable economy spell | Strategy heuristic | ActionFeatureExtractor | `ALG-ECON-SPELL-FALLBACK` | feature differential preserves legacy action |
| `Core/DecisionEngine.cs:1254` | final greedy or MCTS-selected best action | Strategy heuristic | Legacy fallback | `ALG-FINAL-BEST-ACTION` | legacy fallback remains enabled |

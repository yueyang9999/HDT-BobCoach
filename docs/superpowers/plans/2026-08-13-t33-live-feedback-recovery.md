# T33 Live Feedback Recovery Plan

Date: 2026-08-13

## Objective

Fix the latest four observed live-game failures without changing the visible legacy policy or making strategy-quality claims:

1. Round 1 text guidance and shop-panel priority disagree.
2. Round 2 loses the expected full-gold baseline and suppresses upgrade/buy/hero-power hints.
3. Round 3 uses a stale static hero-power cost and misses a live one-cost skill prompt.
4. `BG28_805` Strike Oil is scored as an economy heuristic, but its permanent max-gold effect is not carried through state, simulation, or cross-turn projection.

## Root Causes

- `GoldTracker.Advance` returned `0` on non-recruit frames. The extractor interpreted that as a real balance and could poison the UI before the new-turn baseline was established.
- HDT's combat flag could remain true after combat while Power.log already entered the recruit phase. `NormalRecruitAvailable` therefore stayed false and the new-turn baseline was never created.
- `BobCoachPlugin` passed `HeroSkillCatalog.Cost` into `HeroSkillDecisionService`, while `state.HeroPowerCost` already contained the live HDT entity cost.
- `DecisionVisualizer` independently sorted `ShopCardScore` and built the text action sequence from `bestAction`, with no binding between the two.
- `BG28_805` had purchase-score bonuses, but `MaxGold`, `Simulator.UseSpell`, and `TurnProjector` did not preserve its permanent effect.

## Implementation Order

1. Add T33 red contract.
2. Preserve unknown `-1` and known tracked gold across non-recruit frames.
3. Override a stale HDT combat flag only when actual tavern-shop entities are present.
4. Feed `state.HeroPowerCost` into visible hero-skill decisions.
5. Promote the selected `BuyMinion`/`BuySpell` card to the primary shop marker.
6. Read the HDT `RESOURCES` maximum-gold tag into `state.MaxGold`.
7. Model `BG28_805` in `Simulator.UseSpell` and carry observed max-gold bonuses in `TurnProjector`.
8. Run build, contract, behavior, package, repository, and adversarial checks.
9. Deploy with archive and verify every installed HDT plugin directory.

## Acceptance Boundary

- Engineering gates pass.
- The new DLL is deployed for live verification.
- No win-rate, placement, or strategy-quality improvement is claimed.
- The existing cross-turn planner remains Shadow and the legacy visible decision path remains authoritative.

# T33 Live Feedback Adversarial Review

Date: 2026-08-13

## Conclusion

The fixes pass deterministic engineering gates and address the reproduced live-data failure paths. They do not establish strategy-quality improvement or effect acceptance.

## Reviewed Findings

### Gold Tracking

- Non-recruit frames no longer return a fabricated real `0`.
- Unknown remains `-1`; a known balance is preserved without operation advancement.
- A new turn is initialized on the first valid recruit frame.
- HDT combat is overridden only when `HDT combat == true` and actual tavern-shop entities are present.
- During combat, when no shop evidence exists, gold operations remain closed. This avoids treating combat minion deaths as sells.

### Hero Power Cost

- Visible decisions consume `state.HeroPowerCost`.
- Catalog cost remains only a fallback.
- The dispatch affordability check already uses the live cost; the decision service and glow now agree.

### Shop UI Priority

- The actual selected buy card is promoted to the first shop marker.
- Existing marker count limits are preserved.
- Minion and spell purchases are covered.
- Other cards retain their independent quality signals.

### Permanent Max Gold

- HDT `RESOURCES` is treated as maximum gold when it is at least the base turn cap and no more than 20.
- `BG28_805` use raises the simulated maximum cap by one and consumes the spell.
- `TurnProjector` carries the observed non-turn-base cap bonus to the next turn.
- This is a bounded state fact, not a subjective weight increase.

## Residual Engineering Risks

- Shop-evidence detection is a fallback for stale HDT phase flags. It must remain conditional; replacing HDT phase with raw Power.log `MAIN_ACTION` would be unsafe because combat also emits `MAIN_ACTION`.
- `RESOURCES` interpretation is version-sensitive. The current HDT baseline and captured Power.log both show it as maximum resources, but future HDT/season changes require a new live replay before extending this behavior.
- The promoted visual marker is based on the visible legacy `bestAction`; Shadow planner output intentionally does not override it.
- Live strategy quality still requires version-matched replay evidence.

## Verification

- T33 live feedback contract: PASS
- GoldTracker mirror: 23 PASS
- Algorithm feature harness: PASS
- Contract suite: PASS
- Behavior suite: PASS
- Package suite: PASS
- Repository validation: PASS
- `git diff --check`: PASS with existing LF/CRLF notices

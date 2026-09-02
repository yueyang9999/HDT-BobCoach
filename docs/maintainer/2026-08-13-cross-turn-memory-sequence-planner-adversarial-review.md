# Cross-Turn Memory and Sequence Planner Adversarial Review

Date: 2026-08-13

## Conclusion

The implementation passes the engineering gate for deterministic, bounded, offline, privacy-preserving Shadow infrastructure. It does not pass effect acceptance. No win-rate, placement, or strategy-quality improvement is claimed.

## Verified Properties

- Legacy `GetBestAction` is not changed by the new planner. The sequence bonus is confined to `GetRankedCandidates`.
- The planner uses depth 3 and beam width 8.
- The search returns deterministic output for equal inputs and normalizes non-finite scores.
- Null roots, null actions, null expansion steps, and null expand callbacks are fail-closed.
- The game adapter expands each branch from the accumulated simulated state.
- Strategic memory has bounds of 64 core IDs and 32 pair IDs and is reset at new-game initialization.
- Memory snapshots are defensive copies.
- Replay output stores only bounded card/action identifiers and does not include raw logs, personal data, or player-action labels.
- Planning and memory failures cannot break the visible legacy path.

## Critical Findings Resolved

### Memory Was Not Used

The initial adapter accepted `StrategicMemorySnapshot` but did not read it. This made the cross-turn claim false. The current adapter includes bounded observed-core and observed-pair evidence in Shadow state scoring.

### Expansion Did Not Carry State

The initial adapter simulated only one root action when expanding later branches. This could miss the legality and value of the second and third actions. A sequence-context search overload now simulates the full prefix before enumerating follow-ups.

### Cross-Game Stale State

The initial memory was never reset in the plugin lifecycle. A new `DecisionEngine.ResetStrategicMemory()` is called from `EnsureBattlegroundsInitialized`.

### Unbounded Memory

The initial sets could grow for the lifetime of the plugin. Fixed with deterministic FIFO caps.

### Shadow Planner Could Break Legacy Policy

The initial integration called the planner before the visible policy without exception isolation. Build and memory observation are now contained, so failures fall back to the unchanged legacy path.

### Malformed Replay Plans

The initial serializer assumed every step and action was non-null. The serializer now filters invalid entries and keeps valid plan fields.

## Residual Engineering Limitations

- The 32-bit plan hash is deterministic but is not collision-resistant. It is an audit identity, not a cryptographic or training-label key.
- `StrategyPlan` is exposed as a mutable object from `LastStrategyPlan`. Internal consumers are trusted; future public APIs should expose a defensive copy.
- Memory scoring is an uncalibrated heuristic. It can only influence Shadow ranking; it does not establish next-season decision quality.
- The current memory model records board/hand core and pair evidence. Shop opportunity memory and explicit resource-trigger models remain future work.
- The three 2026-08-13 replays were produced before this working-tree implementation. Running the new analyzer over them verifies field compatibility, not effect acceptance.

## Acceptance Boundary

The next gate is real, version-matched replay/UI validation. A new DLL is required before the changed planner can be observed in a live game. The 300-scenario effect acceptance gate remains `blocked-by-season-data`.

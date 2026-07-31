# BobCoach next-season algorithm optimization design

Date: 2026-07-31
Status: owner-approved for implementation planning

## 1. Decision summary

BobCoach will replace its current "rules decide first, scoring and shallow MCTS run only when reachable" behavior with a unified candidate-ranking pipeline. The first production model will be a local, regularized linear pairwise ranker over state-action features. A lightweight tree ranker may be evaluated later, but it is not part of the first implementation phase.

The target pipeline is:

```text
game-state validation
    -> legal action enumeration
    -> hard-constraint filtering
    -> one-step simulation
    -> state-action feature extraction
    -> unified candidate ranking
    -> confidence and risk gating
    -> selective deterministic beam planning
    -> explanation generation
    -> recommendation or legacy fallback
```

This design optimizes for a solo developer:

- no mandatory network access, cloud inference, paid API, or hosted training service;
- no deep reinforcement learning;
- no attempt to increase the depth of the current random MCTS;
- no one-shot rewrite of the existing decision engine;
- model training remains offline, while plugin inference remains local and deterministic;
- the current v1.1.0 policy remains available as a frozen baseline and runtime fallback throughout migration.

The primary success metric is a reduction in severe mistakes at leveling, refreshing, selling, freezing, buying, and composition-transition decisions. Average placement and top-four rate are long-term outcome metrics, not the initial release gate.

## 2. Current-state diagnosis

The production decision path is currently centered on:

```text
GameStateExtractor
    -> ActionEnumerator
    -> Simulator
    -> FeatureExtractor (22 state features)
    -> ValueFunction (linear V(state))
    -> DecisionEngine (rule layers and early returns)
    -> OverlayRenderer
```

The following limitations motivate the redesign:

1. `DecisionEngine` contains many rule-layer early returns. When a rule returns, later unified scoring, two-step lookahead, and MCTS cannot compare that recommendation with other action classes.
2. `ValueFunction` estimates `V(nextState)` using fixed weights. It does not directly represent action cost, opportunity cost, remaining action space, or uncertainty as `Q(state, action)`.
3. `StrategyAdjuster` applies fixed weight deltas, action biases, and danger-health thresholds. It is contextual tuning, not learning from decision quality.
4. `ProfileEngine` models player preference. Treating preference as decision quality can reinforce a player's existing mistakes.
5. The current MCTS uses a small random search whose leaf evaluation is still the same linear state value. It does not add a reliable source of strategic knowledge.
6. The MCTS call site states that it should override only when significantly better, but currently an action difference is sufficient to replace the prior recommendation.
7. The two-step threshold is `1.03` while its comment describes an 8% threshold. Multiplicative comparison is also invalid when candidate values may be negative.
8. Existing tests mainly protect rule and packaging contracts. They do not provide a frozen decision-quality benchmark across action classes.

Preliminary local replay inspection also found duplicated snapshots and temporal misalignment between inferred player actions and the state used for the recommendation. Existing replay files are useful for diagnosis and scenario discovery, but they are not accepted as direct supervised-learning labels.

## 3. Goals and non-goals

### 3.1 Goals

- Put buy, play, sell, level, refresh, and freeze actions on one comparable scoring surface.
- Reduce severe errors at high-impact decision points by at least 20% relative to the frozen v1.1.0 baseline before activation.
- Make action scoring aware of immediate value, opportunity cost, follow-up actions, survival risk, and transition cost.
- Use lookahead only when the choice is high impact or the leading candidates are close.
- Isolate cross-season reasoning from season-specific card and mechanic semantics.
- Detect unknown or out-of-distribution states and fall back safely.
- Produce explanations from actual score contributions rather than from a separate reason-rule system.
- Create a versioned local data contract that supports future learning without uploading gameplay data.

### 3.2 Non-goals

- Proving a causal improvement in average placement during the initial implementation phase.
- Training directly on all existing replay frames.
- Copying the player's historical choices as ground truth.
- Building a general combat simulator or a full-game reinforcement-learning environment.
- Adding mandatory online statistics, telemetry, accounts, subscriptions, or paid features.
- Covering every new-season card effect with handcrafted decision rules before the season is playable.
- Replacing every existing rule in one change.

## 4. Success metrics

The benchmark must evaluate the frozen v1.1.0 policy and every candidate policy on exactly the same versioned scenarios.

### 4.1 Primary metric

```text
severe_error_rate
    = severe recommendations / evaluable high-impact scenarios

relative_error_reduction
    = (baseline_error_rate - candidate_error_rate) / baseline_error_rate
```

A severe error is an action judged likely to cause immediate death, waste a decisive amount of economy, destroy a key composition asset without adequate return, miss an unambiguous triple or power breakpoint, or take an action dominated by another legal action.

### 4.2 Secondary metrics

- Top-1 acceptable-action accuracy: the first recommendation belongs to the accepted answer set.
- Top-3 coverage: at least one of the first three candidates belongs to the accepted answer set.
- Severity-weighted regret: the difference between the selected action grade and the best accepted grade.
- Category regression: per-category change for level, refresh, buy, sell, freeze, play, and transition decisions.
- Override precision: among disagreements with v1.1.0, the proportion judged better, neutral, or worse.
- Runtime: P50, P95, and P99 latency for ranking and optional planning.
- Fallback correctness: the proportion of corrupted, incomplete, or unsupported states that take the defined fallback path.

### 4.3 Activation gates

The new policy cannot replace the visible v1.1.0 recommendation unless all of the following hold:

- severe-error relative reduction is at least 20%;
- Top-1 acceptable-action accuracy improves by at least 10 percentage points;
- Top-3 coverage improves by at least 15 percentage points;
- no high-impact action category regresses by more than 5 percentage points;
- normal ranking P95 is at most 10 ms;
- decisions that invoke planning have P95 latency at most 25 ms;
- model-load, critical-state, and unsupported-mechanic fallback tests pass 100%;
- no known severe-error regression is hidden by an improvement in aggregate score.

Failure to meet a gate keeps the new policy in shadow mode. It is not a reason to weaken the gate.

## 5. Target architecture

### 5.1 Decision orchestration

`DecisionOrchestrator` owns the pipeline but not the domain logic of its components. It receives an immutable game-state snapshot and returns a `DecisionResult` containing the chosen action, ranked alternatives, confidence, planner status, fallback status, and explanation factors.

The orchestrator must be able to run two policies for the same snapshot:

- `LegacyDecisionPolicy`: the frozen v1.1.0 behavior;
- `UnifiedRankingPolicy`: the new candidate-ranking behavior.

In shadow mode, only the legacy result reaches the overlay. The unified result and the difference summary may be written to the local decision record when local replay recording is enabled.

### 5.2 Candidate generation

`CandidateGenerator` reuses `ActionEnumerator` where its legality model is correct. It produces stable action descriptors and does not assign strategic priority.

Every descriptor must include:

- action type;
- source and target zones;
- stable entity ID when available;
- card ID when applicable;
- gold and resource costs;
- simulator support level;
- a deterministic action identity for replay correlation and tie breaking.

### 5.3 Hard constraints

`HardConstraintFilter` may reject an action only for:

- game legality;
- unavailable resources;
- an invalid or missing target;
- an explicitly modeled irreversible action that is known to be invalid under current game rules;
- a tested safety invariant whose violation is always dominated.

Turn curves, preferred compositions, generic health thresholds, common leveling patterns, and similar strategy heuristics are not hard constraints. They become features, risk terms, or ranking evidence.

Each existing early-return rule must be classified before migration:

| Classification | Treatment |
| --- | --- |
| Legality | Move to candidate generation or hard constraints |
| Safety invariant | Keep behind an explicit test and reason code |
| Strategy heuristic | Convert to a feature or soft score |
| Duplicate or conflicting rule | Remove only after differential tests prove coverage |
| Unsupported special case | Keep in legacy fallback until its semantics are modeled |

### 5.4 State-action feature extraction

`ActionFeatureExtractor` computes `phi(state, action, simulatedNextState)`. It may reuse current state features, but the new feature schema is named and versioned rather than defined only by array position.

The first production schema should contain approximately 35-45 features in these groups:

| Group | Examples |
| --- | --- |
| State context | turn, tavern tier, health plus armor, gold, hand slots, board slots |
| Immediate power | attack/health delta, effective keywords, unit count, board-power delta |
| Economy | net gold cost, usable remaining gold, wasted gold, refresh/freeze value |
| Action space | legal follow-up count, playable follow-ups, hand/board congestion |
| Growth | pairs, triple progress, scaling engines, key-component retention |
| Survival | expected damage exposure, low-health tempo need, weak-board leveling risk |
| Composition | current synergy, target compatibility, number/value of pieces abandoned |
| Uncertainty | unknown-effect count, simulator coverage, stochastic-outcome range |

The linear first-stage model may include a limited set of explicit interaction features:

- low health x immediate power;
- board full x buy action;
- level action x expected damage exposure;
- pair progress x matching shop card;
- remaining gold x available follow-up actions;
- transition action x protected composition value.

Feature extraction must not read player-preference weights. Preference is a separate concern and cannot make a lower-quality action appear strategically superior.

### 5.5 Unified ranking

The first model approximates action value as:

```text
Q_theta(state, action) = theta dot phi(state, action)
```

Training uses regularized pairwise logistic ranking:

```text
loss = sum(log(1 + exp(-label * (Q(preferred) - Q(other)))))
       + lambda * squared_norm(theta)
```

This choice is intentional:

- inference is fast and deterministic;
- every score can be decomposed into feature contributions;
- a solo developer can inspect failures without specialized infrastructure;
- the model artifact is small and can be validated before loading;
- explicit features make early data requirements lower than for a large black-box model.

The ranker uses a single score scale for every action type. Per-action arbitrary multipliers are prohibited unless they are represented as named, tested features.

The model artifact must contain:

- artifact schema version;
- feature schema version and ordered feature names;
- normalization statistics;
- weights and intercept;
- training-set identity and season range without embedding user data;
- benchmark metrics;
- creation time and content checksum.

An artifact with a schema mismatch, invalid number, missing feature, or failed checksum is rejected before inference.

### 5.6 Selective deterministic planning

Planning is not the default path. It runs when:

- the normalized score margin between leading candidates is below a calibrated threshold;
- an action is high impact or difficult to reverse, such as leveling or selling a protected unit;
- survival risk crosses a calibrated boundary;
- the first action has materially different follow-up flexibility from the runner-up.

The initial planner uses:

- root candidates: Top-4 ranked actions;
- beam width: 6;
- maximum depth: 2-4 recruit-phase actions, constrained by remaining resources;
- deterministic tie breaking;
- a hard elapsed-time budget;
- the best complete path found so far when time expires.

Known actions use deterministic simulation. Shop refreshes use a separate expected-shop estimator based on tavern tier, available pool information, and relevant card classes. The planner does not use random rollout noise to create false confidence.

The old MCTS cannot override a recommendation merely because it selected a different action. During migration it either remains inside the legacy policy or is disabled as an override once the new planner passes its differential benchmark.

### 5.7 Confidence and fallback

Decision confidence is derived from:

- score margin;
- critical-state completeness;
- semantic and simulator coverage;
- distance from the validated feature distribution.

These factors are combined conservatively. A critical floor failure forces fallback even when the other factors are high.

Fallback order is:

1. use the unified ranker when confidence is sufficient;
2. invoke the planner when candidates are close and planning is supported;
3. use known-effect scoring with an uncertainty penalty for partially supported content;
4. return the legacy v1.1.0 decision for model failure, critical-state failure, or severe distribution shift;
5. return no strong recommendation if neither policy can produce a safe, valid action.

Planner timeout falls back to the best complete ranked path. It does not fail the whole decision. Exceptions are rate-limited in diagnostics and must not escape into the HDT UI thread.

### 5.8 Explanation generation

`ExplanationBuilder` reads the selected action's score decomposition and planner result. It reports the two or three largest decision-relevant factors in player-facing language.

Explanation rules cannot modify action ranking. Raw model values, confidence formulas, and implementation details remain diagnostic data rather than overlay instructions.

## 6. Data design

### 6.1 Event alignment

The new recorder uses one stable `decisionId` to join events:

```text
DecisionCreated
    -> RecommendationIssued
    -> PlayerActionObserved
    -> CombatOutcomeAttached
    -> GameOutcomeAttached
```

`DecisionCreated` is emitted before the recommendation and contains the state before any observed player action. `PlayerActionObserved` is attached later and is never written as if it were a label for the post-action state.

A decision sample contains:

- record and feature schema versions;
- game build, season ID, and card-pool identity;
- decision ID, turn, phase, and state fingerprint;
- the state before the decision;
- all legal candidate descriptors;
- feature vector, raw rank score, risk adjustment, and planner value per candidate;
- the issued recommendation, policy ID, confidence, and fallback reason;
- the later observed player action when correlation succeeds;
- optional combat and final-game outcomes.

Unchanged state fingerprints do not create duplicate decision samples. Diagnostics must distinguish a missing observed action from a player action that differs from the recommendation.

### 6.2 Labels

The first benchmark contains at least 300 unique high-impact decision scenes. Labels support multiple acceptable actions:

- preferred;
- acceptable;
- weak but not severe;
- severe error;
- not evaluable because semantics are incomplete.

Pairwise preferences are generated only when the relative ordering is defensible. Deterministic weak labels may cover legality, clear resource waste, unambiguous triple misses, and other tested invariants. Weak labels cannot dominate the benchmark or silently become hard constraints.

Observed player actions represent behavior, not correctness. Final placement is attached at game level and is not copied onto every decision as a reward.

### 6.3 Dataset boundaries

- Split by whole game, never by neighboring frames.
- Keep a final time-based holdout from newer games.
- Report old-season and current-season metrics separately.
- Deduplicate by normalized state and candidate fingerprint before splitting.
- Keep raw replays, labels derived from private games, and generated datasets outside the repository.
- Commit only synthetic minimal fixtures and approved aggregate model metadata.
- Keep all gameplay data local unless a future design receives explicit approval.

Before implementation uses the existing large replay corpus, it must be moved off the system drive according to the workspace storage rules. Migration, verification, compatibility handling, and deletion of the source are separate operational work; deletion still requires explicit authorization.

## 7. New-season adaptation

Season support is split into two layers.

### 7.1 Cross-season layer

The cross-season layer owns economy, health, board and hand capacity, tavern progression, immediate power, action flexibility, triples, survival pressure, and transition cost. Its feature meanings remain stable across patches.

### 7.2 Season extension layer

The season layer supplies:

- new mechanic semantics;
- versioned card-effect facts;
- effective game-rule changes;
- season-only state and action features;
- simulator coverage declarations;
- card-pool and distribution inputs for expected-shop estimation.

Unknown entities produce explicit unknown-feature values and lower confidence. They do not silently receive neutral treatment with full confidence.

At the start of a major season:

1. update state extraction and mechanic semantics first;
2. keep the legacy policy visible while the unified policy runs in shadow mode;
3. prioritize labeling disagreements in leveling, refresh, protected sales, and transitions;
4. activate the unified policy only after all release gates pass on current-season scenarios;
5. keep low-confidence states on the fallback path;
6. decide on tree ranking only after the linear model's remaining errors justify it.

## 8. Model evolution

### 8.1 Stage one: linear pairwise ranker

The regularized linear ranker is the required first production model. Feature and benchmark quality take priority over model complexity.

### 8.2 Stage two: optional LightGBM/LambdaMART shadow model

A tree ranker may be introduced only when all of the following are true:

- the event-aligned dataset and 300-scene benchmark are stable;
- there are enough independent labeled comparisons to prevent a tree model from memorizing a small set of games;
- the tree model improves held-out Top-1 accuracy by at least 3 percentage points beyond the linear model;
- severe-error performance and confidence calibration do not regress;
- local inference remains within the latency budget;
- the exported artifact can be validated and evaluated without adding a mandatory runtime service.

The tree model first runs only in shadow mode. It does not replace the linear model based on training-set accuracy.

## 9. Implementation sequence and effort

The expected first-stage effort is 20-30 effective solo-development days. This is an engineering estimate, not a calendar guarantee.

| Phase | Effective days | Deliverable |
| --- | ---: | --- |
| P0: benchmark and contracts | 3 | frozen v1.1.0 baseline, label schema, initial scenario harness, decision-event contract |
| P1: pipeline isolation | 4-5 | orchestrator, legacy policy wrapper, hard-constraint boundary, shadow comparison |
| P2: unified ranking | 6-7 | named state-action features, model artifact contract, linear pairwise ranker |
| P3: planning and confidence | 4-5 | selective beam planner, expected-refresh evaluation, confidence/OOD fallback |
| P4: calibration and acceptance | 3-5 | at least 300 labeled scenes, category analysis, performance and activation report |
| Contingency | 0-5 | new-season semantic gaps, simulator corrections, benchmark regressions |

Implementation must proceed behind shadow mode. Moving to the next phase does not imply that the prior phase is eligible for visible activation.

## 10. Verification strategy

### 10.1 Automated tests

- Feature unit tests for every named feature and relevant interaction.
- Candidate and hard-constraint tests for legality and resource boundaries.
- Golden-scenario tests that compare legacy and unified policies.
- Model-contract tests for schema mismatch, truncation, invalid numbers, and checksum failure.
- Determinism tests for ranking, tie breaking, and planner output.
- Planner timeout tests that return the best complete path.
- OOD and missing-semantic tests that force the expected confidence reduction or fallback.
- Event-alignment tests that prevent post-action state from being paired with the observed action.
- Deduplication and whole-game dataset-split tests.
- Performance tests for normal ranking and planned decisions.
- Existing repository build, behavior, package, sensitive-file, personal-path, replay-data, large-file, and `git diff --check` gates.

### 10.2 Review reports

Each candidate model receives a local acceptance report containing:

- baseline and candidate artifact identities;
- benchmark and season identities;
- overall and per-action metrics;
- a list of every new severe error;
- disagreement samples and override precision;
- confidence/fallback rates;
- latency percentiles;
- the activation decision and reason.

The report contains aggregate evidence only. It must not be committed if it exposes replay data or personal paths.

## 11. Expected improvement

The following ranges are planning hypotheses conditioned on correct implementation, a representative benchmark, and successful activation gates. They are not promises of ladder outcomes.

| Policy stage | Severe-error change | Top-1 change | Average-placement hypothesis | Top-four hypothesis |
| --- | ---: | ---: | ---: | ---: |
| v1.1.0 | baseline | baseline | baseline | baseline |
| Stage-one minimum gate | at least -20% | at least +10 pp | not claimed | not claimed |
| Stage-one target | -25% to -35% | +12 to +18 pp | improve by 0.15-0.35 | +3 to +5 pp |
| Mature linear plus tree ranker | -35% to -45% | +18 to +25 pp | improve by 0.25-0.50 | +4 to +8 pp |

Offline decision metrics can be measured directly. Average placement and top-four rate cannot be attributed reliably to the algorithm without a large, controlled sample because they are affected by player rating, heroes, patches, lobbies, and variance. They remain long-term directional checks rather than first-stage release criteria.

## 12. Risks and controls

| Risk | Control |
| --- | --- |
| New season invalidates old rules or weights | Separate cross-season and season features; shadow mode and current-season gates |
| Existing replay labels teach the wrong timing | Event-aligned decision IDs; old data used only for diagnosis and scenario discovery |
| Model copies player mistakes | Player action is behavior, not truth; quality labels and preferences remain separate |
| Linear model underfits interactions | Add only justified explicit interactions; evaluate a tree model later in shadow mode |
| Tree model overfits limited data | Whole-game/time holdouts and a minimum held-out gain beyond the linear ranker |
| Search adds latency without knowledge | Selective deterministic planning, strict budget, best-complete-path timeout behavior |
| Unknown card effect receives a confident score | Explicit unknown features, semantic coverage score, confidence floor, fallback |
| Aggregate metrics hide a fatal regression | Per-category gates and mandatory review of every new severe error |
| Migration changes too much at once | Legacy policy wrapper, shadow comparison, phased rule classification |
| Personal data enters the public repository | Synthetic fixtures only, repository scans, local aggregate reports |

## 13. Approved decisions

- Decision quality is measured first by severe-error reduction at key decisions.
- Average placement and top-four rate are long-term validation metrics.
- The implementation follows the full 20-30 effective-day path; the rejected one-week challenger shortcut is not part of this design.
- Unified state-action ranking is the first-stage architecture.
- A regularized linear pairwise ranker is the first production model.
- LightGBM/LambdaMART is optional and gated by data and held-out improvement.
- Beam search replaces deeper random rollout as the preferred limited-lookahead method.
- Runtime remains local, offline-capable, free, and privacy preserving.
- The v1.1.0 policy remains the baseline and fallback until the new policy passes every activation gate.

## 14. Next gate

After the user reviews and approves this written specification, create a separate phased implementation plan under `docs/superpowers/plans/`. No production algorithm code changes are authorized by this specification alone.

# Active Trinket Rule Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four exact, deterministic equipped-trinket rules while preserving the independent hidden quote-recommendation boundary.

**Architecture:** Extend the versioned `TrinketEffectRegistry` and immutable `ActiveTrinketContext`; pass each combat side's existing hand into its own start-of-combat effect call. Keep extraction fail-closed and test production ordering and source-based classification without Firestone or user data.

**Tech Stack:** C# 7-compatible HDT plugin code, HearthDb 1.53.5 reference snapshot, PowerShell Roslyn harnesses, Node.js repository tests.

## Global Constraints

- Preserve all trinket recognition, local rules, recommendation service, and UI code; quote recommendations remain hidden by default.
- Equipped effects must not share an enable condition with quote recommendations or `TrinketRecommendationsVisible`.
- Use exact, case-sensitive CardIds and version the local registry as `hdt-1.53.5-hearthdb-2026-07-22`.
- Unknown effects fail closed and produce exact-CardId, once-per-game diagnostics.
- Tests use synthetic state only and never read Firestone data or user cache.
- Do not read, migrate, delete, or clean historical user cache; do not run full VM validation or `-RemoveUserData`.
- Do not modify CI/CD, create a PR, or publish a Release.

---

### Task 1: Lock the expanded rules with failing tests

**Files:**
- Modify: `tests/ActiveTrinketEffectsHarness.cs`

**Interfaces:**
- Consumes: `TrinketEffectRegistry.Resolve(IEnumerable<string>)`, `ActiveTrinketContext.ApplyStartOfCombat(IList<CombatUnit>, IList<MinionData>)`.
- Produces: regression coverage for exact resolution, owner-only hand effects, board ordering, tribe filtering, empty-hand behavior, and synergy targets.

- [ ] **Step 1: Add exact CardId constants and expected ruleset version**

Add constants for `BG30_MagicItem_542`, `BG35_MagicItem_702`, `BG30_MagicItem_441`, and `BG35_MagicItem_754`; change the expected version to `hdt-1.53.5-hearthdb-2026-07-22`.

- [ ] **Step 2: Add synthetic start-of-combat assertions**

Construct owner and opponent boards/hands and assert:

```csharp
dreamcatcher.ApplyStartOfCombat(ownerBoard, ownerHand);
stegodon.ApplyStartOfCombat(ownerBoard, ownerHand);
tinyfin.ApplyStartOfCombat(ownerBoard, ownerHand);
dramaloc.ApplyStartOfCombat(ownerBoard, ownerHand);
```

The tests must prove highest-stat selection, additive hand-stat grants, left-most selection, tribe filtering, no-op empty hands, and no opponent leakage.

- [ ] **Step 3: Add simulator wiring and production-source guard assertions**

Read `GameStateExtractor.cs` as source text and assert that `ExtractActiveTrinkets(entities, state)` precedes `TrackGold(state, entities)`, both shop and hand assignments call the exact CardId classifier, and unknown diagnostics reach `ExtractorLog` without `HasLocalTrinketFact` filtering.

- [ ] **Step 4: Run the focused harness and verify RED**

Run:

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.53.5\build-reference'
powershell -ExecutionPolicy Bypass -File tests/test_active_trinket_effects.ps1
```

Expected: compilation or assertion failure because the four rules and two-argument combat method do not exist yet.

### Task 2: Implement the exact local effects

**Files:**
- Modify: `src/BobCoach/Core/TrinketEffectRegistry.cs`
- Modify: `src/BobCoach/Core/ActiveTrinketContext.cs`
- Modify: `src/BobCoach/Core/CombatSimulator.cs`

**Interfaces:**
- Consumes: active CardIds, owner `IList<CombatUnit>`, owner `IList<MinionData>`.
- Produces: `ApplyStartOfCombat(IList<CombatUnit> ownerBoard, IList<MinionData> ownerHand)` and eight-card exact registry resolution.

- [ ] **Step 1: Register the four CardIds**

Add exact constants, booleans, switch cases, constructor parameters, and resolved IDs. Update `RuleSetVersion` to `hdt-1.53.5-hearthdb-2026-07-22`.

- [ ] **Step 2: Implement deterministic start-of-combat effects**

In `ActiveTrinketContext`, compute snapshots before mutation:

```csharp
int highestBoardAttack = ownerBoard.Where(unit => unit != null).Max(unit => unit.Attack);
MinionData highestHealthHandCard = ownerHand.Where(card => card != null)
    .OrderByDescending(card => card.Health).FirstOrDefault();
int highestHandAttack = ownerHand.Where(card => card != null).Max(card => card.Attack);
```

Apply each rule only to its exact target. Preserve `MaxHealth` when Tinyfin adds health and keep zero/empty inputs as no-ops.

- [ ] **Step 3: Route owner hands through combat simulation**

Change the two calls to:

```csharp
ctx.AttackerTrinkets.ApplyStartOfCombat(atkUnits, ctx.AttackerHand);
ctx.DefenderTrinkets.ApplyStartOfCombat(defUnits, ctx.DefenderHand);
```

- [ ] **Step 4: Run the focused harness and verify GREEN**

Run the Task 1 PowerShell command. Expected: `PASS active trinket effects are local, exact, and simulation-safe` and exit code 0.

### Task 3: Document the expanded local boundary

**Files:**
- Modify: `README.md`
- Modify: `DATA_SOURCES.md`
- Modify: `PRIVACY.md`
- Modify: `NOTICE`
- Modify: `docs/maintainer/ARCHITECTURE.md`
- Modify: `docs/maintainer/DEPENDENCIES.md`

**Interfaces:**
- Consumes: the implemented rule registry and HearthDb snapshot version.
- Produces: public and maintainer documentation that distinguishes quote recommendations from equipped effects and lists the eight exact local rules.

- [ ] **Step 1: Update user-facing boundaries**

State that quote recommendations remain hidden, equipped effects still affect later decisions, and no Firestone trinket statistics or cache are used.

- [ ] **Step 2: Update source, privacy, and notice records**

Record HearthDb as the factual source for exact CardIds/text, the local synthetic-test policy, and the absence of new network or user-cache access.

- [ ] **Step 3: Update architecture and dependency records**

Document `ApplyStartOfCombat(ownerBoard, ownerHand)`, owner isolation, ruleset version, and all eight covered CardIds.

### Task 4: Verify, audit, commit, and push

**Files:**
- Verify all modified files; do not change `.github`.

**Interfaces:**
- Consumes: completed code, tests, and docs.
- Produces: one reviewed commit pushed to `origin/codex/add-bilingual-readme`.

- [ ] **Step 1: Run the full verification matrix**

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.53.5\build-reference'
npm test
powershell -ExecutionPolicy Bypass -File tests/test_active_trinket_effects.ps1
powershell -ExecutionPolicy Bypass -File tools/build/build_release.ps1
powershell -ExecutionPolicy Bypass -File tools/build/validate_repository.ps1
git diff --check
```

Expected: every command exits 0.

- [ ] **Step 2: Audit release contents and sensitive data**

Use the release whitelist generated by the build tools, inspect changed files for Firestone trinket endpoints, credentials, personal absolute paths, replay data, and files over the repository size limit. Confirm `.github` has no diff.

- [ ] **Step 3: Review the final diff**

Check `git status --short --branch`, `git diff --stat`, `git diff`, and `git diff --check`. Any unrelated user changes must remain untouched.

- [ ] **Step 4: Commit and push the authorized branch**

```powershell
git add README.md DATA_SOURCES.md PRIVACY.md NOTICE docs/maintainer/ARCHITECTURE.md docs/maintainer/DEPENDENCIES.md docs/superpowers/specs/2026-07-22-active-trinket-rule-expansion-design.md docs/superpowers/plans/2026-07-22-active-trinket-rule-expansion.md src/BobCoach/Core/TrinketEffectRegistry.cs src/BobCoach/Core/ActiveTrinketContext.cs src/BobCoach/Core/CombatSimulator.cs tests/ActiveTrinketEffectsHarness.cs
git commit -m "feat: expand local equipped trinket rules"
git push origin codex/add-bilingual-readme
```

Expected: commit succeeds and remote branch advances; no PR or Release is created.

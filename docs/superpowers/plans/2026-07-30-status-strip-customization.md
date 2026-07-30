# BobCoach Status Strip Customization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted 12-28 DIP status-strip font sizing and temporarily unlocked, bounded dragging through a non-modal HDT plugin settings window without changing BobCoach decision logic or other overlay behavior.

**Architecture:** Keep configuration and normalization in `LayoutConfig`, put deterministic font scaling and bounds math in an HDT-independent `StatusStripLayoutPolicy`, and let `OverlayRenderer` own one stable status root plus runtime-only drag state. `BobCoachPlugin` owns a single settings window and closes it during unload; both the window and F10 calibration mutate the same `LayoutConfig` instance.

**Tech Stack:** C# 7.3, .NET Framework 4.7.2, WPF, HDT 1.54.0.7376 plugin API, PowerShell behavior tests, Node.js source-contract tests.

## Global Constraints

- Preserve `OnButtonPress()` as the Power.log configuration assistant.
- Keep all gameplay and privacy behavior offline and unchanged.
- Store `StatusOffsetX`, `StatusOffsetY`, and `StatusFontSize`; never persist unlocked state.
- Clamp font size to `12-28`, default to `16`, and reset position to `(8, 105)`.
- Scale status typography, line height, padding, and maximum width by `StatusFontSize / 13`; keep colors, weights, and visibility rules unchanged.
- Keep the entire measured panel inside the current `OverlayCanvas` bounds.
- Preserve all valid v9 and v10 calibration fields during migration to the new schema.
- Release mouse capture and remove handlers when the panel is cleared, combat starts, the settings window closes, or the plugin unloads.
- Do not change CI/CD configuration, release identity, shop recognition, or decision-engine behavior.

---

### Task 1: Pure Status Layout Policy And Configuration Migration

**Files:**
- Create: `src/BobCoach/Core/StatusStripLayoutPolicy.cs`
- Modify: `src/BobCoach/Core/LayoutConfig.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Create: `tests/StatusStripLayoutPolicyBehavior.cs`
- Create: `tests/test_status_strip_layout_policy.ps1`
- Modify: `tests/run_behavior_tests.ps1`
- Modify: `tests/test_layout_config_migration.ps1`

**Interfaces:**
- Produces: `StatusStripLayoutPolicy.NormalizeFontSize(double)`, `GetScale(double)`, and `ClampPosition(double, double, double, double, double, double)`.
- Produces: `LayoutConfig.StatusOffsetX`, `StatusOffsetY`, `StatusFontSize`, plus schema migration that retains existing fields.

- [ ] **Step 1: Write failing policy tests**

Add tests that assert font values below 12, above 28, `NaN`, and infinities normalize correctly; verify scale `16 / 13`; verify negative, oversized, and panel-larger-than-canvas coordinates clamp correctly.

- [ ] **Step 2: Run policy tests and confirm RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_status_strip_layout_policy.ps1
```

Expected: compilation fails because `StatusStripLayoutPolicy` does not exist.

- [ ] **Step 3: Write failing migration tests**

Extend migration coverage to assert defaults `(8, 105, 16)`, preservation of representative v9/v10 shop, board, hand, panel, and tier fields, invalid font normalization, status offset rebasing, and no font rebasing.

- [ ] **Step 4: Run migration tests and confirm RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_layout_config_migration.ps1
```

Expected: assertions fail because schema v11 and status properties are absent.

- [ ] **Step 5: Implement minimal policy and migration**

Add an immutable point/result type if required by the existing test harness. Bump the config schema to v11, normalize v11 values, migrate v10 without resetting fields, apply the existing v9 shop-offset migration before adding status defaults, and include status X/Y in `RebasePixelValues` while leaving font size unchanged.

- [ ] **Step 6: Run focused tests and confirm GREEN**

Run both PowerShell scripts and confirm zero failures.

### Task 2: Shared Position And F10 Calibration Behavior

**Files:**
- Modify: `src/BobCoach/Core/GameLayoutCalculator.cs`
- Modify: `src/BobCoach/Core/CalibrationOverlay.cs`
- Modify: `tests/test_layout_resolution_matrix.ps1`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**
- Consumes: `LayoutConfig.StatusOffsetX/Y`.
- Produces: `GameLayoutCalculator.GetStatusStripPosition()` from shared config.
- Produces: F10 `Status` movement and position-only reset.

- [ ] **Step 1: Add failing coordinate and F10 tests**

Assert status coordinates scale through the existing calibration coordinate system. Add source-contract assertions that `ApplyOffset("Status", dx, dy)` changes only `StatusOffsetX/Y`, and `ResetCurrentZone()` restores only position `(8, 105)` without changing `StatusFontSize`.

- [ ] **Step 2: Run focused tests and confirm RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test_layout_resolution_matrix.ps1
node .\tests\test_ui_lifecycle.js
```

Expected: status-coordinate and F10 assertions fail against the current hard-coded/empty implementation.

- [ ] **Step 3: Implement shared status coordinates**

Change `GetStatusStripPosition()` to use `_config.ScaleX(StatusOffsetX, ClientWidth)` and `_config.ScaleY(StatusOffsetY, ClientHeight)`. Fill the existing `Status` branches in calibration offset and reset logic, preserving current save behavior.

- [ ] **Step 4: Run focused tests and confirm GREEN**

Re-run both commands and confirm zero failures.

### Task 3: Stable Status Root, Scaling, Dragging, And Cleanup

**Files:**
- Modify: `src/BobCoach/OverlayRenderer.cs`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**
- Produces: `SetStatusStripUnlocked(bool)`, `SetStatusStripFontSize(double)`, `ResetStatusStripSettings()`, and `DisposeStatusStripInteraction()` (exact visibility may remain internal).
- Consumes: the shared `LayoutConfig` through `GameLayoutCalculator.Config`.

- [ ] **Step 1: Add failing renderer contract tests**

Assert `ShowStatusStrip` reuses one tagged root rather than clearing/recreating it, child text remains non-hit-testable, locked root is click-through, unlocked root captures mouse, drag loss/release ends interaction, and cleanup detaches handlers/releases capture. Assert typography, line height, padding, and widths derive from `StatusFontSize / 13`.

- [ ] **Step 2: Run renderer contracts and confirm RED**

Run:

```powershell
node .\tests\test_ui_lifecycle.js
```

Expected: stable-root, scaling, drag, and cleanup assertions fail.

- [ ] **Step 3: Implement the stable root and visual refresh**

Create one status `Border` root with an inner `StackPanel`; update children on each status refresh. Preserve existing colors, weights, line conditions, and tags. On clear/combat, terminate drag before removing the root; allow a later `ShowStatusStrip` to recreate and reattach it safely.

- [ ] **Step 4: Implement runtime locking and bounded dragging**

Only the root participates in hit testing while unlocked. Capture mouse on left down, update clamped position on move, convert canvas coordinates back to calibration coordinates, and save only after a valid drag completes. Re-clamp after measurement, font changes, and canvas-size changes; persist corrected visible coordinates when necessary.

- [ ] **Step 5: Run renderer contracts and confirm GREEN**

Run the Node test and confirm zero failures.

### Task 4: Non-Modal Settings Window And Plugin Lifecycle

**Files:**
- Create: `src/BobCoach/StatusStripSettingsWindow.cs`
- Modify: `src/BobCoach/BobCoachPlugin.cs`
- Modify: `src/BobCoach/BobCoach.csproj`
- Modify: `tests/test_ui_lifecycle.js`

**Interfaces:**
- Consumes: renderer methods from Task 3.
- Produces: one HDT `MenuItem` labelled `Bob教练设置`, a single non-modal window, integer slider `12-28`, unlock checkbox, and reset button.

- [ ] **Step 1: Add failing window and lifecycle contracts**

Assert `MenuItem` no longer returns null, `OnButtonPress()` still calls the Power.log assistant, duplicate menu clicks activate the existing window, closing the window locks the renderer, and `OnUnload()` closes the window and disposes renderer interaction.

- [ ] **Step 2: Run lifecycle contracts and confirm RED**

Run:

```powershell
node .\tests\test_ui_lifecycle.js
```

Expected: menu/window/lifecycle assertions fail because no settings window exists.

- [ ] **Step 3: Implement the settings window**

Build the small WPF window in code using `CheckBox`, `Slider`, current integer value text, and `Button`. Preview font changes live; persist on slider release. Reset both position and font immediately. Close safely when the renderer/plugin is unavailable.

- [ ] **Step 4: Wire plugin ownership and unload**

Cache the menu item and window, marshal window operations onto the WPF dispatcher, activate rather than duplicate, log configuration save failures without throwing into the UI thread, close during unload, and keep `OnButtonPress()` unchanged.

- [ ] **Step 5: Run lifecycle contracts and confirm GREEN**

Run the Node test and confirm zero failures.

### Task 5: Documentation And Complete Verification

**Files:**
- Modify only the existing user settings/troubleshooting document selected by `docs/README.md` if the new controls are not discoverable from HDT itself.
- Modify: `docs/superpowers/plans/2026-07-30-status-strip-customization.md` checkboxes as work completes.

- [ ] **Step 1: Run focused behavior suite**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run_behavior_tests.ps1
```

- [ ] **Step 2: Run full repository tests**

```powershell
npm test
```

- [ ] **Step 3: Run Release build against exact HDT 1.54.0.7376**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\build_release.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory "$env:TEMP\bobcoach-status-strip-build" -Force
```

- [ ] **Step 4: Run repository validation and whitespace checks**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\validate_repository.ps1
git diff --check
```

- [ ] **Step 5: Review requirements and remaining manual acceptance**

Re-read the approved design. Record automated evidence and leave HDT 1.54 in-game drag, click-through, DPI/resolution, combat hide/return, and restart persistence checks explicitly marked as manual if no disposable HDT environment is available.

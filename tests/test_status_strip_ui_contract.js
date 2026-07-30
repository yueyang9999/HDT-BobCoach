"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const read = rel => fs.readFileSync(path.join(root, rel), "utf8");

const plugin = read("src/BobCoach/BobCoachPlugin.cs");
const renderer = read("src/BobCoach/OverlayRenderer.cs");
const calibration = read("src/BobCoach/Core/CalibrationOverlay.cs");
const project = read("src/BobCoach/BobCoach.csproj");

const failures = [];
function check(name, condition) {
    if (!condition) failures.push(name);
}

const statusMethodStart = renderer.indexOf("public void ShowStatusStrip");
const statusMethodEnd = renderer.indexOf("\n        public void ", statusMethodStart + 1);
const statusMethod = renderer.slice(statusMethodStart, statusMethodEnd);
const clearNonPanelStart = renderer.indexOf("public void ClearNonPanel");
const clearNonPanelEnd = renderer.indexOf("\n        public void ", clearNonPanelStart + 1);
const clearNonPanelMethod = renderer.slice(clearNonPanelStart, clearNonPanelEnd);
const fadeStart = renderer.indexOf("public void FadeOutActiveElements");
const fadeEnd = renderer.indexOf("\n        public void ", fadeStart + 1);
const fadeMethod = renderer.slice(fadeStart, fadeEnd);
const refreshStart = renderer.indexOf("public void RefreshLayout");
const refreshEnd = renderer.indexOf("\n        public void ", refreshStart + 1);
const refreshMethod = renderer.slice(refreshStart, refreshEnd);
const mouseMoveStart = renderer.indexOf("private void StatusStripMouseMove");
const mouseMoveEnd = renderer.indexOf("\n        private void ", mouseMoveStart + 1);
const mouseMoveMethod = renderer.slice(mouseMoveStart, mouseMoveEnd);
const interactionStart = renderer.indexOf("private void ApplyStatusStripInteractionState");
const interactionEnd = renderer.indexOf("\n        private void ", interactionStart + 1);
const interactionMethod = renderer.slice(interactionStart, interactionEnd);
const ensureRootStart = renderer.indexOf("private void EnsureStatusStripRoot");
const ensureRootEnd = renderer.indexOf("\n        private void ", ensureRootStart + 1);
const ensureRootMethod = renderer.slice(ensureRootStart, ensureRootEnd);
const detachStart = renderer.indexOf("private void DetachStatusStrip");
const detachEnd = renderer.indexOf("\n        //", detachStart + 1);
const detachMethod = renderer.slice(detachStart, detachEnd);
const shopRefreshStart = plugin.indexOf("private void RequestShopUiRefresh");
const shopRefreshEnd = plugin.indexOf("\n        private ", shopRefreshStart + 1);
const shopRefreshMethod = plugin.slice(shopRefreshStart, shopRefreshEnd);
const calibrationRenderStart = calibration.indexOf("public void Render(Canvas canvas)");
const calibrationRenderEnd = calibration.indexOf("\n        public void Clear", calibrationRenderStart + 1);
const calibrationRenderMethod = calibration.slice(calibrationRenderStart, calibrationRenderEnd);
const calibrationStatusPreviewStart = calibration.indexOf("private void DrawStatusPreview");
const calibrationStatusPreviewEnd = calibration.indexOf("\n        private void ", calibrationStatusPreviewStart + 1);
const calibrationStatusPreviewMethod = calibration.slice(calibrationStatusPreviewStart, calibrationStatusPreviewEnd);

check("plugin menu exposes settings entry", !/MenuItem\s*\{\s*get\s*\{\s*return null;/.test(plugin));
check("settings window is held by the plugin", plugin.includes("StatusStripSettingsWindow"));
check("settings window is non-modal", plugin.includes(".Show()") && !plugin.includes(".ShowDialog()"));
check("plugin unload closes settings window", /OnUnload\([\s\S]*?CloseStatusStripSettings/.test(plugin));
check("Power.log assistant remains available", plugin.includes("Power.log configuration assistant requested"));
check("settings window is compiled", project.includes('Compile Include="StatusStripSettingsWindow.cs"'));

check("renderer owns a stable status root", renderer.includes("_statusStripRoot"));
check("status refresh does not clear its root", !statusMethod.includes('ClearTaggedElements("status_strip")'));
check("status root can be unlocked", renderer.includes("SetStatusStripUnlocked"));
check("status root handles mouse down", renderer.includes("MouseLeftButtonDown"));
check("status root handles mouse move", renderer.includes("MouseMove"));
check("status root handles mouse up", renderer.includes("MouseLeftButtonUp"));
check("status root handles lost capture", renderer.includes("LostMouseCapture"));
check("unlocked status root is registered with the HDT overlay",
    interactionMethod.includes("OverlayExtensions.SetIsOverlayHitTestVisible")
    && interactionMethod.includes("_statusStripUnlocked"));
check("status root is attached before HDT overlay hit registration",
    ensureRootMethod.indexOf("canvas.Children.Add(_statusStripRoot)") >= 0
    && ensureRootMethod.indexOf("canvas.Children.Add(_statusStripRoot)")
        < ensureRootMethod.indexOf("ApplyStatusStripInteractionState()"));
check("status root recreation preserves the runtime unlock state",
    ensureRootMethod.includes("DetachStatusStrip(false)"));
check("drag captures the mouse", renderer.includes("CaptureMouse()"));
check("drag starts only after mouse capture succeeds",
    /if\s*\(\s*!_statusStripRoot\.CaptureMouse\(\)\s*\)\s*return;[\s\S]*?_statusDragging\s*=\s*true;/.test(renderer));
check("drag release releases capture", renderer.includes("ReleaseMouseCapture()"));
check("status teardown exists", renderer.includes("DetachStatusStrip"));
check("status teardown unregisters the HDT overlay hit target before clearing the root",
    detachMethod.indexOf("OverlayExtensions.SetIsOverlayHitTestVisible(_statusStripRoot, false)") >= 0
    && detachMethod.indexOf("OverlayExtensions.SetIsOverlayHitTestVisible(_statusStripRoot, false)")
        < detachMethod.indexOf("_statusStripRoot = null"));
check("status teardown is used by full clear", /public void Clear\([\s\S]*?DetachStatusStrip/.test(renderer));
check("routine redraw preserves the status root",
    !clearNonPanelMethod.includes("DetachStatusStrip")
    && clearNonPanelMethod.includes('tag == "status_strip"'));
check("routine fade preserves the status root",
    !fadeMethod.includes("DetachStatusStrip")
    && fadeMethod.includes("ReferenceEquals(el, _statusStripRoot)"));
check("shop refresh preserves the status root and an active drag",
    shopRefreshMethod.includes("_renderer.ClearNonPanel()")
    && !shopRefreshMethod.includes("_renderer.Clear()"));
check("drag updates in-memory position before persistence",
    /SetStatusStripCanvasPosition\([\s\S]*?true\);/.test(mouseMoveMethod));
check("lock state does not change measured border thickness",
    /ApplyStatusStripInteractionState[\s\S]*?BorderThickness = new Thickness\(1\)/.test(renderer));
check("font-scaled width is not resolution-scaled twice",
    statusMethod.includes("Math.Min(metrics.NarrowMaxWidth, canvasWidth)")
    && statusMethod.includes("Math.Min(metrics.WideMaxWidth, canvasWidth)"));
check("layout refresh keeps the existing calculator",
    !refreshMethod.includes("new Engine.GameLayoutCalculator"));
check("font changes refresh status layout", renderer.includes("SetStatusStripFontSize"));
check("default reset updates position and font", renderer.includes("ResetStatusStripCustomization"));

check("calibration can share a LayoutConfig", calibration.includes("CalibrationOverlay(LayoutConfig config)"));
check("F10 status arrows update X", calibration.includes("_config.StatusOffsetX += dx"));
check("F10 status arrows update Y", calibration.includes("_config.StatusOffsetY += dy"));
check("F10 status reset restores X only", calibration.includes("_config.StatusOffsetX = def.StatusOffsetX"));
check("F10 status reset restores Y only", calibration.includes("_config.StatusOffsetY = def.StatusOffsetY"));
check("F10 renders a visible status preview", calibrationRenderMethod.includes("DrawStatusPreview"));
check("F10 status preview uses the shared position, font metrics, and bounds policy",
    calibration.includes("GetStatusStripPosition()")
    && calibration.includes("StatusStripLayoutPolicy.CreateMetrics")
    && calibration.includes("StatusStripLayoutPolicy.ClampPosition"));
check("F10 status preview does not mutate shared position during rendering",
    !calibrationStatusPreviewMethod.includes("_config.StatusOffset")
    && !calibrationStatusPreviewMethod.includes("UpdateStatusConfigFromCanvasPosition"));

if (failures.length) {
    for (const failure of failures) console.error("FAIL " + failure);
    process.exit(1);
}

console.log("PASS status strip UI contract");

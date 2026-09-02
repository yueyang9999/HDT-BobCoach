"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const pluginPath = path.join(root, "src", "BobCoach", "BobCoachPlugin.cs");
const rendererPath = path.join(root, "src", "BobCoach", "OverlayRenderer.cs");
const policyPath = path.join(root, "src", "BobCoach", "Core", "ShopLayoutSlotPolicy.cs");

function fail(message) {
    console.error(`FAIL ${message}`);
    process.exit(1);
}

if (!fs.existsSync(policyPath))
    fail("missing shared shop layout slot policy");

const plugin = fs.readFileSync(pluginPath, "utf8");
const renderer = fs.readFileSync(rendererPath, "utf8");

if (!plugin.includes("_shopLayoutSlotPolicy.Resolve("))
    fail("initial rendering must resolve layout through the shared stateful policy");
if (!plugin.includes("_renderer.SyncShopTagPositions(_cachedState.ShopMinions, _shopLayoutDecision)"))
    fail("frame synchronization must receive the same saved layout decision");
if (plugin.includes("else if (!_shopLayoutSlotPolicy.AwaitingCompleteShop)"))
    fail("a purchase must end the awaiting-complete window even during a partial snapshot");
if (!plugin.includes("_shopLayoutSlotPolicy.ObservePurchase();"))
    fail("purchase detection must confirm a real purchase through the shared policy");
if (!plugin.includes("_shopLayoutSlotPolicy.MarkRefreshDetected()"))
    fail("only a detected entity refresh may advance the remembered shop tier");
if (!plugin.includes("_shopLayoutSlotPolicy.ObserveShopTier(")
    || !plugin.includes("state.ShopMinions.Count")
    || !plugin.includes("state.ShopSlotCount"))
    fail("render evaluation must consume a pending refresh tier only after the full shop arrives");
if (!plugin.includes("GetShopSlotCountForTier(layoutTier)"))
    fail("initial shop labels must use the last real refresh tier, not the newly upgraded tier");
if (plugin.includes("_lastShopContentHash") || plugin.includes("_lastShopRefreshTier"))
    fail("generic shop-content changes must not update the remembered refresh tier");
if (!plugin.includes('"|L" + layoutDecision.LayoutCount'))
    fail("layout width changes must participate in render de-duplication");
if (!plugin.includes("slotCountForLayout = layoutDecision.LayoutCount"))
    fail("initial shop labels must use the policy layout count");
if (!plugin.includes("layoutDecision.UseDenseSlots"))
    fail("initial shop labels must honor the policy slot mapping mode");

const syncStart = renderer.indexOf("SyncShopTagPositions(");
const syncBlock = syncStart >= 0 ? renderer.slice(syncStart, syncStart + 2600) : "";
if (!syncBlock.includes("Engine.ShopLayoutDecision layoutDecision"))
    fail("frame synchronization must accept the resolved layout decision");
if (!syncBlock.includes("int layoutCount = layoutDecision.LayoutCount"))
    fail("frame synchronization must use the policy layout count");
if (!syncBlock.includes("layoutDecision.UseDenseSlots"))
    fail("frame synchronization must honor the policy slot mapping mode");
if (syncBlock.includes("Math.Min(7, liveShopMinions.Count)"))
    fail("frame synchronization must not independently recalculate a dense actual-count layout");

console.log("PASS shared shop layout decision contract");

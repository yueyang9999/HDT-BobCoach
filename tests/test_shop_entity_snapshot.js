"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const pluginPath = path.join(root, "src", "BobCoach", "BobCoachPlugin.cs");
const policyPath = path.join(root, "src", "BobCoach", "Core", "ShopEntitySnapshotPolicy.cs");

function fail(message) {
    console.error(`FAIL ${message}`);
    process.exit(1);
}

if (!fs.existsSync(policyPath))
    fail("missing one shared shop snapshot policy for change detection and state hashing");

const policy = fs.readFileSync(policyPath, "utf8");
if (!policy.includes("CardType.BATTLEGROUND_SPELL"))
    fail("shop snapshot policy must recognize CARDTYPE=BATTLEGROUND_SPELL");
if (!policy.includes("IsChangeDetectionCandidate") || !policy.includes("IsStateHashCandidate"))
    fail("shop snapshot policy must expose explicit change-detection and state-hash decisions");

const plugin = fs.readFileSync(pluginPath, "utf8");
const changeStart = plugin.indexOf("var shopIds = new List<int>();");
const changeBlock = changeStart >= 0 ? plugin.slice(changeStart, changeStart + 1800) : "";
if (!changeBlock.includes("ShopEntitySnapshotPolicy.IsChangeDetectionCandidate"))
    fail("shop change detection must use the shared policy so a late tavern spell triggers re-evaluation");

const hashStart = plugin.indexOf("private string ComputeStateHash()");
const hashBlock = hashStart >= 0 ? plugin.slice(hashStart, hashStart + 3400) : "";
if (!hashBlock.includes("ShopEntitySnapshotPolicy.IsStateHashCandidate"))
    fail("state hash must use the shared policy so zero-health tavern spells change the snapshot");

console.log("PASS late tavern spell shop snapshot contract");

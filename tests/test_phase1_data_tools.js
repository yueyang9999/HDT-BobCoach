"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const tools = [
    path.join(root, "tools", "build", "generate_card_object_catalog.js"),
    path.join(root, "tools", "build", "generate_hero_skill_registry.js"),
    path.join(root, "tools", "build", "audit_unknown_card_ids.js"),
];

for (const file of tools) {
    assert.ok(fs.existsSync(file), `missing phase1 data tool: ${file}`);
}

const audit = spawnSync(process.execPath, [tools[2]], { encoding: "utf8" });
assert.strictEqual(audit.status, 0, `unknown card audit failed: ${audit.stdout} ${audit.stderr}`);
assert.ok(audit.stdout.includes("PASS unknown card ID fallback audit"),
    "unknown card audit must report PASS");

const missingRoot = spawnSync(process.execPath, [tools[0]], { encoding: "utf8" });
assert.notStrictEqual(missingRoot.status, 0, "generator must require --data-root");
assert.ok(missingRoot.stderr.includes("--data-root is required"),
    "generator must report missing --data-root");

console.log("PASS phase1 data tools contract");

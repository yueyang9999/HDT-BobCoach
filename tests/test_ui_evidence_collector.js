"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const collector = path.join(root, "tools", "release", "collect_real_ui_evidence.ps1");
assert.ok(fs.existsSync(collector), "missing real UI evidence collector");
const source = fs.readFileSync(collector, "utf8");
assert.ok(source.includes("--replay-only"), "collector must replay tail with --replay-only");
assert.ok(source.includes("LATEST_POWER_LOG"), "collector must identify latest Power.log");
assert.ok(source.includes("REAL_UI_EVIDENCE_DIR"), "collector must expose evidence directory");
assert.ok(source.includes("app-1.54.1"), "collector must check local HDT app-1.54.1 plugin dir");

console.log("PASS real UI evidence collector contract");

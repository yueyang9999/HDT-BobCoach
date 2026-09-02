"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const tool = path.join(root, "tools", "evaluate", "replay_sequence_compare.js");
const temp = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-replay-compare-"));

try {
    const globalFile = path.join(temp, "global.jsonl");
    const firestoneFile = path.join(temp, "firestone.json");
    const outputDir = path.join(temp, "out");
    fs.writeFileSync(globalFile, [
        JSON.stringify({ e: "meta", hero: "HERO" }),
        JSON.stringify({ e: "action", kind: "buy", cardId: "BG25_001", turn: 1 }),
        JSON.stringify({ e: "powerAction", kind: "buy", cardId: "BG25_001", turn: 1 }),
        JSON.stringify({ e: "action", kind: "play", cardId: "BG25_001", turn: 1 }),
        JSON.stringify({ e: "action", kind: "level", cardId: "", turn: 2 }),
        JSON.stringify({ e: "choice", kind: "discover", choiceId: 1, sourceCardId: "SRC", turn: 1, candidates: ["BG25_003", "BG25_004"] }),
        JSON.stringify({ e: "choiceCompleted", kind: "discover", choiceId: 1, selectedCardId: "BG25_003", turn: 1 }),
        JSON.stringify({ e: "end", finalRank: 3, turnCount: 2 }),
    ].join("\n") + "\n", "utf8");
    fs.writeFileSync(firestoneFile, JSON.stringify({
        hero: "HERO",
        finalRank: 3,
        turnCount: 2,
        actions: [
            { kind: "buy", cardId: "BG25_001", turn: 1 },
            { kind: "play", cardId: "BG25_001", turn: 1 },
            { kind: "buy", cardId: "BG25_MISSING", turn: 1 },
            { kind: "level", cardId: "", turn: 2 },
        ],
        choices: [{
            kind: "discover",
            key: "discover|202|9",
            sourceCardId: "SRC",
            turn: 1,
            candidates: ["BG25_003", "BG25_004"],
            selectedCardId: "BG25_003",
        }],
    }), "utf8");

    const run = spawnSync(process.execPath, [
        tool,
        "--global", globalFile,
        "--firestone", firestoneFile,
        "--output-dir", outputDir,
        "--label", "test",
    ], { encoding: "utf8", windowsHide: true });
    assert.strictEqual(run.status, 0, run.stderr || "comparison tool failed");
    const report = JSON.parse(run.stdout);
    assert.ok(report.strictComparison.missingFromGlobal.some(item =>
        item.kind === "buy" && item.cardId === "BG25_MISSING"),
        "missing global buy action is reported");
    assert.strictEqual(report.choiceComparison.mismatchCount, 0, "matching choice is aligned");
    assert.strictEqual(report.coverage.global.powerActions, 1, "powerAction coverage is reported");
    assert.ok(fs.existsSync(path.join(outputDir, "compare_test.json")), "report output is written");
    console.log("PASS replay sequence compare");
} finally {
    fs.rmSync(temp, { recursive: true, force: true });
}

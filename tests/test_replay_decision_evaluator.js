"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { execFileSync, spawnSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const tool = path.join(root, "tools", "evaluate", "replay_decision_evaluator.js");

function runTool(input, output) {
    const args = ["--input", input];
    if (output) args.push("--output", output);
    return JSON.parse(execFileSync(process.execPath, [tool, ...args], {
        encoding: "utf8",
        windowsHide: true,
    }));
}

const temp = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-replay-eval-"));
try {
    const input = path.join(temp, "replay.jsonl");
    const output = path.join(temp, "out", "report.json");
    fs.writeFileSync(input, [
        JSON.stringify({ gameId: "g1", turn: 1, recommended: "BuyMinion|0|A|tavern_shop", actual: "BuyMinion|0|A|tavern_shop", finalPlacement: 1 }),
        JSON.stringify({ gameId: "g1", turn: 2, recommended: "BuyMinion|1|B|tavern_shop", actual: "Refresh|-1||", finalPlacement: 6 }),
        JSON.stringify({ gameId: "g2", turn: 1, recommended: "SellMinion|0|C|", actual: "SellMinion|0|C|", finalPlacement: 4 }),
    ].join("\n") + "\n", "utf8");

    const result = runTool(input);
    execFileSync(process.execPath,
        [tool, "--input", input, "--output", output],
        { encoding: "utf8", windowsHide: true });
    assert.strictEqual(result.total, 3, "total decision count");
    assert.strictEqual(result.matches, 2, "match count");
    assert.strictEqual(result.mismatchCount, 1, "mismatch count");
    assert.strictEqual(result.highImpactMismatches, 1, "high-impact mismatch count");
    assert.ok(Math.abs(result.matchRate - 2 / 3) < 1e-9, "match rate");
    assert.ok(result.typeStats.some(stat => stat.type === "BuyMinion" && stat.total === 1),
        "type stats include BuyMinion");
    assert.ok(fs.existsSync(output), "report output file exists");

    const written = JSON.parse(fs.readFileSync(output, "utf8"));
    assert.strictEqual(written.total, 3, "written report total");
    assert.strictEqual(written.mismatches[0].gameId, "g1", "written mismatch game id");

    const missing = spawnSync(process.execPath,
        [tool, "--input", path.join(temp, "missing.json")],
        { encoding: "utf8", windowsHide: true });
    assert.notStrictEqual(missing.status, 0, "missing input fails closed");

    console.log("PASS replay decision evaluator");
} finally {
    fs.rmSync(temp, { recursive: true, force: true });
}

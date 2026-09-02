"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const analyzer = path.join(root, "tools", "evaluate", "global_replay_analyzer.js");
const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-global-analyzer-"));

try {
    const lines = [
        "{\"v\":1,\"g\":\"g1\",\"seq\":1,\"e\":\"meta\",\"hero\":\"H\"}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":2,\"e\":\"state\",\"turn\":1}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":3,\"e\":\"action\",\"kind\":\"buy\",\"cardId\":\"BG23_000\"}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":4,\"e\":\"action\",\"kind\":\"sell\",\"cardId\":\"BG23_000\"}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":5,\"e\":\"powerAction\",\"kind\":\"buy\",\"cardId\":\"BG23_000\",\"turn\":1}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":6,\"e\":\"combat\",\"kind\":\"attack\",\"attacker\":1,\"defender\":2}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":7,\"e\":\"choice\",\"kind\":\"discover\"}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":8,\"e\":\"recommend\",\"turn\":1}",
        "{\"v\":1,\"g\":\"g1\",\"seq\":9,\"e\":\"end\",\"finalRank\":1}",
    ];
    const file = path.join(dir, "global_test.jsonl");
    fs.writeFileSync(file, lines.join("\n") + "\n", "utf8");
    const run = spawnSync(process.execPath, [analyzer, "--file", file], { encoding: "utf8" });
    assert.strictEqual(run.status, 0, run.stderr || "analyzer failed");
    const parsed = JSON.parse(run.stdout);
    assert.strictEqual(parsed.totals.events, 9, "event total");
    assert.strictEqual(parsed.totals.states, 1, "state total");
    assert.strictEqual(parsed.totals.recommendations, 1, "recommend total");
    assert.strictEqual(parsed.totals.actionsByKind.buy, 1, "buy total");
    assert.strictEqual(parsed.totals.actionsByKind.sell, 1, "sell total");
    assert.strictEqual(parsed.totals.powerActionsByKind.buy, 1, "power buy total");
    assert.strictEqual(parsed.totals.combatsByKind.attack, 1, "combat attack total");
    assert.strictEqual(parsed.totals.choicesByKind.discover, 1, "discover total");

    const dynamicFile = path.join(dir, "global_dynamic_plan_test.jsonl");
    fs.writeFileSync(dynamicFile, [
        JSON.stringify({ v: 1, g: "g2", seq: 1, e: "state", turn: 1, gold: 0, liveGold: 0, freeRefreshCount: 0 }),
        JSON.stringify({
            v: 1,
            g: "g2",
            seq: 2,
            e: "recommend",
            turn: 1,
            bestAction: { type: "Refresh", target: -1 },
            actionScores: [{ type: "Refresh", target: -1, cost: 1, recommended: true }],
            mainlinePlan: {
                mode: "Pivot",
                pivotScore: -0.3,
                shouldHoldForNextTurn: true,
                shouldBuyCoreFirst: false,
                hasTripleInShop: false,
                alternativeCoreCardIds: ["BG36_764"],
                strategyObjective: "SearchCore",
                strategyPlanHash: "abc123",
                strategyPlanSteps: ["BuyMinion:0:busker"],
                memoryObservedCoreCardIds: ["core-a"],
                memoryObservedPairCardIds: [],
            },
        }),
        JSON.stringify({ v: 1, g: "g2", seq: 3, e: "state", turn: 2, gold: 5, liveGold: 5, freeRefreshCount: 0 }),
        JSON.stringify({
            v: 1,
            g: "g2",
            seq: 4,
            e: "recommend",
            turn: 2,
            bestAction: { type: "BuyMinion", target: 0 },
            actionScores: [{ type: "BuyMinion", target: 0, cost: 3, recommended: true }],
            mainlinePlan: {
                mode: "Commit",
                pivotScore: -Infinity,
                shouldHoldForNextTurn: false,
                shouldBuyCoreFirst: true,
                hasTripleInShop: false,
                alternativeCoreCardIds: [],
                strategyObjective: "",
                strategyPlanHash: "",
                strategyPlanSteps: [],
                memoryObservedCoreCardIds: [],
                memoryObservedPairCardIds: [],
            },
        }),
        JSON.stringify({ v: 1, g: "g2", seq: 5, e: "state", turn: 2, gold: 0, liveGold: 0, freeRefreshCount: 0 }),
    ].join("\n") + "\n", "utf8");
    const dynamicRun = spawnSync(process.execPath,
        [analyzer, "--file", dynamicFile], { encoding: "utf8" });
    assert.strictEqual(dynamicRun.status, 0, dynamicRun.stderr || "dynamic analyzer failed");
    const dynamic = JSON.parse(dynamicRun.stdout);
    assert.strictEqual(dynamic.games[0].dynamicPlans.length, 2, "dynamic plan total");
    assert.strictEqual(dynamic.games[0].anomalies.length, 5, "dynamic anomaly total");
    const firstPlan = dynamic.games[0].dynamicPlans[0];
    assert.strictEqual(firstPlan.strategyObjective, "SearchCore", "strategy objective");
    assert.strictEqual(firstPlan.strategyPlanHash, "abc123", "strategy plan hash");
    assert.deepStrictEqual(firstPlan.strategyPlanSteps, ["BuyMinion:0:busker"], "strategy steps");
    assert.deepStrictEqual(firstPlan.memoryObservedCoreCardIds, ["core-a"], "observed core cards");
    assert.deepStrictEqual(firstPlan.memoryObservedPairCardIds, [], "observed pair cards");
    for (const code of [
        "zero-gold-paid-action",
        "economy-mode-overridden",
        "negative-score-pivot-or-hedge",
        "generic-core-evidence-in-pivot",
        "pivot-with-insufficient-alternative-cores",
    ]) {
        assert.ok(firstPlan.anomalies.includes(code), "missing dynamic anomaly: " + code);
    }
    assert.deepStrictEqual(dynamic.games[0].dynamicPlans[1].anomalies, [], "valid plan anomaly list");
    assert.strictEqual(dynamic.totals.anomaliesByCode["zero-gold-paid-action"], 1, "zero-gold anomaly total");
    console.log("PASS global replay analyzer contract");
} finally {
    fs.rmSync(dir, { recursive: true, force: true });
}

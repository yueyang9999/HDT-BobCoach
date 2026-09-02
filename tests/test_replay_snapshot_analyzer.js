"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { execFileSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const tool = path.join(root, "tools", "evaluate", "replay_snapshot_analyzer.js");

function runTool(dir, output, decisions, cards) {
    const args = ["--replay-dir", dir, "--output", output];
    if (decisions) args.push("--decisions", decisions);
    if (cards) args.push("--cards", cards);
    execFileSync(process.execPath, [tool, ...args], {
        encoding: "utf8",
        windowsHide: true,
    });
    return JSON.parse(fs.readFileSync(output, "utf8"));
}

const temp = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-snapshot-analyzer-"));
try {
    const replayDir = path.join(temp, "replays");
    fs.mkdirSync(replayDir, { recursive: true });
    const shopMinion = {
        id: "BG25_001",
        name: "复活的骑兵",
        atk: 2,
        hp: 1,
        tier: 1,
        pos: 0,
        cost: -1,
        spell: false,
    };
    const spell = {
        id: "BG33_101",
        name: "新生幼苗",
        atk: 0,
        hp: 0,
        tier: 1,
        pos: 0,
        cost: 4,
        spell: true,
    };
    const boardCard = {
        id: "BG25_001",
        name: "复活的骑兵",
        atk: 2,
        hp: 1,
        tier: 1,
        pos: 0,
        cost: -1,
        spell: false,
    };
    const snapshot = {
        hero: "测试英雄",
        heroName: "测试英雄",
        finalRank: 2,
        turns: [
            {
                turn: 1,
                gold: 0,
                liveGold: 0,
                freeRefreshCount: 0,
                tier: 1,
                health: 30,
                board: [],
                shop: [],
                hand: [],
                algoRec: "Refresh",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "Refresh", target: -1 },
                timeline: ["phase:recruit"],
            },
            {
                turn: 1,
                gold: 4,
                liveGold: 4,
                tavernUpgradeCost: 4,
                tier: 1,
                health: 30,
                board: [],
                shop: [],
                hand: [],
                algoRec: "Upgrade",
                levelSug: "LEVEL_UP",
                recommendedAction: { type: "Upgrade", target: -1 },
                timeline: ["phase:recruit"],
            },
            {
                turn: 1,
                gold: 3,
                liveGold: 3,
                tier: 1,
                health: 30,
                board: [],
                shop: [spell],
                hand: [],
                algoRec: "BuyMinion",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "BuyMinion", target: 0 },
                timeline: ["phase:recruit"],
            },
            {
                turn: 1,
                gold: 3,
                liveGold: 3,
                tier: 1,
                health: 30,
                board: [],
                shop: [spell],
                hand: [],
                algoRec: "BuySpell",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "BuySpell", target: 0 },
                timeline: ["phase:recruit"],
                timestamp: "t4",
            },
            {
                turn: 2,
                gold: 5,
                liveGold: 5,
                frozenShop: false,
                tier: 2,
                health: 30,
                board: [boardCard],
                shop: [shopMinion],
                hand: [],
                algoRec: "SellMinion",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "SellMinion", target: 0 },
                shadowRecommendedAction: { type: "SellMinion", target: 0 },
                shadowActionScores: [
                    { type: "SellMinion", target: 0, cardId: "BG25_001", cost: 0, score: 1.0, recommended: true },
                    { type: "Upgrade", target: -1, cardId: "", cost: 5, score: 0.8, recommended: false },
                ],
                timeline: ["phase:recruit"],
            },
            {
                turn: 2,
                gold: 3,
                liveGold: 3,
                tier: 2,
                health: 30,
                board: [boardCard],
                shop: [],
                hand: [spell],
                algoRec: "BuySpell",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "BuySpell", target: -1 },
                timeline: ["phase:recruit"],
            },
            {
                turn: 2,
                gold: 3,
                liveGold: 3,
                tier: 2,
                health: 30,
                board: [boardCard],
                shop: [],
                hand: [spell],
                algoRec: "BuySpell",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "BuySpell", target: -1 },
                timeline: ["spell:BG33_101(新生幼苗)"],
            },
            {
                turn: 3,
                gold: 0,
                liveGold: 0,
                tier: 2,
                health: 30,
                board: [],
                shop: [],
                hand: [],
                algoRec: "PickTrinket",
                levelSug: "NO_GOLD",
                recommendedAction: { type: "PickTrinket", target: 0 },
                shadowRecommendedAction: { type: "BuyMinion", target: 0 },
                shadowActionScores: [
                    { type: "BuyMinion", target: 0, cardId: "BG25_001", cost: 3, score: 1.0, recommended: true },
                ],
                timeline: ["phase:recruit"],
            },
        ],
    };
    fs.writeFileSync(path.join(replayDir, "snapshot_test.json"), JSON.stringify(snapshot), "utf8");
    const cardsPath = path.join(temp, "cards.json");
    fs.writeFileSync(cardsPath, JSON.stringify([
        { id: "BG33_101", name: "新生幼苗", type: "Battleground_spell", cost: 4 },
    ]), "utf8");

    const output = path.join(temp, "report.json");
    const decisionsOutput = path.join(temp, "decisions.jsonl");
    const report = runTool(replayDir, output, decisionsOutput, cardsPath);

    assert.strictEqual(report.games, 1, "analyzes one snapshot");
    const issues = report.summary.issues;
    const issue = name => issues.find(item => item.issue === name);
    assert.ok(issue("no-gold-refresh"), "zero gold refresh with no free refresh is reported");
    assert.ok(!issue("insufficient-gold-upgrade"), "discounted upgrade with enough gold is not reported");
    assert.ok(issue("insufficient-gold-buy"), "buy recommendation above live gold is reported");
    assert.ok(issue("buy-minion-targets-spell"), "BuyMinion targeting a spell is reported");

    const rows = fs.readFileSync(decisionsOutput, "utf8").trim();
    assert.ok(rows.length > 0, "decision rows are written");
    assert.ok(report.summary.mismatchCount >= 1, "spell use mismatch is counted");
    assert.ok(report.summary.topMismatches.some(m => m.pattern === "BuySpell -> UseSpell"),
        "missing use-spell recommendation appears in top mismatches");
    assert.ok(!issue("scored-multiple-recommended"),
        "shadow score serializer must not mark multiple recommendations");
    assert.ok(!issue("recommended-not-top-score"),
        "shadow recommended action must match the highest score");
    assert.ok(!issue("legacyRecNotInShadowScores"),
        "legacy trinket recommendation is not falsely reported as missing from shadow scores");

    console.log("PASS replay snapshot analyzer");
} finally {
    fs.rmSync(temp, { recursive: true, force: true });
}

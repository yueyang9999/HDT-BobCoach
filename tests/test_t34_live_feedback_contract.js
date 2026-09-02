"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
function read(rel) {
    return fs.readFileSync(path.join(root, rel), "utf8");
}

const plugin = read("src/BobCoach/BobCoachPlugin.cs");
const extractor = read("src/BobCoach/GameStateExtractor.cs");
const enumerator = read("src/BobCoach/Core/ActionEnumerator.cs");
const engine = read("src/BobCoach/Core/DecisionEngine.cs");
const visualizer = read("src/BobCoach/Core/DecisionVisualizer.cs");
const planner = read("src/BobCoach/Core/MainlineStrategyPlanner.cs");

assert.ok(
    plugin.includes("_discoverBatchFromLog = batch;") &&
        plugin.includes("_discoverPanelActive = true;"),
    "Faelin passive choice must enter the authoritative discover render pipeline",
);
assert.ok(
    plugin.includes("_discoverBatchFromLog = null;") &&
        plugin.includes("_discoverPanelActive = false;"),
    "Faelin passive completion must clear the discover pipeline",
);
assert.ok(
    extractor.includes("card.Cost = card.IsSpell ? 0 : card.Cost;"),
    "hand tavern spells must use their hand-play cost, not shop purchase cost",
);
assert.ok(
    !extractor.includes("isSpell && e.Attack <= 0 && e.Health <= 0"),
    "hand spells must not be marked dormant/locked because they have no attack/health",
);
assert.ok(
    enumerator.includes("int spellCost = 0;"),
    "hand tavern spells must be enumerated as zero-cost hand actions",
);
assert.ok(
    engine.includes('action.CardId != "BG36_880"') &&
        engine.includes("MinionData.TribeMatches(board.Tribe, \"恶魔\")"),
    "BG36_880 must be promoted as a use-spell action when a friendly demon exists",
);
assert.ok(
    engine.indexOf("var methodicalMadness = TryGetMethodicalMadnessUse(state, actions);") >
        engine.indexOf("var synergyTribeBuy = TryPickSynergyTribeBuy(") &&
        engine.includes("ShouldUseMethodicalMadnessNow(state, mainlinePlan, bestAction)") &&
        engine.includes("state.ShopMinions.Count(shop => shop != null && !shop.IsSpell) < 2"),
    "BG36_880 must not hard-override a critical buy or level-up; it is a safe end-of-turn fallback",
);
assert.ok(
    planner.includes("ResolveCompDir(state, compLock, catalog)"),
    "comp direction must use guide core evidence, not only raw tribe counts",
);
assert.ok(
    planner.includes("coreTribeSignals"),
    "mainline planner must separate core-tribe evidence from ordinary counts",
);
assert.ok(
    engine.includes("TryPickSynergyTribeBuy("),
    "visible decision engine must pick matching-tribe buy candidates around established cores",
);
assert.ok(
    engine.includes("bool boardFullForSynergyBuy = state.BoardMinions.Count >= state.MaxBoardSlots;") &&
        engine.includes("best = new GameAction { Type = ActionType.SellMinion"),
    "established tribe must produce a protected sell-to-buy path when the board is full",
);
assert.ok(
    visualizer.includes("bestHandCard.CardId == \"BG36_880\""),
    "visualizer must give BG36_880 an explicit friendly-demon target",
);

console.log("PASS T34 live feedback contract");

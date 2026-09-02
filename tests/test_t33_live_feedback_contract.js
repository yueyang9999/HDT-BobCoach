"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
function read(rel) {
    return fs.readFileSync(path.join(root, rel), "utf8");
}

const goldTracker = read("src/BobCoach/Core/GoldTracker.cs");
const extractor = read("src/BobCoach/GameStateExtractor.cs");
const plugin = read("src/BobCoach/BobCoachPlugin.cs");
const visualizer = read("src/BobCoach/Core/DecisionVisualizer.cs");
const simulator = read("src/BobCoach/Core/Simulator.cs");
const projector = read("src/BobCoach/Core/TurnProjector.cs");

assert.ok(
    /if \(!normalRecruitAvailable\)\s*return\s+_selfTrackedGold\s*;/.test(goldTracker),
    "non-recruit frames must preserve known gold or return -1, never force real zero",
);

const trackGoldStart = extractor.indexOf("private int TrackGold(");
const trackGoldEnd = extractor.indexOf("private int _goldDiagCount", trackGoldStart);
const trackGoldBody = extractor.slice(trackGoldStart, trackGoldEnd);
assert.ok(
    trackGoldBody.includes("state.MaxGold = Math.Max(state.MaxGold, hdtTag)"),
    "HDT RESOURCES max-gold tag must update state.MaxGold",
);
assert.ok(
    extractor.includes("bool HasRecruitShopEvidence(List<Entity> entities)") &&
        extractor.includes("bool isCombat = hdtCombat && !hasRecruitShopEvidence"),
    "extractor must override a stale HDT combat flag only when shop entities are present",
);

const heroSkillBlockStart = plugin.indexOf("if (heroSkill != null)");
const heroSkillBlockEnd = plugin.indexOf("// T1/T2", heroSkillBlockStart);
const heroSkillBlock = plugin.slice(heroSkillBlockStart, heroSkillBlockEnd);
assert.ok(
    /int\s+heroPowerCost\s*=\s*state\.HeroPowerCost\s*>=\s*0\s*\?\s*state\.HeroPowerCost\s*:\s*heroSkill\.Cost\s*;/.test(
        heroSkillBlock,
    ),
    "hero skill suggestion must prefer live state.HeroPowerCost over catalog cost",
);
assert.ok(
    /heroSkillDecision\s*=\s*HeroSkillDecisionService\.Decide\([\s\S]*?heroPowerCost,/.test(
        heroSkillBlock,
    ),
    "hero skill decision must consume the live cost",
);
assert.ok(
    visualizer.includes("PromoteBestShopAction("),
    "visualizer must promote the selected buy action as the primary shop marker",
);
assert.ok(
    visualizer.includes("bestAction.Type != ActionType.BuyMinion") &&
        visualizer.includes("bestAction.Type != ActionType.BuySpell"),
    "visualizer promotion must cover minion and spell buys",
);

assert.ok(
    /BG28_805/.test(simulator) &&
        simulator.includes("state.MaxGold = Math.Min(20, state.MaxGold + 1)"),
    "simulator must model the permanent max-gold effect of BG28_805",
);

assert.ok(
    /Math\.Max\(\s*0,\s*state\.MaxGold\s*-\s*Math\.Min\(\s*10,\s*2\s*\+\s*state\.Turn\s*\)/.test(
        projector,
    ),
    "TurnProjector must carry the observed max-gold cap bonus to the next turn",
);

console.log("PASS T33 live feedback contract");

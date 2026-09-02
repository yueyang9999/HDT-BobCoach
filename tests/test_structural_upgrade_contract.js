"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
function read(rel) {
    return fs.readFileSync(path.join(root, rel), "utf8");
}

const engine = read("src/BobCoach/Core/DecisionEngine.cs");
const simulator = read("src/BobCoach/Core/Simulator.cs");
const projector = read("src/BobCoach/Core/TurnProjector.cs");
const plugin = read("src/BobCoach/BobCoachPlugin.cs");
const actionSource = read("src/BobCoach/Core/ActionEnumerator.cs");
const shadowSerializer = read("src/BobCoach/Core/ShadowScoreSerializer.cs");
const visualizer = read("src/BobCoach/Core/DecisionVisualizer.cs");
const overlay = read("src/BobCoach/OverlayRenderer.cs");

assert.ok(engine.includes("GetRankedCandidates"), "unified candidate surface is present");
assert.ok(engine.includes("GetCrossTurnPlan"), "cross-turn planning is present");
assert.ok(projector.includes("public static GameState Advance"), "TurnProjector is present");
assert.ok(projector.includes("public sealed class CrossTurnPlan"), "CrossTurnPlan is present");
assert.ok(engine.includes("OpponentModel.IsLevelUpLethal"), "lethal upgrade safety is wired");
assert.ok(engine.includes("IsSelfReplacingEconomySell"), "self-replacing economy sell is wired");
assert.ok(visualizer.includes("IsVisualSelfReplacingEconomySell"),
    "visualizer mirrors economy sell protection");
assert.ok(simulator.includes("state.LiveGold = state.Gold"),
    "simulator syncs live gold after gold-changing actions");
assert.ok(simulator.includes("SampleTierOneReplacement"), "river skipper replacement is modeled");
assert.ok(plugin.includes("TryResolveRankedFallback"), "ranked fallback is wired");
assert.ok(plugin.includes("GetLiveGold(state)"), "plugin uses live gold");
assert.ok(plugin.includes("NormalizeHeroCardId(e.CardId)"),
    "final placement filters by normalized local hero");
assert.ok(engine.includes("GetCardObjectFact"), "card object database is wired");
assert.ok(engine.includes("_cardObjectDatabase.LoadFromConfiguredRoot()"),
    "card object database loads from configured season data");
assert.ok(engine.includes("ActionType.PlayMinion"), "play-minion candidate is wired");
assert.ok(engine.includes("ActionType.UseSpell"), "use-spell candidate is wired");
assert.ok(simulator.includes("SimulatePlayMinion"), "play-minion simulation is wired");
assert.ok(simulator.includes("SimulateUseSpell"), "use-spell simulation is wired");
assert.ok(plugin.includes("ShadowActionScoresJson"), "shadow score snapshot is wired");
assert.ok(plugin.includes("ShadowRecommendedActionJson"), "shadow recommendation snapshot is wired");
assert.ok(shadowSerializer.includes("seenKeys"), "shadow scores deduplicate identical action keys");
assert.ok(actionSource.includes("seenPowers"), "hero powers deduplicate repeated card ids");

const trinketPanelStart = overlay.indexOf("public void ShowTrinketHints");
const trinketPanelEnd = overlay.indexOf("public void ShowTrinketLoading", trinketPanelStart);
const trinketPanel = overlay.slice(trinketPanelStart, trinketPanelEnd);
assert.ok(!trinketPanel.includes('line += "  [" + h.Reason + "]"'),
    "trinket panel does not expose internal scoring attributes");

const statusHintBlock = plugin.slice(plugin.indexOf("bool shownPrimary"), plugin.indexOf("// ── 商店卡片高亮"));
assert.ok(!statusHintBlock.includes("成长"), "status hint does not dump growth labels");
assert.ok(!statusHintBlock.includes("资源生成"), "status hint does not dump resource labels");

console.log("PASS structural upgrade adversarial contract");

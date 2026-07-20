"use strict";

const fs = require("fs");
const path = require("path");

function buildRegistry(cards, build) {
    const records = (Array.isArray(cards) ? cards : [])
        .filter(card => card && card.battlegroundsTimewarpCard === 1)
        .filter(card => card.type === "MINION")
        .filter(card => Number.isInteger(card.battlegroundsPremiumDbfId))
        .filter(card => !Number.isInteger(card.battlegroundsNormalDbfId))
        .filter(card => card.techLevel === 3 || card.techLevel === 5)
        .map(card => ({
            cardId: card.id,
            kind: card.techLevel === 3 ? "lesser" : "greater",
            tier: card.techLevel,
            races: Array.isArray(card.races) ? [...card.races].sort() : [],
        }))
        .filter(card => typeof card.cardId === "string" && card.cardId.length > 0)
        .sort((left, right) => left.cardId.localeCompare(right.cardId, "en"));

    return {
        build,
        source: "HearthstoneJSON battlegroundsTimewarpCard=1; Blizzard Minor=T3 Major=T5",
        cards: records,
    };
}

function main() {
    const root = path.resolve(__dirname, "..");
    const sourcePath = path.join(root, "data", "_authority", "_hsjson_all_zh.json");
    const factsPath = path.join(root, "data", "anomaly_facts.json");
    const outputPath = path.join(root, "data", "timewarp_minion_registry.json");
    const cards = JSON.parse(fs.readFileSync(sourcePath, "utf8"));
    const facts = JSON.parse(fs.readFileSync(factsPath, "utf8"));
    const registry = buildRegistry(cards, facts.build);
    fs.writeFileSync(outputPath, JSON.stringify(registry, null, 2) + "\n", "utf8");
    const lesser = registry.cards.filter(card => card.kind === "lesser").length;
    const greater = registry.cards.filter(card => card.kind === "greater").length;
    console.log(`generated ${registry.cards.length} timewarp minions (lesser=${lesser}, greater=${greater})`);
}

if (require.main === module) main();

module.exports = { buildRegistry };

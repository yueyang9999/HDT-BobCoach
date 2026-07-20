"use strict";

const fs = require("fs");
const path = require("path");

const INITIAL_POOL_COPIES = Object.freeze({
    1: 16, 2: 15, 3: 13, 4: 11, 5: 9, 6: 7,
});

function buildRegistry(cards, build) {
    const allCards = Array.isArray(cards) ? cards : [];
    const byDbfId = new Map(allCards
        .filter(card => card && Number.isInteger(card.dbfId))
        .map(card => [card.dbfId, card]));
    const seen = new Set();
    const records = allCards
        .filter(card => card && card.isBattlegroundsBuddy === true)
        .filter(card => card.type === "MINION")
        .filter(card => Number.isInteger(card.battlegroundsPremiumDbfId))
        .filter(card => !Number.isInteger(card.battlegroundsNormalDbfId))
        .map(card => {
            if (typeof card.id !== "string" || card.id.length === 0)
                throw new Error("buddy normal card is missing id");
            if (!Number.isInteger(card.techLevel) || !INITIAL_POOL_COPIES[card.techLevel])
                throw new Error(`buddy ${card.id} has invalid tier ${card.techLevel}`);
            if (seen.has(card.id)) throw new Error(`duplicate buddy ${card.id}`);
            seen.add(card.id);
            const golden = byDbfId.get(card.battlegroundsPremiumDbfId);
            if (!golden || typeof golden.id !== "string"
                || golden.battlegroundsNormalDbfId !== card.dbfId)
                throw new Error(`buddy ${card.id} is missing its linked golden card`);
            return {
                cardId: card.id,
                goldenCardId: golden.id,
                tier: card.techLevel,
                races: Array.isArray(card.races) ? [...card.races].sort() : [],
                initialPoolCopies: INITIAL_POOL_COPIES[card.techLevel],
            };
        })
        .sort((left, right) => left.cardId.localeCompare(right.cardId, "en"));

    return {
        build,
        source: "HearthstoneJSON isBattlegroundsBuddy=true; normal/premium DBF link; CardPoolTracker tier copies",
        cards: records,
    };
}

function main() {
    const root = path.resolve(__dirname, "..");
    const sourcePath = path.join(root, "data", "_authority", "_hsjson_all_zh.json");
    const factsPath = path.join(root, "data", "anomaly_facts.json");
    const outputPath = path.join(root, "data", "buddy_minion_registry.json");
    const cards = JSON.parse(fs.readFileSync(sourcePath, "utf8"));
    const facts = JSON.parse(fs.readFileSync(factsPath, "utf8"));
    const registry = buildRegistry(cards, facts.build);
    fs.writeFileSync(outputPath, JSON.stringify(registry, null, 2) + "\n", "utf8");
    const tiers = Object.fromEntries([1, 2, 3, 4, 5, 6]
        .map(tier => [tier, registry.cards.filter(card => card.tier === tier).length]));
    console.log(`generated ${registry.cards.length} buddy minions ${JSON.stringify(tiers)}`);
}

if (require.main === module) main();

module.exports = { buildRegistry, INITIAL_POOL_COPIES };

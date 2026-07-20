"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const BUILD = 246003;
const SOURCE_URL = `https://api.hearthstonejson.com/v1/${BUILD}/zhCN/cards.json`;
const RETRIEVED_DATE = "2026-07-10";
const GENERATOR_PATH = "scripts/generate_shop_pool_registry.js";

function sha256(content) {
    return crypto.createHash("sha256").update(content).digest("hex").toUpperCase();
}

function serializeJson(value) {
    return `${JSON.stringify(value, null, 2)}\n`;
}

function compareCardIds(left, right) {
    return left < right ? -1 : left > right ? 1 : 0;
}

function buildRegistry(sourceCards, sourceSha256) {
    if (!Array.isArray(sourceCards)) throw new Error("source cards must be a JSON array");
    if (!/^[A-F0-9]{64}$/.test(sourceSha256)) throw new Error("source SHA-256 is invalid");

    const selected = [];
    let minions = 0;
    let spells = 0;
    for (const card of sourceCards) {
        if (!card || typeof card.id !== "string" || card.set !== "BATTLEGROUNDS") continue;
        const isMinion = card.type === "MINION"
            && card.isBattlegroundsPoolMinion === true
            && card.battlegroundsNormalDbfId === undefined;
        const isSpell = card.type === "BATTLEGROUND_SPELL"
            && card.isBattlegroundsPoolSpell === true;
        if (!isMinion && !isSpell) continue;
        selected.push(card.id);
        if (isMinion) minions++;
        else spells++;
    }

    const unique = new Set(selected);
    if (unique.size !== selected.length) throw new Error("shop pool source contains duplicate IDs");
    const cards = [...unique].sort(compareCardIds);
    return {
        schemaVersion: 1,
        build: BUILD,
        sourceUrl: SOURCE_URL,
        retrievedDate: RETRIEVED_DATE,
        generator: GENERATOR_PATH,
        sourceSha256,
        cardsSha256: sha256(serializeJson(cards)),
        counts: { minions, spells, total: cards.length },
        cards,
    };
}

function main() {
    const root = path.resolve(__dirname, "..");
    const sourcePath = path.join(root, "data", "_authority", "_hsjson_all_zh.json");
    const outputPath = path.join(root, "data", "shop_pool_registry.json");
    const sourceBuffer = fs.readFileSync(sourcePath);
    const registry = buildRegistry(
        JSON.parse(sourceBuffer.toString("utf8")),
        sha256(sourceBuffer),
    );
    const output = serializeJson(registry);
    fs.writeFileSync(outputPath, output, "utf8");
    console.log(`generated ${registry.cards.length} shop pool members, sha256=${sha256(output)}`);
}

if (require.main === module) main();

module.exports = {
    BUILD,
    SOURCE_URL,
    buildRegistry,
    main,
    serializeJson,
    sha256,
};

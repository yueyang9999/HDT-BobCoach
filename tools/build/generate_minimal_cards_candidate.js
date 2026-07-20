"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const BUILD = 246003;
const SOURCE_URL = `https://api.hearthstonejson.com/v1/${BUILD}/zhCN/cards.json`;
const RETRIEVED_DATE = "2026-07-10";
const GENERATOR_PATH = "scripts/generate_minimal_cards_candidate.js";
const ALLOWED_FIELDS = [
    "str_id",
    "card_type",
    "tier",
    "attack",
    "health",
    "minion_types_cn",
];

const TYPE_MAP = Object.freeze({
    MINION: "minion",
    HERO: "hero",
    HERO_POWER: "hero power",
    BATTLEGROUND_SPELL: "tavern",
    BATTLEGROUND_TRINKET: "trinket",
    BATTLEGROUND_ANOMALY: "anomaly",
    BATTLEGROUND_QUEST_REWARD: "baconquestreward",
    SPELL: "quest",
});

const RACE_MAP = Object.freeze({
    BEAST: "野兽",
    DEMON: "恶魔",
    DRAGON: "龙",
    ELEMENTAL: "元素",
    MECH: "机械",
    MECHANICAL: "机械",
    MURLOC: "鱼人",
    NAGA: "纳迦",
    PIRATE: "海盗",
    QUILBOAR: "野猪人",
    UNDEAD: "亡灵",
});
const ALL_RACES_CN = Object.freeze([
    "亡灵", "鱼人", "恶魔", "机械", "元素", "野兽", "海盗", "龙", "野猪人", "纳迦",
]);

function sha256(content) {
    return crypto.createHash("sha256").update(content).digest("hex").toUpperCase();
}

function serializeJson(value) {
    return `${JSON.stringify(value, null, 2)}\n`;
}

function compareCardIds(left, right) {
    return left < right ? -1 : left > right ? 1 : 0;
}

function readSelectionIds(selectionRows) {
    if (!Array.isArray(selectionRows)) throw new Error("selection must be a JSON array");
    const ids = selectionRows.map((row, index) => {
        const id = typeof row === "string" ? row : row && row.str_id;
        if (typeof id !== "string" || id.length === 0) {
            throw new Error(`selection row ${index} has no str_id`);
        }
        return id;
    });
    const unique = new Set(ids);
    if (unique.size !== ids.length) throw new Error("selection contains duplicate card IDs");
    return ids.sort(compareCardIds);
}

function mapRaces(card) {
    const sourceRaces = Array.isArray(card.races) && card.races.length > 0
        ? card.races
        : card.race ? [card.race] : [];
    const result = [];
    for (const race of sourceRaces) {
        if (race === "ALL") {
            for (const mapped of ALL_RACES_CN) {
                if (!result.includes(mapped)) result.push(mapped);
            }
            continue;
        }
        const mapped = RACE_MAP[race];
        if (!mapped) throw new Error(`unsupported race ${race} on ${card.id}`);
        if (!result.includes(mapped)) result.push(mapped);
    }
    return result.length > 0 ? result : ["中立"];
}

function buildCards(authorityRows, selectionRows, includeClassificationInputs) {
    if (!Array.isArray(authorityRows)) throw new Error("authority must be a JSON array");
    const authorityById = new Map();
    for (const card of authorityRows) {
        if (!card || typeof card.id !== "string" || card.id.length === 0) continue;
        if (authorityById.has(card.id)) throw new Error(`authority contains duplicate ID ${card.id}`);
        authorityById.set(card.id, card);
    }

    return readSelectionIds(selectionRows).map(cardId => {
        const source = authorityById.get(cardId);
        if (!source) throw new Error(`selection ID missing from authority: ${cardId}`);
        const cardType = TYPE_MAP[source.type];
        if (!cardType) throw new Error(`unsupported card type ${source.type} on ${cardId}`);

        const output = {
            str_id: cardId,
            card_type: cardType,
        };

        if (includeClassificationInputs) {
            output.text_cn = source.text || "";
            output.mechanics = Array.isArray(source.mechanics) ? [...source.mechanics] : [];
        }

        if (source.type === "MINION") {
            if (Number.isInteger(source.techLevel)) output.tier = source.techLevel;
            output.attack = Number.isInteger(source.attack) ? source.attack : 0;
            output.health = Number.isInteger(source.health) ? source.health : 0;
            output.minion_types_cn = mapRaces(source);
        } else if (source.type === "BATTLEGROUND_SPELL") {
            if (Number.isInteger(source.techLevel)) output.tier = source.techLevel;
        }

        return output;
    });
}

function buildRuntimeCards(authorityRows, selectionRows) {
    return buildCards(authorityRows, selectionRows, false);
}

function buildClassificationReviewCards(authorityRows, selectionRows) {
    return buildCards(authorityRows, selectionRows, true);
}

function createManifest({ authorityBuffer, selectionBuffer, outputBuffer, selectionRows, cards }) {
    const selectionFields = [...new Set(selectionRows.flatMap(row =>
        row && typeof row === "object" && !Array.isArray(row) ? Object.keys(row) : []))];
    return {
        schemaVersion: 1,
        activation: "development_audit_only",
        build: BUILD,
        sourceUrl: SOURCE_URL,
        retrievedDate: RETRIEVED_DATE,
        generator: GENERATOR_PATH,
        selectionContract: "pre-5B.2 cards.json str_id membership only",
        rowCount: cards.length,
        authoritySha256: sha256(authorityBuffer),
        selectionSha256: sha256(selectionBuffer),
        outputSha256: sha256(outputBuffer),
        retainedFields: ALLOWED_FIELDS,
        removedSelectionFields: selectionFields
            .filter(field => !ALLOWED_FIELDS.includes(field))
            .sort(compareCardIds),
    };
}

function parseArgs(argv) {
    const options = {};
    for (let index = 0; index < argv.length; index += 2) {
        const key = argv[index];
        const value = argv[index + 1];
        if (!key || !key.startsWith("--") || !value) throw new Error(`invalid argument near ${key || "end"}`);
        options[key.slice(2)] = value;
    }
    return options;
}

function main(argv = process.argv.slice(2)) {
    const root = path.resolve(__dirname, "..");
    const options = parseArgs(argv);
    const authorityPath = path.resolve(options.authority || path.join(root, "data", "_authority", "_hsjson_all_zh.json"));
    const selectionPath = path.resolve(options.selection || path.join(root, "data", "cards.json"));
    const outputPath = path.resolve(options.output || path.join(root, "data", "cards_minimal_246003.candidate.json"));
    const manifestPath = path.resolve(options.manifest || path.join(root, "data", "cards_minimal_246003.candidate.manifest.json"));
    const productionPath = path.resolve(root, "data", "cards.json");
    if (outputPath === productionPath || manifestPath === productionPath) {
        throw new Error("candidate generator must not overwrite production cards.json");
    }

    const authorityBuffer = fs.readFileSync(authorityPath);
    const selectionBuffer = fs.readFileSync(selectionPath);
    const authorityRows = JSON.parse(authorityBuffer.toString("utf8"));
    const selectionRows = JSON.parse(selectionBuffer.toString("utf8"));
    const cards = buildRuntimeCards(authorityRows, selectionRows);
    const outputBuffer = Buffer.from(serializeJson(cards), "utf8");
    const manifest = createManifest({
        authorityBuffer,
        selectionBuffer,
        outputBuffer,
        selectionRows,
        cards,
    });
    fs.writeFileSync(outputPath, outputBuffer);
    fs.writeFileSync(manifestPath, serializeJson(manifest), "utf8");
    console.log(`generated candidate ${cards.length} rows, sha256=${manifest.outputSha256}`);
}

if (require.main === module) main();

module.exports = {
    ALLOWED_FIELDS,
    BUILD,
    RETRIEVED_DATE,
    SOURCE_URL,
    buildRuntimeCards,
    buildClassificationReviewCards,
    createManifest,
    serializeJson,
    sha256,
};

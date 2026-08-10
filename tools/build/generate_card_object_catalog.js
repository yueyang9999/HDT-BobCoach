"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

function sha256(text) {
    return crypto.createHash("sha256").update(text, "utf8").digest("hex");
}

function requireDataRoot(argv) {
    const index = argv.indexOf("--data-root");
    if (index < 0 || !argv[index + 1]) {
        throw new Error("--data-root is required");
    }
    return path.resolve(argv[index + 1]);
}

function readJson(file) {
    if (!fs.existsSync(file)) return null;
    return JSON.parse(fs.readFileSync(file, "utf8"));
}

function readCardsMap(file) {
    const root = readJson(file);
    if (!root) return new Map();
    const cards = Array.isArray(root) ? root : root.cards;
    const map = new Map();
    for (const card of cards || []) {
        if (card && typeof card.id === "string") map.set(card.id, card);
    }
    return map;
}

function parseActivateCost(text) {
    if (!text) return 0;
    const match = text.match(/Activate\s*\((\d+)\)/i) || text.match(/发动（(\d+)）/);
    return match ? Number(match[1]) : 0;
}

function mergeRows(enMap, zhMap, card) {
    const en = enMap.get(card.cardId) || {};
    const zh = zhMap.get(card.cardId) || {};
    return {
        cardId: card.cardId,
        dbfId: card.dbfId != null ? card.dbfId : en.dbfId,
        nameCn: card.name_cn || zh.name || en.name || "",
        nameEn: card.name_en || en.name || "",
        type: card.type || en.type || "",
        techLevel: card.techLevel != null ? card.techLevel : en.techLevel,
        cost: card.cost != null ? card.cost : en.cost,
        attack: card.attack != null ? card.attack : en.attack,
        health: card.health != null ? card.health : en.health,
        races: Array.isArray(card.races) && card.races.length > 0
            ? card.races
            : en.races || [],
        mechanics: Array.isArray(card.mechanics) ? card.mechanics : en.mechanics || [],
        textCn: card.text_cn || zh.text || "",
        textEn: card.text_en || en.text || "",
        canActivate: false,
        activateCost: 0,
    };
}

function buildCatalog(root) {
    const enMap = readCardsMap(path.join(root, "raw", "cards.enUS.latest.json"));
    const zhMap = readCardsMap(path.join(root, "raw", "cards.zhCN.latest.json"));
    const registryRoot = path.join(root, "generated", "registries");
    const minion = readJson(path.join(registryRoot, "minion_registry.json"));
    const spell = readJson(path.join(registryRoot, "tavern_spell_registry.json"));
    const trinket = readJson(path.join(registryRoot, "trinket_registry.json"));
    const activate = readJson(path.join(registryRoot, "activate_registry.json"));
    const hero = readJson(path.join(registryRoot, "hero_registry.json"));

    const rows = [];
    for (const source of [minion, spell, hero]) {
        for (const card of source && Array.isArray(source.cards) ? source.cards : []) {
            if (!card || !card.cardId) continue;
            rows.push(mergeRows(enMap, zhMap, card));
        }
    }
    for (const card of trinket && Array.isArray(trinket.lesser) ? trinket.lesser : []) {
        if (card && card.cardId) rows.push(mergeRows(enMap, zhMap, card));
    }
    for (const card of trinket && Array.isArray(trinket.greater) ? trinket.greater : []) {
        if (card && card.cardId) rows.push(mergeRows(enMap, zhMap, card));
    }

    const activateById = new Map();
    for (const card of activate && Array.isArray(activate.cards) ? activate.cards : []) {
        if (card && card.cardId) activateById.set(card.cardId, card);
    }
    for (const row of rows) {
        const act = activateById.get(row.cardId);
        if (!act) continue;
        row.canActivate = true;
        row.activateCost = parseActivateCost(act.text_en || row.textEn);
    }

    const unique = new Map();
    for (const row of rows) {
        if (!unique.has(row.cardId)) unique.set(row.cardId, row);
    }
    const cards = [...unique.values()].sort((a, b) => a.cardId.localeCompare(b.cardId));
    const meta = (minion && minion._meta) || (activate && activate._meta) || {
        schemaVersion: 1,
        season: "unknown",
        gameBuild: "unknown",
        cardPoolIdentity: "unknown",
        source: "generate_card_object_catalog",
        capturedAt: new Date().toISOString(),
    };
    const output = {
        _meta: {
            ...meta,
            source: "generate_card_object_catalog",
            capturedAt: new Date().toISOString(),
        },
        kind: "cardObjectCatalog",
        cards,
        counts: { total: cards.length, activate: cards.filter(c => c.canActivate).length },
    };
    const outputPath = path.join(registryRoot, "card_object_catalog.json");
    fs.writeFileSync(outputPath, JSON.stringify(output, null, 2) + "\n", "utf8");
    const text = fs.readFileSync(outputPath, "utf8");
    output._meta.sha256 = sha256(text);
    fs.writeFileSync(outputPath, JSON.stringify(output, null, 2) + "\n", "utf8");
    console.log(`PASS card_object_catalog total=${cards.length} activate=${output.counts.activate}`);
}

const root = requireDataRoot(process.argv.slice(2));
buildCatalog(root);

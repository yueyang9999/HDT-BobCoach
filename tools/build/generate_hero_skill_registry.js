"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

function sha256(text) {
    return crypto.createHash("sha256").update(text, "utf8").digest("hex");
}

function requireDataRoot(argv) {
    const index = argv.indexOf("--data-root");
    if (index < 0 || !argv[index + 1]) throw new Error("--data-root is required");
    return path.resolve(argv[index + 1]);
}

function readJson(file) {
    if (!fs.existsSync(file)) return null;
    return JSON.parse(fs.readFileSync(file, "utf8"));
}

function readCardsByDbf(file) {
    const root = readJson(file);
    if (!root) return new Map();
    const cards = Array.isArray(root) ? root : root.cards;
    const map = new Map();
    for (const card of cards || []) {
        if (card && card.dbfId != null) map.set(card.dbfId, card);
    }
    return map;
}

function inferPowerType(card) {
    const text = `${card.text || ""} ${card.name || ""}`.toLowerCase();
    if (text.includes("passive") || text.includes("被动")) return "Passive";
    if (text.includes("aura") || text.includes("光环")) return "Aura";
    if (card.cost != null && Number(card.cost) > 0) return "Active";
    return "unknown";
}

function buildHeroSkills(root) {
    const en = readCardsByDbf(path.join(root, "raw", "cards.enUS.latest.json"));
    const zh = readCardsByDbf(path.join(root, "raw", "cards.zhCN.latest.json"));
    const heroRegistry = readJson(path.join(
        root, "generated", "registries", "hero_registry.json"));
    const rows = [];
    for (const hero of heroRegistry && Array.isArray(heroRegistry.cards)
        ? heroRegistry.cards : []) {
        if (!hero || !hero.cardId || hero.heroPowerDbfId == null) continue;
        const powerEn = en.get(hero.heroPowerDbfId) || {};
        const powerZh = zh.get(hero.heroPowerDbfId) || {};
        const buddy = hero.battlegroundsBuddyDbfId != null
            ? en.get(hero.battlegroundsBuddyDbfId) : null;
        rows.push({
            heroCardId: hero.cardId,
            heroPowerCardId: powerEn.id || "",
            buddyCardId: buddy ? buddy.id : "",
            nameCn: hero.name_cn || "",
            nameEn: hero.name_en || "",
            powerType: inferPowerType(powerEn),
            cost: powerEn.cost != null ? powerEn.cost : -1,
            unlockTurn: 1,
            unlockTier: 1,
            textCn: powerZh.text || "",
            textEn: powerEn.text || "",
        });
    }
    const meta = (heroRegistry && heroRegistry._meta) || {
        schemaVersion: 1,
        season: "unknown",
        gameBuild: "unknown",
        cardPoolIdentity: "unknown",
        source: "generate_hero_skill_registry",
        capturedAt: new Date().toISOString(),
    };
    const output = {
        _meta: {
            ...meta,
            source: "generate_hero_skill_registry",
            capturedAt: new Date().toISOString(),
        },
        kind: "heroSkillRegistry",
        cards: rows,
        counts: { total: rows.length, active: rows.filter(r => r.powerType === "Active").length },
    };
    const outputPath = path.join(
        root, "generated", "registries", "hero_skill_registry.json");
    fs.writeFileSync(outputPath, JSON.stringify(output, null, 2) + "\n", "utf8");
    output._meta.sha256 = sha256(fs.readFileSync(outputPath, "utf8"));
    fs.writeFileSync(outputPath, JSON.stringify(output, null, 2) + "\n", "utf8");
    console.log(`PASS hero_skill_registry total=${rows.length} active=${output.counts.active}`);
}

const root = requireDataRoot(process.argv.slice(2));
buildHeroSkills(root);

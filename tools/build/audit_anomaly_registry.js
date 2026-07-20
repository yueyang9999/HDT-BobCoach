"use strict";

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

function normalizeCardText(value) {
    return String(value || "")
        .replace(/<[^>]+>/g, "")
        .replace(/\[x\]/g, "")
        .split("@")[0]
        .replace(/\s+/g, "")
        .replace(/[“”‘’'"。,.，！!（）()：:]/g, "");
}

function auditAnomalyRegistry({ registry, cards, build, manifest }) {
    if (cards && !Array.isArray(cards) && Array.isArray(cards.cards)) cards = cards.cards;
    const cardMap = new Map((cards || []).map(card => [card.id, card]));
    const records = [];

    for (const [cardId, entry] of Object.entries(registry || {})) {
        const facts = (manifest || {})[cardId];
        const card = cardMap.get(cardId);
        const issues = [];

        if (!facts) issues.push("missing_manifest_entry");
        if (entry.freeRefresh === 99) issues.push("legacy_free_refresh_sentinel");
        if (facts && facts.availability === "active"
            && (!Array.isArray(facts.rules) || facts.rules.length === 0)) {
            issues.push("missing_typed_rules");
        }
        if (!card && (!facts || facts.availability !== "not_in_build")) {
            issues.push("missing_build_card");
        }

        const normalizedRegistryText = normalizeCardText(entry.text);
        const normalizedBuildText = normalizeCardText(card && card.text);
        let textStatus = "missing";
        if (card) {
            textStatus = normalizedRegistryText === normalizedBuildText ? "exact" : "different";
            if (textStatus === "different") issues.push("text_difference");
        }

        const childCardIds = facts && Array.isArray(facts.childCardIds)
            ? facts.childCardIds.slice()
            : [];
        for (const childCardId of childCardIds) {
            if (!cardMap.has(childCardId)) issues.push("missing_child_card:" + childCardId);
        }

        records.push({
            cardId,
            name: entry.name || (card && card.name) || cardId,
            build,
            lifecycle: facts ? facts.lifecycle : "unknown",
            scope: facts ? facts.scope : "unknown",
            availability: facts ? facts.availability : "unknown",
            childCardIds,
            rules: facts && Array.isArray(facts.rules) ? facts.rules.slice() : [],
            sourceText: card ? card.text || "" : "",
            textSha256: card
                ? crypto.createHash("sha256").update(String(card.text || ""), "utf8").digest("hex").toUpperCase()
                : "",
            textStatus,
            issues: issues.sort(),
        });
    }

    records.sort((a, b) => a.cardId.localeCompare(b.cardId));
    const ready = records.filter(record => record.issues.length === 0).length;
    return {
        build,
        summary: {
            total: records.length,
            ready,
            blocked: records.length - ready,
        },
        records,
    };
}

function readJson(filePath) {
    return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
}

function runCli(argv) {
    const root = path.resolve(__dirname, "..");
    const cardsPath = argv[2] || path.join(root, "data", "anomaly_build_246003_cards.json");
    const build = Number(argv[3] || 246003);
    const registry = readJson(path.join(root, "data", "anomaly_registry.json"));
    const manifest = readJson(path.join(root, "data", "anomaly_manifest.json"));
    const cards = readJson(cardsPath);
    const snapshotIndex = argv.indexOf("--snapshot");
    if (snapshotIndex >= 0) {
        const snapshotPath = argv[snapshotIndex + 1];
        if (!snapshotPath) throw new Error("--snapshot requires an output path");
        const sourceCards = Array.isArray(cards) ? cards : cards.cards;
        const requiredIds = new Set(Object.keys(registry));
        for (const facts of Object.values(manifest)) {
            for (const childId of facts.childCardIds || []) requiredIds.add(childId);
        }
        const snapshot = sourceCards
            .filter(card => requiredIds.has(card.id))
            .map(card => ({ id: card.id, name: card.name || "", text: card.text || "", type: card.type || "" }))
            .sort((a, b) => a.id.localeCompare(b.id));
        fs.writeFileSync(snapshotPath, JSON.stringify(snapshot, null, 2) + "\n", "utf8");
        process.stdout.write("WROTE " + snapshot.length + " cards to " + snapshotPath + "\n");
        return;
    }
    const report = auditAnomalyRegistry({ registry, cards, build, manifest });
    const outputIndex = argv.indexOf("--output");
    if (outputIndex >= 0) {
        const outputPath = argv[outputIndex + 1];
        if (!outputPath) throw new Error("--output requires a path");
        fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + "\n", "utf8");
    } else {
        process.stdout.write(JSON.stringify(report, null, 2) + "\n");
    }
    if (report.summary.blocked > 0) process.exitCode = 1;
}

if (require.main === module) runCli(process.argv);

module.exports = {
    auditAnomalyRegistry,
    normalizeCardText,
};

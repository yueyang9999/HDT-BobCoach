"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const { cardPoolIdentity, contractHeader } = require("../model/contracts");

const GENERATOR_NAME = "audit_season_snapshot";
const SUPPORTED_OPTIONS = new Set([
    "--old-en", "--new-en", "--old-zh", "--new-zh", "--season",
    "--game-build", "--output", "--captured-at",
]);

function parseArgs(argv) {
    const args = {};
    for (let index = 0; index < argv.length; index += 2) {
        const option = argv[index];
        const value = argv[index + 1];
        if (!SUPPORTED_OPTIONS.has(option)) throw new Error(`unknown option: ${option || "<empty>"}`);
        if (!value || value.startsWith("--")) throw new Error(`missing value for option: ${option}`);
        if (Object.prototype.hasOwnProperty.call(args, option)) throw new Error(`duplicate option: ${option}`);
        args[option] = value;
    }
    return args;
}

function requireOption(args, option) {
    if (!args[option]) throw new Error(`required option: ${option}`);
    return args[option];
}

function readJson(file) {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function sha256File(file) {
    return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

function isSoloCard(card) {
    return typeof card.id === "string" && !/^BGDUO/.test(card.id);
}

function isBattlegroundsCard(card) {
    return /^BATTLEGROUND/i.test(card.set || "");
}

function isPoolMinion(card) {
    return isSoloCard(card) && card.type === "MINION" && card.isBattlegroundsPoolMinion === true;
}

function isPoolSpell(card) {
    return isSoloCard(card) && card.type === "BATTLEGROUND_SPELL" && card.isBattlegroundsPoolSpell === true;
}

function isTrinket(card) {
    return isSoloCard(card) && card.type === "BATTLEGROUND_TRINKET";
}

function compareIds(left, right) {
    return Buffer.compare(Buffer.from(left, "utf8"), Buffer.from(right, "utf8"));
}

function sortIds(ids) {
    return [...new Set(ids)].sort(compareIds);
}

function buildCardIndex(cards) {
    const index = new Map();
    for (const card of cards) {
        if (!index.has(card.id)) index.set(card.id, card);
    }
    return index;
}

function describeCard(enCard, zhIndex) {
    if (!enCard) return null;
    const zh = enCard && zhIndex.get(enCard.id);
    return {
        id: enCard.id,
        name_en: enCard.name || "",
        name_cn: zh && zh.name ? zh.name : "",
        type: enCard.type || "",
        spellSchool: enCard.spellSchool || "",
        text_en: (enCard.text || "").replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim(),
        text_cn: zh && zh.text ? zh.text.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim() : "",
    };
}

function summarizeSnapshot(cards, zhIndex) {
    const battlegrounds = cards.filter(isBattlegroundsCard);
    const soloBattlegrounds = battlegrounds.filter(isSoloCard);
    const poolMinions = soloBattlegrounds.filter(isPoolMinion);
    const poolSpells = soloBattlegrounds.filter(isPoolSpell);
    const trinkets = soloBattlegrounds.filter(isTrinket);
    const anomalies = soloBattlegrounds.filter(card => card.type === "BATTLEGROUND_ANOMALY");
    const heroes = soloBattlegrounds.filter(card => card.type === "HERO");
    const heroPowers = soloBattlegrounds.filter(card => card.type === "HERO_POWER");
    const trinketSections = { lesser: [], greater: [] };
    for (const card of trinkets) {
        const section = card.spellSchool === "LESSER_TRINKET" ? "lesser" : "greater";
        trinketSections[section].push(card);
    }

    return {
        totalCards: cards.length,
        battlegroundsCards: battlegrounds.length,
        soloBattlegroundsCards: soloBattlegrounds.length,
        poolMinions: poolMinions.length,
        poolMinionIds: sortIds(poolMinions.map(card => card.id)),
        poolSpells: poolSpells.length,
        poolSpellIds: sortIds(poolSpells.map(card => card.id)),
        trinkets: trinkets.length,
        trinketIds: sortIds(trinkets.map(card => card.id)),
        trinketsLesser: trinketSections.lesser.length,
        trinketsGreater: trinketSections.greater.length,
        anomalies: anomalies.length,
        anomalyIds: sortIds(anomalies.map(card => card.id)),
        heroes: heroes.length,
        heroPowers: heroPowers.length,
        heroIds: sortIds(heroes.map(card => card.id)),
        heroPowerIds: sortIds(heroPowers.map(card => card.id)),
    };
}

function diffIds(oldIds, newIds) {
    const oldSet = new Set(oldIds);
    const newSet = new Set(newIds);
    return {
        removed: sortIds(oldIds.filter(id => !newSet.has(id))),
        added: sortIds(newIds.filter(id => !oldSet.has(id))),
    };
}

function classifyAdded(added, oldAllIds) {
    const oldAllSet = new Set(oldAllIds);
    const returning = added.filter(id => oldAllSet.has(id));
    const brandNew = added.filter(id => !oldAllSet.has(id));
    return {
        brandNew: sortIds(brandNew),
        returningCandidates: sortIds(returning),
    };
}

function collectPoolDiff(oldCards, newCards) {
    const oldAllIds = oldCards.map(card => card.id);
    const newAllIds = newCards.map(card => card.id);

    const minions = diffIds(
        oldCards.filter(isPoolMinion).map(card => card.id),
        newCards.filter(isPoolMinion).map(card => card.id)
    );
    const spells = diffIds(
        oldCards.filter(isPoolSpell).map(card => card.id),
        newCards.filter(isPoolSpell).map(card => card.id)
    );

    const minionClassification = classifyAdded(minions.added, oldAllIds);
    const spellClassification = classifyAdded(spells.added, oldAllIds);

    const oldTrinket = oldCards.filter(isTrinket);
    const newTrinket = newCards.filter(isTrinket);
    const oldTrinketLesser = sortIds(oldTrinket
        .filter(card => card.spellSchool === "LESSER_TRINKET").map(card => card.id));
    const oldTrinketGreater = sortIds(oldTrinket
        .filter(card => card.spellSchool !== "LESSER_TRINKET").map(card => card.id));
    const newTrinketLesser = sortIds(newTrinket
        .filter(card => card.spellSchool === "LESSER_TRINKET").map(card => card.id));
    const newTrinketGreater = sortIds(newTrinket
        .filter(card => card.spellSchool !== "LESSER_TRINKET").map(card => card.id));

    return {
        minions: {
            oldCount: oldCards.filter(isPoolMinion).length,
            newCount: newCards.filter(isPoolMinion).length,
            removedCount: minions.removed.length,
            removed: minions.removed,
            addedCount: minions.added.length,
            added: minions.added,
            brandNewCount: minionClassification.brandNew.length,
            brandNew: minionClassification.brandNew,
            returningCandidateCount: minionClassification.returningCandidates.length,
            returningCandidates: minionClassification.returningCandidates,
        },
        spells: {
            oldCount: oldCards.filter(isPoolSpell).length,
            newCount: newCards.filter(isPoolSpell).length,
            removedCount: spells.removed.length,
            removed: spells.removed,
            addedCount: spells.added.length,
            added: spells.added,
            brandNewCount: spellClassification.brandNew.length,
            brandNew: spellClassification.brandNew,
            returningCandidateCount: spellClassification.returningCandidates.length,
            returningCandidates: spellClassification.returningCandidates,
        },
        trinkets: {
            oldCount: oldTrinket.length,
            newCount: newTrinket.length,
            lesser: {
                oldCount: oldTrinketLesser.length,
                newCount: newTrinketLesser.length,
                removed: diffIds(oldTrinketLesser, newTrinketLesser).removed,
                added: diffIds(oldTrinketLesser, newTrinketLesser).added,
            },
            greater: {
                oldCount: oldTrinketGreater.length,
                newCount: newTrinketGreater.length,
                removed: diffIds(oldTrinketGreater, newTrinketGreater).removed,
                added: diffIds(oldTrinketGreater, newTrinketGreater).added,
            },
        },
    };
}

function collectMechanics(newCards, newZhIndex, oldAnomalyIds) {
    const battlegrounds = newCards.filter(isBattlegroundsCard);
    const soloBattlegrounds = battlegrounds.filter(isSoloCard);
    const activateMatches = soloBattlegrounds
        .filter(card => /<b>Activate\s*\(|Activate\s*\(/i.test(card.text || ""))
        .map(card => card.id);
    const lockboxMatches = soloBattlegrounds
        .filter(card => /\bLockbox\b/i.test(card.text || "") || /\bLockbox\b/i.test(card.name || ""))
        .map(card => card.id);
    const fishbaitMatches = soloBattlegrounds
        .filter(card => /\bFishbait\b/i.test(card.text || "") || /\bFishbait\b/i.test(card.name || ""))
        .map(card => card.id);

    const darkGiftRelated = soloBattlegrounds
        .filter(card => /Dark Gift/i.test(card.text || "") || /Dark Gift/i.test(card.name || ""))
        .map(card => card.id);

    const darkGiftEffectRows = soloBattlegrounds
        .filter(card => /^BG36_MidGameEffect_000t/.test(card.id))
        .map(card => card);
    const darkGiftNames = [];
    const seenNames = new Set();
    for (const card of darkGiftEffectRows) {
        const name = normalizeDarkGiftName(card.name || "");
        if (name && !seenNames.has(name)) {
            seenNames.add(name);
            darkGiftNames.push(card.name.trim());
        }
    }
    darkGiftNames.sort();

    const bg36Anomaly = newCards.find(card => card.id === "BG36_Anomaly_002");
    const currentSeasonAnomalyCandidates = newCards
        .filter(card => card.type === "BATTLEGROUND_ANOMALY" && /^BG36_/.test(card.id))
        .map(card => describeCard(card, newZhIndex));

    return {
        activate: {
            matchedCardCount: activateMatches.length,
            matchedCards: sortIds(activateMatches),
        },
        lockbox: {
            matchedCardCount: lockboxMatches.length,
            matchedCards: sortIds(lockboxMatches),
        },
        fishbait: {
            matchedCardCount: fishbaitMatches.length,
            matchedCards: sortIds(fishbaitMatches),
        },
        darkGift: {
            officialCount: 43,
            rawUniqueEffectNameCount: darkGiftNames.length,
            rawUniqueEffectNames: darkGiftNames,
            rawEffectRowCount: darkGiftEffectRows.length,
            relatedCardCount: darkGiftRelated.length,
            relatedCards: sortIds(darkGiftRelated),
        },
        anomaly: {
            bg36AnomalyIsDarkGiftMechanic: Boolean(bg36Anomaly),
            bg36Anomaly: bg36Anomaly ? describeCard(bg36Anomaly, newZhIndex) : null,
            currentSeasonAnomalyCandidates,
            legacyAnomalyCountInLatestData: soloBattlegrounds
                .filter(card => card.type === "BATTLEGROUND_ANOMALY" && !/^BG36_/.test(card.id)).length,
            legacyAnomalyIdsInLatestData: sortIds(soloBattlegrounds
                .filter(card => card.type === "BATTLEGROUND_ANOMALY" && !/^BG36_/.test(card.id))
                .map(card => card.id)),
            oldAnomalyIds: sortIds(oldAnomalyIds),
        },
    };
}

function normalizeDarkGiftName(value) {
    return value
        .replace(/[\u2018\u2019\u201A\u2032]/g, "'")
        .replace(/[\u201C\u201D\u201E\u2033]/g, "\"")
        .replace(/\s+/g, " ")
        .trim()
        .toLowerCase();
}

function buildReport(options) {
    const oldEn = readJson(options.oldEn);
    const newEn = readJson(options.newEn);
    const oldZh = readJson(options.oldZh);
    const newZh = readJson(options.newZh);
    const oldZhIndex = buildCardIndex(oldZh);
    const newZhIndex = buildCardIndex(newZh);

    const oldSnapshot = summarizeSnapshot(oldEn, oldZhIndex);
    const newSnapshot = summarizeSnapshot(newEn, newZhIndex);
    const poolDiff = collectPoolDiff(oldEn, newEn);
    const mechanics = collectMechanics(newEn, newZhIndex, oldSnapshot.anomalyIds);

    const poolCardIds = [
        ...newSnapshot.poolMinionIds,
        ...newSnapshot.poolSpellIds,
        ...newSnapshot.trinketIds,
    ];
    const cardPoolIdentityValue = cardPoolIdentity(poolCardIds);
    const capturedAt = options.capturedAt || new Date().toISOString();

    const report = {
        season: options.season,
        gameBuild: options.gameBuild,
        sources: {
            oldEn: options.oldEn,
            newEn: options.newEn,
            oldZh: options.oldZh,
            newZh: options.newZh,
            oldEnSha256: sha256File(options.oldEn),
            newEnSha256: sha256File(options.newEn),
            oldZhSha256: sha256File(options.oldZh),
            newZhSha256: sha256File(options.newZh),
        },
        cardPoolIdentity: cardPoolIdentityValue,
        snapshots: {
            old: oldSnapshot,
            latest: newSnapshot,
        },
        diff: poolDiff,
        mechanics,
    };

    const sha256 = crypto.createHash("sha256")
        .update(JSON.stringify(report), "utf8")
        .digest("hex");
    const meta = contractHeader({
        season: options.season,
        gameBuild: options.gameBuild,
        cardPoolIdentity: cardPoolIdentityValue,
        source: "local-raw-json-capture",
        capturedAt,
        sha256,
        provenance: {
            generator: GENERATOR_NAME,
            inputs: {
                oldEn: options.oldEn,
                newEn: options.newEn,
                oldZh: options.oldZh,
                newZh: options.newZh,
            },
            officialReferences: [
                "https://hearthstone.blizzard.com/en-us/news/24290432/36-2-patch-notes",
            ],
        },
    });

    return { _meta: meta, ...report };
}

function printSummary(report) {
    const lines = [
        `season=${report.season}`,
        `gameBuild=${report.gameBuild}`,
        `sha256=${report._meta.sha256}`,
        `cardPoolIdentity=${report._meta.cardPoolIdentity}`,
        `minions: ${report.diff.minions.oldCount} -> ${report.diff.minions.newCount} `
            + `(removed=${report.diff.minions.removedCount}, added=${report.diff.minions.addedCount}, `
            + `new=${report.diff.minions.brandNewCount}, returning=${report.diff.minions.returningCandidateCount})`,
        `spells: ${report.diff.spells.oldCount} -> ${report.diff.spells.newCount} `
            + `(removed=${report.diff.spells.removedCount}, added=${report.diff.spells.addedCount}, `
            + `new=${report.diff.spells.brandNewCount}, returning=${report.diff.spells.returningCandidateCount})`,
        `trinkets: ${report.diff.trinkets.oldCount} -> ${report.diff.trinkets.newCount} `
            + `(lesser=${report.diff.trinkets.lesser.oldCount}->${report.diff.trinkets.lesser.newCount}, `
            + `greater=${report.diff.trinkets.greater.oldCount}->${report.diff.trinkets.greater.newCount})`,
        `activateMatches=${report.mechanics.activate.matchedCardCount}`,
        `lockboxMatches=${report.mechanics.lockbox.matchedCardCount}`,
        `fishbaitMatches=${report.mechanics.fishbait.matchedCardCount}`,
        `darkGiftRawNames=${report.mechanics.darkGift.rawUniqueEffectNameCount}/${report.mechanics.darkGift.officialCount}`,
    ];
    process.stdout.write(`${lines.join("\n")}\n`);
}

function run(argv) {
    const args = parseArgs(argv);
    const oldEn = requireOption(args, "--old-en");
    const newEn = requireOption(args, "--new-en");
    const oldZh = requireOption(args, "--old-zh");
    const newZh = requireOption(args, "--new-zh");
    const season = requireOption(args, "--season");
    const gameBuild = requireOption(args, "--game-build");
    const outputPath = requireOption(args, "--output");
    const capturedAt = args["--captured-at"] || null;
    if (capturedAt && !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/.test(capturedAt))
        throw new Error("captured-at must be an ISO-8601 UTC timestamp");

    const report = buildReport({
        oldEn,
        newEn,
        oldZh,
        newZh,
        season,
        gameBuild,
        capturedAt,
    });
    fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });
    fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
    printSummary(report);
}

if (require.main === module) {
    try {
        run(process.argv.slice(2));
    } catch (error) {
        process.stderr.write(`Error: ${error.message}\n`);
        process.exitCode = 1;
    }
}

module.exports = {
    GENERATOR_NAME,
    buildReport,
    classifyAdded,
    collectMechanics,
    collectPoolDiff,
    describeCard,
    diffIds,
    normalizeDarkGiftName,
    parseArgs,
    run,
    summarizeSnapshot,
};

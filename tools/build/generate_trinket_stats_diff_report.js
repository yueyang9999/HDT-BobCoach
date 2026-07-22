"use strict";

const fs = require("fs");

const MIN_REFERENCE_POINTS = 1000;
const SUPPORTED_OPTIONS = new Set(["--active", "--shadow", "--registry", "--output"]);

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

function parseJsonLines(text) {
    return String(text || "").replace(/^\uFEFF/, "").split(/\r?\n/)
        .filter(line => line.trim()).map(line => JSON.parse(line));
}

function averageRanks(items, compare, tieKey) {
    const sorted = items.slice().sort(compare);
    const ranks = new Map();
    for (let index = 0; index < sorted.length;) {
        let end = index + 1;
        while (end < sorted.length && tieKey(sorted[end]) === tieKey(sorted[index])) end++;
        const rank = ((index + 1) + end) / 2;
        for (let cursor = index; cursor < end; cursor++) ranks.set(sorted[cursor].cardId, rank);
        index = end;
    }
    return ranks;
}

function correlation(items, rankA, rankB) {
    if (items.length < 2) return null;
    const a = items.map(item => rankA.get(item.cardId));
    const b = items.map(item => rankB.get(item.cardId));
    const meanA = a.reduce((sum, value) => sum + value, 0) / a.length;
    const meanB = b.reduce((sum, value) => sum + value, 0) / b.length;
    let numerator = 0;
    let spreadA = 0;
    let spreadB = 0;
    for (let index = 0; index < a.length; index++) {
        const deltaA = a[index] - meanA;
        const deltaB = b[index] - meanB;
        numerator += deltaA * deltaB;
        spreadA += deltaA * deltaA;
        spreadB += deltaB * deltaB;
    }
    return spreadA > 0 && spreadB > 0 ? numerator / Math.sqrt(spreadA * spreadB) : null;
}

function buildCatalog(registry, statMap) {
    const result = [];
    for (const kind of ["lesser", "greater"]) {
        for (const row of Array.isArray(registry && registry[kind]) ? registry[kind] : []) {
            const stat = statMap.get(row.cardId);
            if (!stat) continue;
            result.push({
                cardId: row.cardId,
                name: row.name_cn || row.name || row.cardId,
                kind,
                rated: row.unrated !== true,
                schemeScore: row.unrated === true ? 0 : Number(row.score || 0),
                placement: Number(stat.AveragePlacement),
                pickRate: Number(stat.PickRate),
                dataPoints: Number(stat.DataPoints),
            });
        }
    }
    return result;
}

function rankCatalog(rows) {
    const schemeRanks = averageRanks(rows,
        (a, b) => Number(b.rated) - Number(a.rated) || b.schemeScore - a.schemeScore
            || a.cardId.localeCompare(b.cardId),
        item => `${item.rated ? 1 : 0}:${item.schemeScore}`);
    const referenceRanks = averageRanks(rows,
        (a, b) => a.placement - b.placement || a.cardId.localeCompare(b.cardId),
        item => item.placement.toFixed(9));
    return rows.map(item => ({
        ...item,
        schemeRank: schemeRanks.get(item.cardId),
        referenceRank: referenceRanks.get(item.cardId),
        rankGap: schemeRanks.get(item.cardId) - referenceRanks.get(item.cardId),
    }));
}

function topByScheme(offers) {
    return offers.slice().sort((a, b) => Number(!b.IsUnrated) - Number(!a.IsUnrated)
        || Number(b.score) - Number(a.score)
        || String(a.CardId).localeCompare(String(b.CardId)))[0];
}

function topByReference(offers, statMap) {
    return offers.filter(offer => statMap.has(offer.CardId)
        && Number(statMap.get(offer.CardId).DataPoints) >= MIN_REFERENCE_POINTS)
        .sort((a, b) => Number(statMap.get(a.CardId).AveragePlacement)
            - Number(statMap.get(b.CardId).AveragePlacement)
            || String(a.CardId).localeCompare(String(b.CardId)))[0];
}

function number(value, digits = 2) {
    return Number(value).toFixed(digits);
}

function cell(value) {
    return String(value == null ? "" : value).replace(/\|/g, "\\|");
}

function generateReport(active, registry, shadows, generatedAt = new Date()) {
    if (!active || active.StatusReason !== "verified" || !Array.isArray(active.Stats)) {
        throw new Error("refusing report: reference trinket statistics snapshot is not verified");
    }
    if (!Array.isArray(shadows)) throw new Error("refusing report: shadow input must be JSON Lines");

    const statMap = new Map(active.Stats.map(row => [row.TrinketCardId, row]));
    const catalog = buildCatalog(registry, statMap);
    const reliableCatalog = catalog.filter(row => row.dataPoints >= MIN_REFERENCE_POINTS);
    const ranked = ["lesser", "greater"].flatMap(kind =>
        rankCatalog(reliableCatalog.filter(row => row.kind === kind)));
    const eligibleShadows = shadows.filter(row => row.schemaVersion === 2
        && row.eligibleForCalibration === true
        && row.completionStatus === "completed"
        && row.selectedCardId
        && Array.isArray(row.offers)
        && row.offers.length > 0);

    const shadowComparisons = eligibleShadows.map(sample => {
        const scheme = topByScheme(sample.offers);
        const reference = topByReference(sample.offers, statMap);
        return {
            sample,
            scheme,
            reference,
            schemeReferenceAgree: Boolean(reference && scheme.CardId === reference.CardId),
            playerSchemeAgree: scheme.CardId === sample.selectedCardId,
            playerReferenceAgree: Boolean(reference && reference.CardId === sample.selectedCardId),
        };
    });

    const lines = [
        "# Offline trinket statistics comparison",
        "",
        `Generated: ${generatedAt.toISOString()}`,
        `Game build: ${active.GameBuild || "unknown"}`,
        `Reference snapshot: ${active.Stats.length} records; ${Number(active.TotalDataPoints || 0).toLocaleString("en-US")} total data points`,
        `Reference content SHA-256: ${active.ContentSha256 || "not-provided"}`,
        `Reliability threshold: ${MIN_REFERENCE_POINTS} data points per trinket`,
        "",
        "This report is offline-only. It does not alter production scoring, recommendations, or UI behavior.",
        "The reference snapshot is explicit caller-provided input and is not read from user caches.",
        "",
        "## Ranking agreement",
        "",
        "| Kind | Reliable records | Rated | Unrated | Spearman rho |",
        "|---|---:|---:|---:|---:|",
    ];

    for (const kind of ["lesser", "greater"]) {
        const rows = ranked.filter(row => row.kind === kind);
        const schemeRanks = new Map(rows.map(row => [row.cardId, row.schemeRank]));
        const referenceRanks = new Map(rows.map(row => [row.cardId, row.referenceRank]));
        const rho = correlation(rows, schemeRanks, referenceRanks);
        lines.push(`| ${kind} | ${rows.length} | ${rows.filter(row => row.rated).length} | ${rows.filter(row => !row.rated).length} | ${rho == null ? "n/a" : number(rho, 3)} |`);
    }

    lines.push("", "## Largest rank gaps", "");
    lines.push("| Trinket | Kind | Scheme rank | Reference rank | Rank gap | Placement | Pick rate | Data points |",
        "|---|---|---:|---:|---:|---:|---:|---:|");
    for (const row of ranked.slice().sort((a, b) => Math.abs(b.rankGap) - Math.abs(a.rankGap)).slice(0, 20)) {
        lines.push(`| ${cell(row.name)} | ${row.kind} | ${number(row.schemeRank, 1)} | ${number(row.referenceRank, 1)} | ${number(row.rankGap, 1)} | ${number(row.placement, 3)} | ${number(row.pickRate * 100, 1)}% | ${row.dataPoints} |`);
    }

    const count = key => shadowComparisons.filter(row => row[key]).length;
    lines.push("", "## Shadow offer comparison", "");
    lines.push(`- Eligible completed offers: ${shadowComparisons.length}`);
    lines.push(`- Local scheme and reference top choice agree: ${count("schemeReferenceAgree")}/${shadowComparisons.length}`);
    lines.push(`- Player and local scheme agree: ${count("playerSchemeAgree")}/${shadowComparisons.length}`);
    lines.push(`- Player and reference agree: ${count("playerReferenceAgree")}/${shadowComparisons.length}`);
    lines.push("");
    return lines.join("\n");
}

function run(argv) {
    const args = parseArgs(argv);
    const activePath = requireOption(args, "--active");
    const shadowPath = requireOption(args, "--shadow");
    const registryPath = requireOption(args, "--registry");
    const report = generateReport(
        readJson(activePath),
        readJson(registryPath),
        parseJsonLines(fs.readFileSync(shadowPath, "utf8")),
    );
    if (args["--output"]) {
        fs.writeFileSync(args["--output"], report, "utf8");
        process.stdout.write(`Trinket statistics comparison written: ${args["--output"]}\n`);
    } else {
        process.stdout.write(`${report}\n`);
    }
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
    generateReport,
    parseArgs,
    parseJsonLines,
    run,
    MIN_REFERENCE_POINTS,
};

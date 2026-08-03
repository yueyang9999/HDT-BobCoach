"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const { cardPoolIdentity, contractHeader } = require("../model/contracts");

const GENERATOR_NAME = "generate_trinket_registry";
const SUPPORTED_OPTIONS = new Set([
    "--input", "--output", "--season", "--game-build", "--card-pool",
    "--source", "--captured-at",
]);

// Contract header keys are emitted in this fixed order by tools/model/contracts.js.
const KNOWN_ROW_KEYS = ["cardId", "name_cn", "name_en", "cost", "effects", "tribes", "score", "text"];

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

function compareCardIds(left, right) {
    return Buffer.compare(Buffer.from(left, "utf8"), Buffer.from(right, "utf8"));
}

function normalizeRow(row, section) {
    if (!row || typeof row !== "object" || Array.isArray(row))
        throw new Error(`${section} row must be an object`);
    if (typeof row.cardId !== "string" || row.cardId.length === 0)
        throw new Error(`${section} row requires a non-empty cardId`);
    const normalized = {};
    for (const key of KNOWN_ROW_KEYS) {
        if (Object.prototype.hasOwnProperty.call(row, key) && row[key] !== undefined)
            normalized[key] = row[key];
    }
    const extra = Object.keys(row)
        .filter(key => !KNOWN_ROW_KEYS.includes(key))
        .sort();
    for (const key of extra) {
        if (row[key] !== undefined) normalized[key] = row[key];
    }
    return normalized;
}

function normalizeSection(section, rows) {
    if (!Array.isArray(rows)) throw new Error(`registry section ${section} must be an array`);
    return rows
        .map(row => normalizeRow(row, section))
        .sort((left, right) => compareCardIds(left.cardId, right.cardId));
}

function readCardPool(cardPoolPath) {
    const value = readJson(cardPoolPath);
    const cardIds = Array.isArray(value) ? value
        : Array.isArray(value && value.cardIds) ? value.cardIds
        : null;
    if (!cardIds) throw new Error("card pool must be a JSON array or an object with a cardIds array");
    return cardPoolIdentity(cardIds);
}

function computeContentDigest(lesser, greater) {
    const canonical = JSON.stringify({ lesser, greater });
    return crypto.createHash("sha256").update(canonical, "utf8").digest("hex");
}

function buildRegistry(input, options) {
    if (!input || typeof input !== "object" || Array.isArray(input))
        throw new Error("input must be a trinket registry JSON object");
    if (!options || typeof options !== "object")
        throw new Error("buildRegistry expects an options object");

    const lesser = normalizeSection("lesser", input.lesser);
    const greater = normalizeSection("greater", input.greater);
    const seen = new Set();
    for (const row of [...lesser, ...greater]) {
        if (seen.has(row.cardId)) throw new Error(`duplicate cardId in registry: ${row.cardId}`);
        seen.add(row.cardId);
    }

    const sha256 = computeContentDigest(lesser, greater);
    const capturedAt = options.capturedAt || new Date().toISOString();
    const source = options.source
        || (input._meta && typeof input._meta.source === "string" && input._meta.source)
        || "explicit-input";
    const provenance = {
        generator: GENERATOR_NAME,
        parameters: {
            season: options.season,
            gameBuild: options.gameBuild,
            cardPool: options.cardPoolPath || null,
            source,
        },
        input: {
            lastUpdated: input._meta && input._meta.last_updated,
            source: input._meta && input._meta.source,
            totalTrinkets: input._meta && input._meta.total_trinkets,
            lesser: lesser.length,
            greater: greater.length,
        },
    };

    const meta = contractHeader({
        season: options.season,
        gameBuild: options.gameBuild,
        cardPoolIdentity: options.cardPoolIdentity || "unknown",
        source,
        capturedAt,
        sha256,
        provenance,
    });
    return { _meta: meta, lesser, greater };
}

function run(argv) {
    const args = parseArgs(argv);
    const inputPath = requireOption(args, "--input");
    const outputPath = requireOption(args, "--output");
    const season = requireOption(args, "--season");
    const gameBuild = requireOption(args, "--game-build");
    const capturedAt = args["--captured-at"] || null;
    if (capturedAt && !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/.test(capturedAt))
        throw new Error("captured-at must be an ISO-8601 UTC timestamp");

    const input = readJson(inputPath);
    const cardPoolIdentityValue = args["--card-pool"]
        ? readCardPool(args["--card-pool"])
        : "unknown";
    const registry = buildRegistry(input, {
        season,
        gameBuild,
        source: args["--source"],
        capturedAt,
        cardPoolIdentity: cardPoolIdentityValue,
        cardPoolPath: args["--card-pool"] || null,
    });

    const output = `${JSON.stringify(registry, null, 2)}\n`;
    fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });
    fs.writeFileSync(outputPath, output, "utf8");
    process.stdout.write(
        `Trinket registry written: ${outputPath} `
        + `(total=${registry.lesser.length + registry.greater.length}, `
        + `sha256=${registry._meta.sha256}, `
        + `cardPoolIdentity=${registry._meta.cardPoolIdentity})\n`);
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
    buildRegistry,
    computeContentDigest,
    normalizeRow,
    parseArgs,
    readCardPool,
    run,
};

"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const { contractHeader } = require("../model/contracts");

const GENERATOR_NAME = "extract_official_season14_sources";
const SUPPORTED_OPTIONS = new Set([
    "--minion-updates",
    "--trinket-updates",
    "--spell-updates",
    "--dark-gifts",
    "--output-dir",
    "--season",
    "--game-build",
    "--card-pool-identity",
    "--source",
    "--captured-at",
]);

const STATUS_NAMES = new Set([
    "New",
    "Changed",
    "Returning",
    "Removed",
    "Changed & Returning",
    "Staying",
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

function decodeEntities(value) {
    return value
        .replace(/&nbsp;/gi, " ")
        .replace(/&hellip;/gi, "...")
        .replace(/&ldquo;/gi, "\u201c")
        .replace(/&rdquo;/gi, "\u201d")
        .replace(/&lsquo;/gi, "\u2018")
        .replace(/&rsquo;/gi, "\u2019")
        .replace(/&mdash;/gi, "\u2014")
        .replace(/&ndash;/gi, "\u2013")
        .replace(/&amp;/gi, "&")
        .replace(/&lt;/gi, "<")
        .replace(/&gt;/gi, ">")
        .replace(/&quot;/gi, "\"")
        .replace(/&#39;/gi, "'")
        .replace(/&#x27;/gi, "'")
        .replace(/&#x2019;/gi, "\u2019");
}

function stripHtml(value) {
    return decodeEntities(value)
        .replace(/<script[\s\S]*?<\/script>/gi, " ")
        .replace(/<style[\s\S]*?<\/style>/gi, " ")
        .replace(/<[^>]+>/g, " ")
        .replace(/\u00a0/g, " ")
        .replace(/\s+/g, " ")
        .trim();
}

function parseTableRows(tableHtml) {
    const rows = [];
    const rowMatches = tableHtml.matchAll(/<tr[\s\S]*?<\/tr>/gi);
    for (const rowMatch of rowMatches) {
        const cells = [];
        const cellMatches = rowMatch[0].matchAll(/<t[dh][^>]*>([\s\S]*?)<\/t[dh]>/gi);
        for (const cellMatch of cellMatches) {
            cells.push(stripHtml(cellMatch[1]));
        }
        if (cells.length > 0) rows.push(cells);
    }
    return rows;
}

function parseTable(tableHtml) {
    const rows = parseTableRows(tableHtml);
    if (rows.length === 0) return { headers: [], rows: [] };
    return {
        headers: rows[0],
        rows: rows.slice(1),
    };
}

function parseTables(html) {
    const tables = [];
    const tableMatches = html.matchAll(/<table[\s\S]*?<\/table>/gi);
    for (const match of tableMatches) tables.push(parseTable(match[0]));
    return tables;
}

function extractDetails(html) {
    const details = [];
    const detailMatches = html.matchAll(/<details\b[^>]*>([\s\S]*?)<\/details>/gi);
    for (const match of detailMatches) {
        const inner = match[1];
        const summaryMatch = inner.match(/<summary\b[^>]*>([\s\S]*?)<\/summary>/i);
        const summary = summaryMatch ? stripHtml(summaryMatch[1]) : "";
        const content = summaryMatch ? inner.replace(summaryMatch[0], "") : inner;
        details.push({
            summary,
            content,
            tables: parseTables(content),
            listItems: [...content.matchAll(/<li[^>]*>([\s\S]*?)<\/li>/gi)]
                .map(itemMatch => stripHtml(itemMatch[1])),
        });
    }
    return details;
}

function isHeaderRow(row, index) {
    if (index !== 0) return false;
    const first = (row[0] || "").toLowerCase();
    return ["minion", "trinket", "spell", "name", "turn", "cost", "effect", "tier"].some(
        header => first === header || first.startsWith(header + " ")
    );
}

function normalizeStatus(value) {
    const normalized = value
        .replace(/\s+/g, " ")
        .replace(/\s*&\s*/g, " & ")
        .trim();
    return STATUS_NAMES.has(normalized) ? normalized : null;
}

function isStatusRow(row) {
    const first = row[0] || "";
    const status = normalizeStatus(first);
    return status !== null && row.slice(1).every(cell => cell === "");
}

function extractSequencedRows(table) {
    const rows = [];
    let currentStatus = null;
    for (let index = 0; index < table.rows.length; index += 1) {
        const row = table.rows[index];
        if (isHeaderRow(row, index)) continue;
        if (isStatusRow(row)) {
            currentStatus = normalizeStatus(row[0]);
            continue;
        }
        if ((row[0] || "").trim() === "") continue;
        rows.push({
            status: currentStatus,
            values: row,
        });
    }
    return rows;
}

function cardNameFromOfficialName(name) {
    const trimmed = name.trim();
    return {
        name: trimmed,
        cardName: trimmed.replace(/^Golden\s+/i, ""),
        golden: /^Golden\s+/i.test(trimmed),
    };
}

function buildMinionPayload(html, topic) {
    const details = extractDetails(html);
    const sections = details.map(detail => {
        const table = detail.tables[0] || { headers: [], rows: [] };
        return {
            section: detail.summary,
            headers: table.headers,
            rows: extractSequencedRows(table).map(({ status, values }) => ({
                status,
                ...cardNameFromOfficialName(values[0] || ""),
                tier: values[1] || "",
                attack: values[2] || "",
                health: values[3] || "",
                effect: values[4] || "",
            })),
        };
    });

    const counts = {};
    for (const section of sections) {
        counts[section.section] = section.rows.length;
    }

    return {
        kind: "officialMinionUpdates",
        topic,
        sections,
        counts,
    };
}

function parseTrinketSectionName(summary) {
    const match = summary.match(/^(Lesser|Greater)\s+Trinkets?:\s*(New|Returning|Removed|Staying)(?:\s*\(Part\s*(\d+)\))?$/i);
    if (!match) {
        return {
            trinketType: "",
            status: summary,
            part: null,
        };
    }
    return {
        trinketType: match[1],
        status: match[2],
        part: match[3] ? Number(match[3]) : null,
    };
}

function buildTrinketPayload(html, topic) {
    const details = extractDetails(html);
    const sections = details.map(detail => {
        const table = detail.tables[0] || { headers: [], rows: [] };
        return {
            section: detail.summary,
            ...parseTrinketSectionName(detail.summary),
            headers: table.headers,
            rows: extractSequencedRows(table).map(({ status, values }) => ({
                status,
                ...cardNameFromOfficialName(values[0] || ""),
                cost: values[1] || "",
                effect: values[2] || "",
            })),
        };
    });

    const counts = {};
    for (const section of sections) counts[section.section] = section.rows.length;

    return {
        kind: "officialTrinketUpdates",
        topic,
        sections,
        counts,
    };
}

function buildSpellPayload(html, topic) {
    const details = extractDetails(html);
    const sections = details.map(detail => {
        const table = detail.tables[0] || { headers: [], rows: [] };
        return {
            section: detail.summary,
            headers: table.headers,
            rows: extractSequencedRows(table).map(({ status, values }) => ({
                status,
                ...cardNameFromOfficialName(values[0] || ""),
                cost: values[1] || "",
                tier: values[2] || "",
                effect: values[3] || "",
            })),
        };
    });

    const counts = {};
    for (const section of sections) counts[section.section] = section.rows.length;

    return {
        kind: "officialSpellUpdates",
        topic,
        sections,
        counts,
    };
}

function buildDarkGiftPayload(html, topic) {
    const tables = parseTables(html);
    const offeringTable = tables.find(table =>
        table.headers.length >= 2
        && /turn/i.test(table.headers[0])
        && /minion tier/i.test(table.headers[1])
    ) || { headers: [], rows: [] };
    const giftTable = tables.find(table =>
        table.headers.length >= 3
        && /name/i.test(table.headers[0])
        && /minimum turn/i.test(table.headers[1])
        && /maximum turn/i.test(table.headers[2])
    ) || { headers: [], rows: [] };
    const details = extractDetails(html);
    const rules = details.map(detail => ({
        title: detail.summary,
        items: detail.listItems,
    }));

    return {
        kind: "officialDarkGiftRegistry",
        topic,
        offeringTable: {
            headers: offeringTable.headers,
            rows: offeringTable.rows.map((row, index) => ({
                turn: row[0] || "",
                minionTier: row[1] || "",
                sourceRow: index + 2,
            })),
        },
        gifts: giftTable.rows.map((row, index) => ({
            name: row[0] || "",
            minimumTurn: row[1] || "",
            maximumTurn: row[2] || "",
            effect: row[3] || "",
            sourceRow: index + 2,
        })),
        rules,
        counts: {
            offeringRows: offeringTable.rows.length,
            giftRows: giftTable.rows.length,
        },
    };
}

function topicMetadata(rawJson, sourcePath) {
    const post = rawJson.post_stream && rawJson.post_stream.posts
        && rawJson.post_stream.posts[0];
    return {
        id: rawJson.id || null,
        title: rawJson.title || "",
        slug: rawJson.slug || "",
        createdAt: rawJson.created_at || null,
        updatedAt: rawJson.updated_at || null,
        postUrl: post && post.post_url ? post.post_url : "",
        postNumber: post && post.post_number !== undefined ? post.post_number : null,
        sourceFile: path.basename(sourcePath),
        sourceFileSha256: sha256File(sourcePath),
    };
}

function contentDigest(payload) {
    return crypto.createHash("sha256")
        .update(JSON.stringify(payload), "utf8")
        .digest("hex");
}

function buildDocument(rawPath, buildPayload, options) {
    const rawJson = readJson(rawPath);
    const topic = topicMetadata(rawJson, rawPath);
    const payload = buildPayload(rawJson.post_stream.posts[0].cooked, topic);
    const sha256 = contentDigest(payload);
    const capturedAt = options.capturedAt || new Date().toISOString();
    const source = options.source || topic.slug;
    const provenance = {
        generator: GENERATOR_NAME,
        topic: {
            id: topic.id,
            slug: topic.slug,
            postUrl: topic.postUrl,
        },
        parameters: {
            season: options.season,
            gameBuild: options.gameBuild,
            cardPoolIdentity: options.cardPoolIdentity || "unknown",
            source,
        },
        rawFileSha256: topic.sourceFileSha256,
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
    return { _meta: meta, ...payload };
}

function writeDocument(document, outputPath) {
    fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });
    fs.writeFileSync(outputPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");
}

function run(argv) {
    const args = parseArgs(argv);
    const outputDir = requireOption(args, "--output-dir");
    const season = requireOption(args, "--season");
    const gameBuild = requireOption(args, "--game-build");
    const capturedAt = args["--captured-at"] || null;
    if (capturedAt && !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/.test(capturedAt))
        throw new Error("captured-at must be an ISO-8601 UTC timestamp");

    const options = {
        season,
        gameBuild,
        cardPoolIdentity: args["--card-pool-identity"],
        source: args["--source"],
        capturedAt,
    };

    const jobs = [
        {
            option: "--minion-updates",
            output: "official_minion_updates.json",
            build: buildMinionPayload,
        },
        {
            option: "--trinket-updates",
            output: "official_trinket_updates.json",
            build: buildTrinketPayload,
        },
        {
            option: "--spell-updates",
            output: "official_spell_updates.json",
            build: buildSpellPayload,
        },
        {
            option: "--dark-gifts",
            output: "official_dark_gift_registry.json",
            build: buildDarkGiftPayload,
        },
    ];

    const written = [];
    for (const job of jobs) {
        const rawPath = args[job.option];
        if (!rawPath) continue;
        const document = buildDocument(rawPath, job.build, options);
        const outputPath = path.join(outputDir, job.output);
        writeDocument(document, outputPath);
        written.push({
            output: outputPath,
            sha256: document._meta.sha256,
            counts: document.counts || {},
        });
    }

    for (const item of written) {
        process.stdout.write(
            `Official source extracted: ${item.output} (sha256=${item.sha256})\n`);
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
    GENERATOR_NAME,
    buildDarkGiftPayload,
    buildMinionPayload,
    buildSpellPayload,
    buildTrinketPayload,
    contentDigest,
    extractDetails,
    parseArgs,
    parseTable,
    parseTableRows,
    parseTrinketSectionName,
    run,
    stripHtml,
};

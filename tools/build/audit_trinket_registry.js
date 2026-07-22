"use strict";

const fs = require("fs");

const SUPPORTED_OPTIONS = new Set(["--registry", "--authority", "--output"]);

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

function buildAuditReport(registry, authorityInput, generatedAt = new Date()) {
    const authorityRows = Array.isArray(authorityInput)
        ? authorityInput
        : Array.isArray(authorityInput && authorityInput.cards) ? authorityInput.cards : [];
    const authority = authorityRows.filter(card => card && card.type === "BATTLEGROUND_TRINKET");
    const authorityById = new Map(authority.map(card => [card.id, card]));
    const sections = ["lesser", "greater"];
    const registryRows = sections.flatMap(section => (Array.isArray(registry && registry[section])
        ? registry[section].map(row => ({ ...row, section })) : []));
    const registryById = new Map(registryRows.map(row => [row.cardId, row]));

    const orphans = registryRows.filter(row => !authorityById.has(row.cardId));
    const missing = authority.filter(card => !registryById.has(card.id));
    const classMismatches = registryRows.flatMap(row => {
        const authorityCard = authorityById.get(row.cardId);
        if (!authorityCard) return [];
        const expectedSection = authorityCard.spellSchool === "LESSER_TRINKET" ? "lesser" : "greater";
        return expectedSection === row.section ? [] : [{
            cardId: row.cardId,
            registrySection: row.section,
            authoritySection: authorityCard.spellSchool,
        }];
    });
    const rated = registryRows.filter(row => row.unrated !== true).length;

    return {
        generatedAt: generatedAt.toISOString(),
        authority: {
            source: "explicit-input",
            totalTrinkets: authority.length,
            lesser: authority.filter(card => card.spellSchool === "LESSER_TRINKET").length,
            greater: authority.filter(card => card.spellSchool !== "LESSER_TRINKET").length,
        },
        registry: {
            total: registryRows.length,
            lesser: registryRows.filter(row => row.section === "lesser").length,
            greater: registryRows.filter(row => row.section === "greater").length,
        },
        diff: {
            orphanCount: orphans.length,
            orphans: orphans.map(row => ({ cardId: row.cardId, section: row.section })),
            missingCount: missing.length,
            missing: missing.map(card => ({ cardId: card.id, spellSchool: card.spellSchool })),
            classMismatchCount: classMismatches.length,
            classMismatches,
        },
        classification: {
            rated,
            unrated: registryRows.length - rated,
        },
    };
}

function run(argv) {
    const args = parseArgs(argv);
    const registryPath = requireOption(args, "--registry");
    const authorityPath = requireOption(args, "--authority");
    const report = buildAuditReport(readJson(registryPath), readJson(authorityPath));
    const output = `${JSON.stringify(report, null, 2)}\n`;
    if (args["--output"]) {
        fs.writeFileSync(args["--output"], output, "utf8");
        process.stdout.write(`Trinket registry audit written: ${args["--output"]}\n`);
    } else {
        process.stdout.write(output);
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

module.exports = { buildAuditReport, parseArgs, run };

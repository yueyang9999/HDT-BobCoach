"use strict";

const assert = require("assert");
const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const repositoryRoot = path.resolve(__dirname, "..");
const auditTool = path.join(repositoryRoot, "tools", "build", "audit_trinket_registry.js");
const comparisonTool = path.join(repositoryRoot, "tools", "build", "generate_trinket_stats_diff_report.js");
const generatorTool = path.join(repositoryRoot, "tools", "build", "generate_trinket_registry.js");
const tempPrefix = "bobcoach-offline-trinket-audit-test-";

function run(script, args, env = {}) {
    return childProcess.spawnSync(process.execPath, [script, ...args], {
        cwd: repositoryRoot,
        encoding: "utf8",
        env: { ...process.env, ...env },
    });
}

function writeJson(root, name, value) {
    const file = path.join(root, name);
    fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`, "utf8");
    return file;
}

function removeTemp(root) {
    const tempRoot = path.resolve(os.tmpdir()) + path.sep;
    const resolved = path.resolve(root);
    assert.ok(resolved.startsWith(tempRoot), `unsafe temporary cleanup path: ${resolved}`);
    assert.ok(path.basename(resolved).startsWith(tempPrefix), `unexpected temporary directory: ${resolved}`);
    fs.rmSync(resolved, { recursive: true, force: true });
}

const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), tempPrefix));
try {
    const appData = path.join(tempRoot, "synthetic-appdata");
    fs.mkdirSync(path.join(appData, "bob-coach", "data", "trinket-stats"), { recursive: true });
    writeJson(path.join(appData, "bob-coach", "data", "trinket-stats"), "active.json", {
        StatusReason: "verified",
        Stats: [],
    });

    const noArgsComparison = run(comparisonTool, [], { APPDATA: appData });
    assert.notStrictEqual(noArgsComparison.status, 0, "comparison tool accepted implicit user-cache inputs");
    assert.match(noArgsComparison.stderr, /required option: --active/i);
    assert.ok(!`${noArgsComparison.stdout}\n${noArgsComparison.stderr}`.includes(appData),
        "comparison tool disclosed or attempted the implicit user-cache path");

    const noArgsAudit = run(auditTool, []);
    assert.notStrictEqual(noArgsAudit.status, 0, "registry audit accepted implicit repository inputs");
    assert.match(noArgsAudit.stderr, /required option: --registry/i);
    assert.doesNotMatch(`${noArgsAudit.stdout}\n${noArgsAudit.stderr}`, /tools[\\/]data[\\/]trinket_registry\.json/i);

    const noArgsGenerator = run(generatorTool, []);
    assert.notStrictEqual(noArgsGenerator.status, 0, "registry generator accepted implicit inputs");
    assert.match(noArgsGenerator.stderr, /required option: --input/i);

    const registryPath = writeJson(tempRoot, "registry.json", {
        lesser: [{ cardId: "TRINKET_SYNTH_001", name_cn: "Synthetic Lesser", score: 4, text: "Gain Gold" }],
        greater: [],
    });
    const authorityPath = writeJson(tempRoot, "authority.json", [{
        id: "TRINKET_SYNTH_001",
        name: "Synthetic Lesser",
        type: "BATTLEGROUND_TRINKET",
        spellSchool: "LESSER_TRINKET",
    }]);
    const activePath = writeJson(tempRoot, "active.json", {
        StatusReason: "verified",
        GameBuild: "synthetic-build",
        LastUpdateDateUtc: "2026-07-22T00:00:00Z",
        TotalDataPoints: 1200,
        ContentSha256: "synthetic-content-hash",
        Stats: [{
            TrinketCardId: "TRINKET_SYNTH_001",
            AveragePlacement: 3.5,
            PickRate: 0.25,
            DataPoints: 1200,
        }],
    });
    const shadowPath = path.join(tempRoot, "shadow.jsonl");
    fs.writeFileSync(shadowPath, "", "utf8");

    const audit = run(auditTool, ["--registry", registryPath, "--authority", authorityPath]);
    assert.strictEqual(audit.status, 0, audit.stderr);
    const auditReport = JSON.parse(audit.stdout);
    assert.strictEqual(auditReport.authority.totalTrinkets, 1);
    assert.strictEqual(auditReport.registry.total, 1);
    assert.strictEqual(auditReport.diff.orphanCount, 0);
    assert.strictEqual(auditReport.diff.missingCount, 0);

    const comparison = run(comparisonTool, [
        "--active", activePath,
        "--shadow", shadowPath,
        "--registry", registryPath,
    ]);
    assert.strictEqual(comparison.status, 0, comparison.stderr);
    assert.match(comparison.stdout, /^# Offline trinket statistics comparison/m);
    assert.match(comparison.stdout, /Reference snapshot: 1 records/);

    const outputPath = path.join(tempRoot, "comparison.md");
    const comparisonToFile = run(comparisonTool, [
        "--active", activePath,
        "--shadow", shadowPath,
        "--registry", registryPath,
        "--output", outputPath,
    ]);
    assert.strictEqual(comparisonToFile.status, 0, comparisonToFile.stderr);
    assert.match(comparisonToFile.stdout, /written:/i);
    assert.match(fs.readFileSync(outputPath, "utf8"), /^# Offline trinket statistics comparison/m);

    const generatorInputPath = writeJson(tempRoot, "generator-input.json", {
        _meta: { last_updated: "2026-07-10", source: "synthetic-source", total_trinkets: 2 },
        lesser: [
            {
                cardId: "TRINKET_SYNTH_002",
                name_cn: "合成小饰品乙",
                name_en: "Synthetic Lesser Two",
                cost: 4,
                effects: [{ type: "gold", subtype: "per_turn", value: 1 }],
                tribes: ["Neutral"],
                score: 4,
                text: "At the start of your turn, gain 1 Gold.",
            },
            {
                cardId: "TRINKET_SYNTH_001",
                name_cn: "合成小饰品甲",
                name_en: "Synthetic Lesser One",
                cost: 3,
                effects: [{ type: "stats", subtype: "divine_shield", value: 1 }],
                tribes: ["Beast"],
                score: 5,
                text: "Give your Beasts Divine Shield.",
            },
        ],
        greater: [
            {
                cardId: "TRINKET_SYNTH_003",
                name_cn: "合成大饰品",
                name_en: "Synthetic Greater",
                cost: 5,
                effects: [{ type: "gold", subtype: "discount", value: 2 }],
                tribes: [],
                score: 6,
                text: "Your Tavern spells cost 2 less.",
            },
        ],
    });
    const cardPoolPath = writeJson(tempRoot, "generator-card-pool.json",
        ["TRINKET_SYNTH_003", "TRINKET_SYNTH_001", "TRINKET_SYNTH_002", "TRINKET_SYNTH_001"]);
    const generatorOutputPath = path.join(tempRoot, "generated", "trinket_registry.json");
    const generatorArgs = [
        "--input", generatorInputPath,
        "--output", generatorOutputPath,
        "--season", "35.6",
        "--game-build", "35.6.2.244584",
        "--card-pool", cardPoolPath,
        "--captured-at", "2026-08-03T00:00:00Z",
    ];
    const generated = run(generatorTool, generatorArgs);
    assert.strictEqual(generated.status, 0, generated.stderr);
    assert.match(generated.stdout, /Trinket registry written:/);
    const registryOutput = JSON.parse(fs.readFileSync(generatorOutputPath, "utf8"));
    assert.deepStrictEqual(Object.keys(registryOutput._meta), [
        "schemaVersion", "season", "gameBuild", "cardPoolIdentity",
        "source", "capturedAt", "sha256", "provenance",
    ]);
    assert.strictEqual(registryOutput._meta.schemaVersion, 1);
    assert.strictEqual(registryOutput._meta.season, "35.6");
    assert.strictEqual(registryOutput._meta.gameBuild, "35.6.2.244584");
    assert.strictEqual(registryOutput._meta.source, "synthetic-source");
    assert.strictEqual(registryOutput._meta.capturedAt, "2026-08-03T00:00:00Z");
    assert.match(registryOutput._meta.sha256, /^[a-f0-9]{64}$/);
    const cardPoolContract = require(path.join(repositoryRoot, "tools", "model", "contracts"));
    assert.strictEqual(registryOutput._meta.cardPoolIdentity,
        cardPoolContract.cardPoolIdentity(["TRINKET_SYNTH_001", "TRINKET_SYNTH_002", "TRINKET_SYNTH_003"]),
        "generator must compute cardPoolIdentity from the explicit card pool set");
    assert.strictEqual(registryOutput.lesser.length, 2);
    assert.strictEqual(registryOutput.greater.length, 1);
    assert.deepStrictEqual(registryOutput.lesser.map(row => row.cardId),
        ["TRINKET_SYNTH_001", "TRINKET_SYNTH_002"],
        "lesser rows must be canonicalized and sorted by cardId");
    assert.strictEqual(registryOutput.lesser[0].effects[0].subtype, "divine_shield");
    assert.strictEqual(registryOutput.lesser[1].effects[0].type, "gold");
    const canonicalDigest = require("crypto").createHash("sha256")
        .update(JSON.stringify({
            lesser: registryOutput.lesser,
            greater: registryOutput.greater,
        }))
        .digest("hex");
    assert.strictEqual(registryOutput._meta.sha256, canonicalDigest,
        "sha256 must be the canonical serialization digest of the normalized content");

    const regenerated = run(generatorTool, generatorArgs);
    assert.strictEqual(regenerated.status, 0, regenerated.stderr);
    assert.deepStrictEqual(
        fs.readFileSync(generatorOutputPath, "utf8"),
        fs.readFileSync(path.join(tempRoot, "generated", "trinket_registry.json"), "utf8"),
        "generator output must be byte-for-byte deterministic for identical explicit inputs");

    const noCardPoolOutputPath = path.join(tempRoot, "generated", "trinket_registry-unknown-pool.json");
    const withoutCardPool = run(generatorTool, [
        "--input", generatorInputPath,
        "--output", noCardPoolOutputPath,
        "--season", "35.6",
        "--game-build", "35.6.2.244584",
        "--captured-at", "2026-08-03T00:00:00Z",
    ]);
    assert.strictEqual(withoutCardPool.status, 0, withoutCardPool.stderr);
    const unknownPoolOutput = JSON.parse(fs.readFileSync(noCardPoolOutputPath, "utf8"));
    assert.strictEqual(unknownPoolOutput._meta.cardPoolIdentity, "unknown",
        "missing card pool must explicitly degrade to unknown, never fabricated");

    const missingSeason = run(generatorTool, [
        "--input", generatorInputPath,
        "--output", path.join(tempRoot, "generated", "no-season.json"),
        "--game-build", "35.6.2.244584",
    ]);
    assert.notStrictEqual(missingSeason.status, 0, "generator accepted a missing season");
    assert.match(missingSeason.stderr, /required option: --season/i);

    const badCapturedAt = run(generatorTool, [
        "--input", generatorInputPath,
        "--output", path.join(tempRoot, "generated", "bad-captured-at.json"),
        "--season", "35.6",
        "--game-build", "35.6.2.244584",
        "--captured-at", "2026-08-03",
    ]);
    assert.notStrictEqual(badCapturedAt.status, 0, "generator accepted a non-UTC captured-at");
    assert.match(badCapturedAt.stderr, /ISO-8601 UTC/);

    const duplicateRegistryPath = writeJson(tempRoot, "generator-duplicate-input.json", {
        _meta: {},
        lesser: [{ cardId: "TRINKET_DUP_001" }],
        greater: [{ cardId: "TRINKET_DUP_001" }],
    });
    const duplicateId = run(generatorTool, [
        "--input", duplicateRegistryPath,
        "--output", path.join(tempRoot, "generated", "duplicate.json"),
        "--season", "35.6",
        "--game-build", "35.6.2.244584",
    ]);
    assert.notStrictEqual(duplicateId.status, 0, "generator accepted a duplicate cardId across sections");
    assert.match(duplicateId.stderr, /duplicate cardId/i);
} finally {
    removeTemp(tempRoot);
}

console.log("PASS offline trinket audit tools require explicit synthetic inputs");

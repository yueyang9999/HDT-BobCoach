"use strict";

const assert = require("assert");
const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const repositoryRoot = path.resolve(__dirname, "..");
const auditTool = path.join(repositoryRoot, "tools", "build", "audit_trinket_registry.js");
const comparisonTool = path.join(repositoryRoot, "tools", "build", "generate_trinket_stats_diff_report.js");
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
} finally {
    removeTemp(tempRoot);
}

console.log("PASS offline trinket audit tools require explicit synthetic inputs");

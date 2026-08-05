"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const EVIDENCE_ROOT = "E:/HDT-BobCoach-evidence/season14";
const FETCH_TOOL = require("E:/HDT-BobCoach-evidence/season14/tools/fetch_season14_comp_guides.js");

const DEFAULT_OUTPUT_DIR = path.join(EVIDENCE_ROOT, "generated", "refresh_reports");
const DEFAULT_GUIDE_SOURCE = path.join(EVIDENCE_ROOT, "generated", "season14_guide_source.json");
const DEFAULT_GUIDES_REGISTRY = path.join(
  EVIDENCE_ROOT,
  "generated",
  "registries",
  "season14_comp_guides.json"
);
const DEFAULT_WEIGHT_REFERENCE = path.join(
  EVIDENCE_ROOT,
  "generated",
  "season14_guide_weight_reference.json"
);
const DEFAULT_OFFICIAL_CARDS = path.join(EVIDENCE_ROOT, "raw", "cards.zhCN.latest.json");
const DEFAULT_ALIASES = path.join(EVIDENCE_ROOT, "generated", "guide_name_aliases.json");

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function writeJson(file, payload) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
}

function contentDigest(payload) {
  return crypto.createHash("sha256")
    .update(JSON.stringify(payload), "utf8")
    .digest("hex");
}

function containsDuo(value) {
  if (typeof value === "string") {
    return value.toLowerCase().indexOf("bgduo") >= 0;
  }
  if (Array.isArray(value)) {
    return value.some(containsDuo);
  }
  if (value && typeof value === "object") {
    return Object.keys(value).some(key => containsDuo(value[key]));
  }
  return false;
}

async function resolvePayload(options) {
  if (options.payload !== undefined) return options.payload;
  if (options.localPath) return readJson(options.localPath);
  if (options.sourceUrl) {
    const response = await fetch(options.sourceUrl, {
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      throw new Error(`fetch ${options.sourceUrl} failed with HTTP ${response.status}`);
    }
    return await response.json();
  }
  throw new Error("payload, localPath, or sourceUrl is required");
}

function buildRefreshReport(options) {
  return {
    generatedAt: options.generatedAt,
    season: "season14",
    kind: "season14GuideRefreshReport",
    source: options.sourceName,
    status: options.status,
    blockReason: options.blockReason || null,
    applied: options.applied,
    rawCount: options.rawCount,
    guidesCount: options.guidesCount,
    validationCounts: options.validationCounts || null,
    sha256: options.sha256,
    fetchReportPath: options.fetchReportPath || null,
    refreshReportPath: options.refreshReportPath,
    guidesRegistryPath: options.guidesRegistryPath,
    weightReferencePath: options.weightReferencePath,
  };
}

function normalizeCounts(payload) {
  try {
    const normalized = FETCH_TOOL.normalizeGuides(payload);
    return { rawCount: normalized.rawCount, guidesCount: normalized.guides.length };
  } catch (error) {
    return { rawCount: 0, guidesCount: 0 };
  }
}

async function runRefresh(options) {
  const outputDir = options.outputDir || DEFAULT_OUTPUT_DIR;
  const guidesRegistryPath = options.guidesRegistryPath || DEFAULT_GUIDES_REGISTRY;
  const weightReferencePath = options.weightReferencePath || DEFAULT_WEIGHT_REFERENCE;
  const guideSourcePath = options.guideSourcePath || DEFAULT_GUIDE_SOURCE;
  const officialCardsPath = options.officialCardsPath || DEFAULT_OFFICIAL_CARDS;
  const aliasesPath = options.aliasesPath || DEFAULT_ALIASES;
  const sourceName = options.sourceName || options.sourceUrl || "season14-guide-refresh";
  const refreshReportPath = path.join(outputDir, "season14_guide_refresh_report.json");

  const payload = await resolvePayload(options);
  const sha256 = contentDigest(payload);
  const counts = normalizeCounts(payload);

  if (containsDuo(payload)) {
    const report = buildRefreshReport({
      generatedAt: new Date().toISOString(),
      sourceName,
      status: "blocked",
      blockReason: "duo_forbidden",
      applied: false,
      rawCount: counts.rawCount,
      guidesCount: counts.guidesCount,
      validationCounts: null,
      sha256,
      fetchReportPath: null,
      refreshReportPath,
      guidesRegistryPath,
      weightReferencePath,
    });
    writeJson(refreshReportPath, report);
    return {
      ok: false,
      blocked: true,
      blockReason: "duo_forbidden",
      applied: false,
      rawCount: counts.rawCount,
      guidesCount: counts.guidesCount,
      sha256,
      refreshReportPath,
      fetchReportPath: null,
      guidesRegistryPath,
      weightReferencePath,
    };
  }

  const result = await FETCH_TOOL.runFetch({
    ...options,
    sourceName,
    outputDir,
    guidesRegistryPath,
    weightReferencePath,
    guideSourcePath,
    officialCardsPath,
    aliasesPath,
    payload,
  });

  const report = buildRefreshReport({
    generatedAt: new Date().toISOString(),
    sourceName,
    status: result.ok ? (result.applied ? "applied" : "ok") : "blocked",
    blockReason: result.blockReason,
    applied: result.applied,
    rawCount: result.rawCount,
    guidesCount: result.guidesCount,
    validationCounts: result.validationCounts || null,
    sha256,
    fetchReportPath: result.fetchReportPath || null,
    refreshReportPath,
    guidesRegistryPath,
    weightReferencePath,
  });
  writeJson(refreshReportPath, report);

  return {
    ...result,
    sha256,
    refreshReportPath,
  };
}

function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const value = argv[index + 1];
    switch (arg) {
      case "--local":
        options.localPath = value;
        index += 1;
        break;
      case "--source":
        options.sourceUrl = value;
        index += 1;
        break;
      case "--source-name":
        options.sourceName = value;
        index += 1;
        break;
      case "--output-dir":
        options.outputDir = value;
        index += 1;
        break;
      case "--guide-source":
        options.guideSourcePath = value;
        index += 1;
        break;
      case "--official-cards":
        options.officialCardsPath = value;
        index += 1;
        break;
      case "--aliases":
        options.aliasesPath = value;
        index += 1;
        break;
      case "--guides-registry":
        options.guidesRegistryPath = value;
        index += 1;
        break;
      case "--weight-reference":
        options.weightReferencePath = value;
        index += 1;
        break;
      case "--apply":
        options.apply = true;
        break;
      case "--help":
      case "-h":
        options.help = true;
        break;
      default:
        throw new Error(`unknown argument ${arg}`);
    }
  }
  return options;
}

function printUsage() {
  process.stdout.write([
    "Usage: node season14_guide_refresh.js --local <json> [--apply]",
    "       node season14_guide_refresh.js --source <url> [--apply]",
    "",
    "Options:",
    "  --local <json>          read a local guide payload",
    "  --source <url>          fetch a guide payload from an endpoint",
    "  --source-name <name>    provenance label",
    "  --output-dir <dir>      refresh reports and fetch artifacts output",
    "  --guide-source <file>   guide source used for sync report input",
    "  --official-cards <file> official zhCN card JSON",
    "  --aliases <file>        alias JSON with confirmed/pending sections",
    "  --guides-registry <file> formal registry; only written with --apply",
    "  --weight-reference <file> weight reference; only written with --apply",
    "  --apply                 write formal registry and weight reference after green validation",
    "  --help                  show this help",
  ].join("\n") + "\n");
}

async function main(argv) {
  const options = parseArgs(argv);
  if (options.help) {
    printUsage();
    return;
  }
  if (!options.localPath && !options.sourceUrl) {
    throw new Error("--local or --source is required");
  }

  const result = await runRefresh(options);
  process.stdout.write(
    `season14_guide_refresh ok=${result.ok} blocked=${result.blockReason || "none"} `
    + `applied=${result.applied} raw=${result.rawCount} guides=${result.guidesCount} `
    + `sha256=${result.sha256} refreshReport=${result.refreshReportPath}\n`
  );
  if (result.validationCounts) {
    process.stdout.write(`name_validation ${JSON.stringify(result.validationCounts)}\n`);
  }
  if (!result.ok) process.exitCode = 1;
}

if (require.main === module) {
  main(process.argv.slice(2)).catch((error) => {
    process.stderr.write(`Error: ${error.message}\n`);
    process.exitCode = 1;
  });
}

module.exports = {
  containsDuo,
  runRefresh,
};

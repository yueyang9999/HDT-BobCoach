"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const EVIDENCE_ROOT = "E:/HDT-BobCoach-evidence/season14";
const WEIGHT_REFERENCE = path.join(
  EVIDENCE_ROOT,
  "generated",
  "season14_guide_weight_reference.json"
);
const COMP_GUIDES = path.join(
  EVIDENCE_ROOT,
  "generated",
  "registries",
  "season14_comp_guides.json"
);
const NAME_VALIDATION_REPORT = path.join(
  EVIDENCE_ROOT,
  "generated",
  "guide_name_validation_report.json"
);
const WEIGHT_TOOL = path.join(
  EVIDENCE_ROOT,
  "tools",
  "build_season14_guide_weight_reference.js"
);

const EXPECTED_COUNTS = {
  guides: 23,
  coreEntries: 138,
  trinketEntries: 69,
  uniqueCoreNames: 81,
  uniqueTrinketNames: 55,
  multiCopyCoreNames: 10,
};

const EXPECTED_TIERS = {
  T0: 4,
  T1: 8,
  T2: 10,
  T3: 1,
};

const TIER_SCORE = {
  T0: 3,
  T1: 2,
  T2: 1,
  T3: 0,
};

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function containsBgDuo(value) {
  if (typeof value === "string") return /BGDUO/i.test(value);
  if (Array.isArray(value)) return value.some(containsBgDuo);
  if (value && typeof value === "object") {
    return Object.keys(value).some(
      key => (/BGDUO/i.test(key) && key.toLowerCase() !== "nobgduo")
          || containsBgDuo(value[key])
    );
  }
  return false;
}

function validateEntries(reference, label, map) {
  assert.ok(map && typeof map === "object" && !Array.isArray(map),
    `${label} must be an object map`);
  for (const [name, item] of Object.entries(map)) {
    assert.equal(item.name, name, `${label} ${name} should use its key as name`);
    assert.equal(typeof item.rawWeight, "number", `${label} ${name} rawWeight must be numeric`);
    assert.equal(typeof item.heuristicWeight, "number",
      `${label} ${name} heuristicWeight must be numeric`);
    assert.equal(typeof item.duplicateBonus, "number",
      `${label} ${name} duplicateBonus must be numeric`);
    assert.ok(item.duplicateBonus >= 0, `${label} ${name} duplicateBonus must be non-negative`);
    assert.ok(item.officialName, `${label} ${name} must resolve officialName`);
    assert.ok(item.officialCardId, `${label} ${name} must resolve officialCardId`);
    assert.ok(item.tierCounts, `${label} ${name} must have tierCounts`);

    const rawWeight = Object.entries(TIER_SCORE).reduce(
      (sum, [tier, score]) => sum + score * (item.tierCounts[tier] || 0),
      0
    );
    assert.equal(item.rawWeight, rawWeight,
      `${label} ${name} rawWeight should aggregate tierCounts`);
    assert.equal(item.heuristicWeight, item.rawWeight + item.duplicateBonus,
      `${label} ${name} heuristicWeight should equal rawWeight + duplicateBonus`);
  }
}

function main() {
  assert.ok(fs.existsSync(WEIGHT_REFERENCE), `missing ${WEIGHT_REFERENCE}`);
  assert.ok(fs.existsSync(COMP_GUIDES), `missing ${COMP_GUIDES}`);
  assert.ok(fs.existsSync(NAME_VALIDATION_REPORT), `missing ${NAME_VALIDATION_REPORT}`);
  assert.ok(fs.existsSync(WEIGHT_TOOL), `missing ${WEIGHT_TOOL}`);

  const reference = readJson(WEIGHT_REFERENCE);
  assert.equal(reference.kind, "season14GuideWeightReference");
  assert.equal(reference.season, "season14");
  assert.equal(reference._meta, undefined,
    "weight reference is a generated data artifact without _meta");
  assert.equal(containsBgDuo(reference), false,
    "weight reference must not contain BGDUO");

  for (const [key, expected] of Object.entries(EXPECTED_COUNTS)) {
    assert.equal(reference.counts[key], expected, `counts.${key}`);
  }
  for (const [tier, expected] of Object.entries(EXPECTED_TIERS)) {
    assert.equal(reference.tiers[tier], expected, `tiers.${tier}`);
  }

  assert.equal(reference.guideReferences.length, 23,
    "guideReferences should cover all comp guides");
  assert.equal(reference.rankedCoreCards.length, reference.counts.uniqueCoreNames,
    "rankedCoreCards should match unique core names");
  assert.equal(reference.rankedTrinkets.length, reference.counts.uniqueTrinketNames,
    "rankedTrinkets should match unique trinket names");

  validateEntries(reference, "coreCards", reference.coreCards);
  validateEntries(reference, "trinkets", reference.trinkets);

  const weightTool = require(WEIGHT_TOOL);
  const guidesDocument = readJson(COMP_GUIDES);
  const report = readJson(NAME_VALIDATION_REPORT);
  const rebuilt = weightTool.buildWeightReference(guidesDocument.guides, report);
  const normalizedReference = { ...reference };
  delete normalizedReference.generatedAt;
  const normalizedRebuilt = { ...rebuilt };
  delete normalizedRebuilt.generatedAt;
  assert.deepStrictEqual(
    normalizedRebuilt,
    normalizedReference,
    "weight reference should be reproducible from comp guides and name validation report"
  );

  process.stdout.write("PASS test_season14_guide_weights\n");
}

main();

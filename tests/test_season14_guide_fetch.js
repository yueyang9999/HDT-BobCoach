"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");

const EVIDENCE_ROOT = "E:/HDT-BobCoach-evidence/season14";
const FETCH_TOOL = path.join(EVIDENCE_ROOT, "tools", "fetch_season14_comp_guides.js");

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function writeJson(file, payload) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
}

function makeTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-season14-fetch-"));
}

function officialCards() {
  return [
    {
      id: "BG35_883",
      name: "巴琳达·斯通赫尔斯",
      type: "MINION",
      set: "BATTLEGROUNDS",
      cost: 6,
      text: "你的以友方随从为目标的法术会施放两次。",
    },
    {
      id: "BGS_071",
      name: "偏折机器人",
      type: "MINION",
      set: "BATTLEGROUNDS",
      cost: 3,
      text: "圣盾。",
    },
    {
      id: "BG32_MagicItem_888",
      name: "回收标签",
      type: "BATTLEGROUND_TRINKET",
      set: "BATTLEGROUNDS",
      cost: 3,
      text: "在使用一张元素牌后，获得一次免费的刷新。",
    },
  ];
}

function goodGuidePayload() {
  return {
    guides: [
      {
        name: "元素雷暴",
        tier: "T0",
        tribes: ["元素"],
        coreCards: [
          "1. 巴琳达·斯通赫尔斯 (6星)：你的以友方随从为目标的法术会施放两次。",
          "2. 偏折机器人 (3星)：圣盾。",
        ],
        trinkets: [
          "1. 回收标签 (3费)：在使用一张元素牌后，获得一次免费的刷新。",
        ],
        strategy: "优先抓高权重核心卡。",
      },
    ],
  };
}

function sheetGuideValues() {
  const values = Array.from({ length: 18 }, () => [null, null, null]);
  values[0][0] = "元素雷暴 攻略";
  values[1][0] = "强度: T0";
  values[1][2] = "种族: 元素";
  values[3][0] = "核心成型卡牌";
  const coreCards = [
    "1. 巴琳达·斯通赫尔斯 (6星)：你的以友方随从为目标的法术会施放两次。",
    "2. 偏折机器人 (3星)：圣盾。",
    "3. 偏折机器人 (3星)：圣盾。",
    "4. 巴琳达·斯通赫尔斯 (6星)：你的以友方随从为目标的法术会施放两次。",
    "5. 偏折机器人 (3星)：圣盾。",
    "6. 巴琳达·斯通赫尔斯 (6星)：你的以友方随从为目标的法术会施放两次。",
  ];
  for (let index = 0; index < 6; index += 1) {
    values[4 + index][0] = coreCards[index];
  }
  values[11][0] = "流派思路";
  values[12][0] = "优先抓核心卡。";
  values[14][0] = "推荐饰品";
  values[15][0] = "1. 回收标签 (3费)：在你使用一张元素牌后，获得一次免费的刷新。 (元素)";
  values[16][0] = "2. 回收标签 (3费)：在你使用一张元素牌后，获得一次免费的刷新。 (元素)";
  values[17][0] = "3. 回收标签 (3费)：在你使用一张元素牌后，获得一次免费的刷新。 (元素)";
  return values;
}

function sheetGuidePayload() {
  return {
    sheets: [
      {
        sheet: "元素雷暴",
        rowCount: 18,
        colCount: 3,
        values: sheetGuideValues(),
      },
    ],
  };
}

function pendingGuidePayload() {
  const payload = goodGuidePayload();
  payload.guides[0].trinkets = [
    "1. 回响标签 (3费)：在使用一张元素牌后，获得一次免费的刷新。",
  ];
  return payload;
}

function setupInputs(tmpDir) {
  const sourcePath = path.join(tmpDir, "season14_guide_source.json");
  const officialCardsPath = path.join(tmpDir, "cards.zhCN.latest.json");
  const aliasesPath = path.join(tmpDir, "guide_name_aliases.json");
  writeJson(sourcePath, { sheets: [] });
  writeJson(officialCardsPath, officialCards());
  writeJson(aliasesPath, { confirmed: [], pending: [] });
  return { sourcePath, officialCardsPath, aliasesPath };
}

function baseOptions(tmpDir, inputs, overrides) {
  return {
    outputDir: path.join(tmpDir, "output"),
    guidesRegistryPath: path.join(tmpDir, "registry", "season14_comp_guides.json"),
    weightReferencePath: path.join(tmpDir, "weight", "season14_guide_weight_reference.json"),
    guideSourcePath: inputs.sourcePath,
    officialCardsPath: inputs.officialCardsPath,
    aliasesPath: inputs.aliasesPath,
    sourceName: "test-payload",
    ...overrides,
  };
}

async function main() {
  assert.ok(fs.existsSync(FETCH_TOOL), `missing ${FETCH_TOOL}`);
  const fetchTool = require(FETCH_TOOL);

  const normalized = fetchTool.normalizeGuide({
    name: "元素雷暴",
    tier: "强度:T0",
    tribes: ["元素"],
    core_cards: ["核心卡A"],
    addon_cards: ["饰品A"],
    how_to_play: "优先抓核心卡。",
  });
  assert.equal(normalized.name, "元素雷暴");
  assert.equal(normalized.tier, "T0");
  assert.deepStrictEqual(normalized.tribes, ["元素"]);
  assert.deepStrictEqual(normalized.coreCards, ["核心卡A"]);
  assert.deepStrictEqual(normalized.trinkets, ["饰品A"]);
  assert.equal(normalized.strategy, "优先抓核心卡。");

  const sheetsNormalized = fetchTool.normalizeGuides(sheetGuidePayload());
  assert.equal(sheetsNormalized.rawCount, 1, "sheet payload should be recognized");
  assert.equal(sheetsNormalized.guides[0].name, "元素雷暴");
  assert.equal(sheetsNormalized.guides[0].tier, "T0");
  assert.equal(sheetsNormalized.guides[0].coreCards.length, 6);
  assert.equal(sheetsNormalized.guides[0].trinkets.length, 3);

  const emptyTmp = makeTempDir();
  const emptyInputs = setupInputs(emptyTmp);
  const emptyResult = await fetchTool.runFetch(baseOptions(emptyTmp, emptyInputs, {
    payload: { guides: [] },
    apply: true,
  }));
  assert.equal(emptyResult.ok, false, "empty fetch result should block");
  assert.equal(emptyResult.blockReason, "empty_result", "empty result should identify itself");
  assert.ok(!fs.existsSync(emptyResult.guidesRegistryPath), "empty result must not write registry");
  assert.ok(fs.existsSync(emptyResult.fetchReportPath), "empty result should write fetch report");
  assert.equal(readJson(emptyResult.fetchReportPath).blockReason, "empty_result");

  const pendingTmp = makeTempDir();
  const pendingInputs = setupInputs(pendingTmp);
  const pendingResult = await fetchTool.runFetch(baseOptions(pendingTmp, pendingInputs, {
    payload: pendingGuidePayload(),
    apply: true,
  }));
  assert.equal(pendingResult.ok, false, "pending alias should block apply");
  assert.equal(pendingResult.blockReason, "name_validation", "validation failure should be explicit");
  assert.ok(!fs.existsSync(pendingResult.guidesRegistryPath), "validation failure must not write registry");
  assert.ok(fs.existsSync(pendingResult.validationReportPath), "failure should still write validation report");
  assert.ok(fs.existsSync(pendingResult.aliasPath), "failure should still write alias review material");
  assert.equal(readJson(pendingResult.validationReportPath).counts.pendingAlias, 1);

  const draftTmp = makeTempDir();
  const draftInputs = setupInputs(draftTmp);
  const draftResult = await fetchTool.runFetch(baseOptions(draftTmp, draftInputs, {
    payload: goodGuidePayload(),
    apply: false,
  }));
  assert.equal(draftResult.ok, true, "clean result should succeed in draft mode");
  assert.equal(draftResult.applied, false, "draft mode should not apply");
  assert.ok(fs.existsSync(draftResult.draftPath), "draft mode should write draft registry");
  assert.ok(fs.existsSync(draftResult.validationReportPath), "draft mode should write validation report");
  assert.ok(!fs.existsSync(draftResult.guidesRegistryPath), "draft mode must not write formal registry");
  assert.ok(!fs.existsSync(draftResult.weightReferencePath), "draft mode must not write weight reference");

  const applyTmp = makeTempDir();
  const applyInputs = setupInputs(applyTmp);
  const applyResult = await fetchTool.runFetch(baseOptions(applyTmp, applyInputs, {
    fetchJson: async () => goodGuidePayload(),
    apply: true,
  }));
  assert.equal(applyResult.ok, true, "clean result should succeed in apply mode");
  assert.equal(applyResult.applied, true, "apply mode should apply");
  assert.ok(fs.existsSync(applyResult.draftPath), "apply mode should still keep draft for review");
  assert.ok(fs.existsSync(applyResult.guidesRegistryPath), "apply mode should write formal registry");
  assert.ok(fs.existsSync(applyResult.weightReferencePath), "apply mode should chain weight reference");
  assert.equal(readJson(applyResult.guidesRegistryPath).guides.length, 1);
  assert.equal(readJson(applyResult.fetchReportPath).applied, true);
  assert.ok(readJson(applyResult.weightReferencePath).counts.guides >= 1);

  process.stdout.write("PASS test_season14_guide_fetch\n");
}

main().catch((error) => {
  process.stderr.write(`Error: ${error.stack || error.message}\n`);
  process.exitCode = 1;
});

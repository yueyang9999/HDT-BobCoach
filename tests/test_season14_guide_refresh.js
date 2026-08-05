"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");

const root = path.resolve(__dirname, "..");
const REFRESH_TOOL = path.join(root, "tools", "refresh", "season14_guide_refresh.js");

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function writeJson(file, payload) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
}

function makeTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-season14-refresh-"));
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

function pendingGuidePayload() {
  const payload = goodGuidePayload();
  payload.guides[0].trinkets = [
    "1. 回响标签 (3费)：在使用一张元素牌后，获得一次免费的刷新。",
  ];
  return payload;
}

function duoGuidePayload() {
  const payload = goodGuidePayload();
  payload.sourceNote = "rejected source with BGDUO_legacy marker";
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
    sourceName: "test-refresh-payload",
    ...overrides,
  };
}

async function main() {
  assert.ok(fs.existsSync(REFRESH_TOOL), `missing ${REFRESH_TOOL}`);
  const refreshTool = require(REFRESH_TOOL);

  const dryRunTmp = makeTempDir();
  const dryRunInputs = setupInputs(dryRunTmp);
  const dryRunResult = await refreshTool.runRefresh(baseOptions(dryRunTmp, dryRunInputs, {
    payload: goodGuidePayload(),
    apply: false,
  }));
  assert.equal(dryRunResult.ok, true, "clean dry-run should succeed");
  assert.equal(dryRunResult.applied, false, "dry-run should not apply");
  assert.ok(!fs.existsSync(dryRunResult.guidesRegistryPath),
    "dry-run must not write formal registry");
  assert.ok(!fs.existsSync(dryRunResult.weightReferencePath),
    "dry-run must not write weight reference");
  assert.ok(fs.existsSync(dryRunResult.refreshReportPath),
    "dry-run should write refresh report");
  assert.equal(readJson(dryRunResult.refreshReportPath).status, "ok");

  const applyTmp = makeTempDir();
  const applyInputs = setupInputs(applyTmp);
  const applyResult = await refreshTool.runRefresh(baseOptions(applyTmp, applyInputs, {
    payload: goodGuidePayload(),
    apply: true,
  }));
  assert.equal(applyResult.ok, true, "clean apply should succeed");
  assert.equal(applyResult.applied, true, "apply should write formal data");
  assert.ok(fs.existsSync(applyResult.guidesRegistryPath),
    "apply should write formal registry");
  assert.ok(fs.existsSync(applyResult.weightReferencePath),
    "apply should chain weight reference");
  assert.ok(fs.existsSync(applyResult.refreshReportPath),
    "apply should write refresh report");
  assert.equal(readJson(applyResult.refreshReportPath).applied, true);

  const emptyTmp = makeTempDir();
  const emptyInputs = setupInputs(emptyTmp);
  const emptyResult = await refreshTool.runRefresh(baseOptions(emptyTmp, emptyInputs, {
    payload: { guides: [] },
    apply: true,
  }));
  assert.equal(emptyResult.ok, false, "empty result should block");
  assert.equal(emptyResult.blockReason, "empty_result", "empty result should identify itself");
  assert.ok(!fs.existsSync(emptyResult.guidesRegistryPath),
    "empty result must not write formal registry");
  assert.ok(fs.existsSync(emptyResult.refreshReportPath),
    "empty result should write refresh report");
  assert.equal(readJson(emptyResult.refreshReportPath).blockReason, "empty_result");

  const pendingTmp = makeTempDir();
  const pendingInputs = setupInputs(pendingTmp);
  const pendingResult = await refreshTool.runRefresh(baseOptions(pendingTmp, pendingInputs, {
    payload: pendingGuidePayload(),
    apply: true,
  }));
  assert.equal(pendingResult.ok, false, "pending alias should block apply");
  assert.equal(pendingResult.blockReason, "name_validation",
    "validation failure should be explicit");
  assert.ok(!fs.existsSync(pendingResult.guidesRegistryPath),
    "validation failure must not write formal registry");
  assert.ok(fs.existsSync(pendingResult.refreshReportPath),
    "validation failure should write refresh report");
  assert.equal(readJson(pendingResult.refreshReportPath).blockReason, "name_validation");

  const duoTmp = makeTempDir();
  const duoInputs = setupInputs(duoTmp);
  const duoResult = await refreshTool.runRefresh(baseOptions(duoTmp, duoInputs, {
    payload: duoGuidePayload(),
    apply: true,
  }));
  assert.equal(duoResult.ok, false, "BGDUO payload should block");
  assert.equal(duoResult.blockReason, "duo_forbidden", "BGDUO should identify itself");
  assert.ok(!fs.existsSync(duoResult.guidesRegistryPath),
    "BGDUO payload must not write formal registry");
  assert.ok(!fs.existsSync(duoResult.weightReferencePath),
    "BGDUO payload must not write weight reference");
  assert.ok(fs.existsSync(duoResult.refreshReportPath),
    "BGDUO payload should write refresh report");
  assert.equal(readJson(duoResult.refreshReportPath).blockReason, "duo_forbidden");

  process.stdout.write("PASS test_season14_guide_refresh\n");
}

main().catch((error) => {
  process.stderr.write(`Error: ${error.stack || error.message}\n`);
  process.exitCode = 1;
});

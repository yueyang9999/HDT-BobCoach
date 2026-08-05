"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const EVIDENCE_ROOT = "E:/HDT-BobCoach-evidence/season14";
const SYNC_TOOL = path.join(EVIDENCE_ROOT, "tools", "sync_season14_guides.js");
const WEIGHT_TOOL = path.join(EVIDENCE_ROOT, "tools", "build_season14_guide_weight_reference.js");

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function main() {
  assert.ok(fs.existsSync(SYNC_TOOL), `missing ${SYNC_TOOL}`);
  assert.ok(fs.existsSync(WEIGHT_TOOL), `missing ${WEIGHT_TOOL}`);

  const sync = require(SYNC_TOOL);

  assert.equal(
    sync.parseSourceName("1. 鬼影幻术师 (5星)：亡语：获取2张理性癫狂。"),
    "鬼影幻术师",
    "should strip guide numbering and parenthetical suffix"
  );
  assert.equal(
    sync.parseSourceName("6. 上锁宝箱 (酒馆法术)：无法使用。5回合后打开宝箱。"),
    "上锁宝箱",
    "should parse tavern spell entries"
  );
  assert.equal(
    sync.parseSourceName("1. “船长”曲奇 (4星)：亡语：获取一张主厨甄选。"),
    "“船长”曲奇",
    "should preserve quoted names"
  );

  const cards = [
    { id: "BG36_MagicItem_810", name: "磁感旋刃", type: "BATTLEGROUND_TRINKET", cost: 2, text: "获取一张4/4的磁力卫星。" },
    { id: "BG36_MagicItem_810t", name: "磁感旋刃", type: "BATTLEGROUND_TRINKET", cost: 2, text: "获取一张8/8的磁力卫星。" },
    { id: "BG35_MagicItem_861", name: "投球手肖像", type: "BATTLEGROUND_TRINKET", cost: 5, text: "获取一张寒热骤变。" },
    { id: "BG36_MagicItem_390", name: "投球手肖像", type: "BATTLEGROUND_TRINKET", cost: 3, text: "获取一张火焰投球手或冰雪投球手。" },
    { id: "BG35_MagicItem_733", name: "典狱长标签", type: "BATTLEGROUND_TRINKET", cost: 2, text: "消灭一个友方亡灵以随机获取2张亡灵牌。" },
    { id: "BG35_MagicItem_733t", name: "典狱长标签", type: "SPELL", cost: 0, text: "消灭一个友方亡灵以随机获取2张亡灵牌。" },
    { id: "BG32_MagicItem_888", name: "回收标签", type: "BATTLEGROUND_TRINKET", cost: 3, text: "在使用一张元素牌后，获得一次免费的刷新。" },
  ];

  const selected = sync.selectOfficialCandidate({
    sourceName: "磁感旋刃",
    rawEntry: "1. 磁感旋刃 (2费)：在你的回合结束时，获取一张4的磁力卫星。",
    field: "trinkets",
    tribes: ["机械"],
    officialCards: cards,
    aliases: [],
  });
  assert.equal(selected.status, "matched", "base card should win over variant");
  assert.equal(selected.officialCardId, "BG36_MagicItem_810", "should choose base magnetic card");

  const ambiguous = sync.selectOfficialCandidate({
    sourceName: "投球手肖像",
    rawEntry: "2. 投球手肖像 (5费)：获取一张寒热骤变。",
    field: "trinkets",
    tribes: ["元素"],
    officialCards: cards,
    aliases: [],
  });
  assert.equal(ambiguous.status, "matched", "context should resolve same-name trinkets");
  assert.equal(ambiguous.officialCardId, "BG35_MagicItem_861", "should choose portrait granting 寒热骤变");

  const pending = sync.selectOfficialCandidate({
    sourceName: "回响标签",
    rawEntry: "1. 回响标签 (3费)：在你使用一张元素牌后，获得一次免费的刷新。",
    field: "trinkets",
    tribes: ["元素"],
    officialCards: cards,
    aliases: [],
  });
  assert.equal(pending.status, "pending", "typographic mismatch should be pending");
  assert.equal(pending.officialName, "回收标签", "pending alias should point to official name");
  assert.equal(pending.officialCardId, "BG32_MagicItem_888", "pending alias should point to official card");

  const realReport = sync.buildSyncReport({
    guideSourcePath: path.join(EVIDENCE_ROOT, "generated", "season14_guide_source.json"),
    guidesRegistryPath: path.join(EVIDENCE_ROOT, "generated", "registries", "season14_comp_guides.json"),
    officialCardsPath: path.join(EVIDENCE_ROOT, "raw", "cards.zhCN.latest.json"),
    aliases: [],
  });
  assert.equal(realReport.counts.totalEntries, 207, "23 guides with 6 core cards and 3 trinkets");
  assert.equal(
    realReport.counts.matched
      + realReport.counts.pendingAlias
      + realReport.counts.unmatched
      + realReport.counts.ambiguous,
    realReport.counts.totalEntries,
    "every entry should have a final status"
  );
  assert.equal(realReport.counts.matched, 205, "battlegrounds same-name cards should be auto-resolved");
  assert.equal(realReport.counts.pendingAlias, 2, "two typo aliases should remain pending for manual confirmation");
  assert.equal(realReport.counts.ambiguous, 0, "same-name cards should not require manual review");
  assert.equal(realReport.counts.unmatched, 0, "all guide names should resolve or have a pending alias suggestion");
  const pendingEntries = realReport.entries.filter(entry => entry.status === "pending");
  const pendingBySource = Object.fromEntries(pendingEntries.map(entry => [entry.sourceName, entry]));
  assert.equal(pendingBySource["回响标签"].officialName, "回收标签", "real report should suggest 回响标签 -> 回收标签");
  assert.equal(pendingBySource["热球手肖像"].officialName, "投球手肖像", "real report should suggest 热球手肖像 -> 投球手肖像");
  const brannEntries = realReport.entries.filter(entry => entry.sourceName === "布莱恩·铜须" && entry.status === "matched");
  assert.equal(brannEntries.length, 5, "all five brann guide rows should resolve");
  assert.ok(brannEntries.every(entry => entry.officialCardId === "BG_LOE_077"), "brann should resolve to current battlegrounds card");
  const prosthetic = realReport.entries.find(entry => entry.sourceName === "义肢假手");
  assert.equal(prosthetic.officialCardId, "BG_DEEP_015", "prosthetic hand should resolve to base battlegrounds card");

  const weight = require(WEIGHT_TOOL);
  const guidesDoc = readJson(path.join(EVIDENCE_ROOT, "generated", "registries", "season14_comp_guides.json"));
  const reference = weight.buildWeightReference(guidesDoc.guides, realReport);
  assert.equal(reference.counts.guides, 23);
  assert.equal(reference.counts.coreEntries, 138, "6 core slots across 23 guides");
  assert.equal(reference.counts.trinketEntries, 69, "3 trinket slots across 23 guides");
  assert.ok(reference.tiers.T0 >= 0, "tier distribution should exist");
  assert.ok(Object.keys(reference.coreCards).length > 0, "core card frequency should be non-empty");
  assert.ok(Object.keys(reference.trinkets).length > 0, "trinket frequency should be non-empty");
  const repeated = Object.values(reference.coreCards).find(card => card.multiCopyGuides.length > 0);
  assert.ok(repeated, "two identical core cards should be counted as a higher-weight signal");
  assert.equal(repeated.occurrences, repeated.multiCopyGuides.length + 1, "two copies in one guide should count twice");

  process.stdout.write("PASS test_season14_guide_sync\n");
}

main();

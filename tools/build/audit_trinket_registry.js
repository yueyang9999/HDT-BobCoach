"use strict";
/*
 * 饰品 registry 权威审计 + rated/unrated 分类 — P1.5 Phase2 (2026-07-10)
 * 1) diff trinket_registry.json vs HSJSON(type=BATTLEGROUND_TRINKET): 孤儿/缺失/大小分类不符
 * 2) 按 sync 脚本 baseScore 的关键词逻辑重跑每条 text → rated(命中≥1价值关键词)/unrated(零命中)
 *    unrated 判定口径(用户确认): 基础分且零价值关键词命中。
 * 只读, 输出报告到 data/_authority/trinket_diff_report.json, 不改 registry。
 */
const fs = require("fs");
const path = require("path");
const base = path.join(__dirname, "..");

const reg = JSON.parse(fs.readFileSync(path.join(base, "data", "trinket_registry.json"), "utf8"));
const all = JSON.parse(fs.readFileSync(path.join(base, "data", "_authority", "_hsjson_all_zh.json"), "utf8"));
const auth = all.filter(c => c.type === "BATTLEGROUND_TRINKET");
const authById = new Map(auth.map(c => [c.id, c]));

// baseScore 的 4 组正向价值关键词(与 sync_trinkets_from_hsjson.js 一致) — 命中任一=rated
const KW = [
    /每当|永久|回合开始|At the start|Whenever|permanently/i,
    /铸币|金币|刷新|Gold|Refresh/i,
    /圣盾|剧毒|烈毒|Divine Shield|Poisonous|Venomous/i,
    /免费|Choose|Discover|发现|获取/i,
];
function isRated(text) {
    text = text || "";
    return KW.some(re => re.test(text));
}

const regEntries = [...(reg.lesser || []).map(e => ({ ...e, _sec: "lesser" })),
                    ...(reg.greater || []).map(e => ({ ...e, _sec: "greater" }))];
const regById = new Map(regEntries.map(e => [e.cardId, e]));

// diff
const orphans = regEntries.filter(e => !authById.has(e.cardId));           // registry有/权威无
const missing = auth.filter(c => !regById.has(c.id));                       // 权威有/registry缺
const classMismatch = [];                                                  // 大小分类不符
for (const e of regEntries) {
    const a = authById.get(e.cardId);
    if (!a) continue;
    const authLesser = a.spellSchool === "LESSER_TRINKET";
    const regLesser = e._sec === "lesser";
    if (authLesser !== regLesser)
        classMismatch.push({ cardId: e.cardId, name: e.name_cn, reg: e._sec, auth: a.spellSchool });
}

// rated/unrated 分类
let rated = 0, unrated = 0;
const unratedList = [];
for (const e of regEntries) {
    if (isRated(e.text)) rated++;
    else { unrated++; unratedList.push({ cardId: e.cardId, sec: e._sec, name: e.name_cn, score: e.score, text: (e.text || "").slice(0, 40) }); }
}

const authLesserN = auth.filter(c => c.spellSchool === "LESSER_TRINKET").length;
const authGreaterN = auth.filter(c => c.spellSchool === "GREATER_TRINKET").length;

const report = {
    timestamp: "2026-07-10",
    authority: { source: "HSJSON latest/zhCN", total_trinkets: auth.length, lesser: authLesserN, greater: authGreaterN },
    registry: { total: regEntries.length, lesser: (reg.lesser || []).length, greater: (reg.greater || []).length },
    diff: {
        orphans_count: orphans.length,
        orphans: orphans.map(e => ({ cardId: e.cardId, sec: e._sec, name: e.name_cn })),
        missing_count: missing.length,
        missing: missing.map(c => ({ cardId: c.id, name: c.name, spellSchool: c.spellSchool })),
        class_mismatch_count: classMismatch.length,
        class_mismatch: classMismatch,
    },
    classification: { rated, unrated, unrated_pct: (100 * unrated / regEntries.length).toFixed(1) + "%" },
    unrated_sample: unratedList.slice(0, 20),
};
fs.writeFileSync(path.join(base, "data", "_authority", "trinket_diff_report.json"), JSON.stringify(report, null, 2));

console.log("═".repeat(60));
console.log(`权威(HSJSON): ${auth.length}条 (lesser ${authLesserN} / greater ${authGreaterN})`);
console.log(`registry: ${regEntries.length}条 (lesser ${(reg.lesser || []).length} / greater ${(reg.greater || []).length})`);
console.log("─".repeat(60));
console.log(`孤儿(registry有/权威无): ${orphans.length}`);
orphans.slice(0, 15).forEach(e => console.log(`  ${e.cardId} [${e._sec}] ${e.name_cn}`));
console.log(`缺失(权威有/registry无): ${missing.length}`);
missing.slice(0, 15).forEach(c => console.log(`  ${c.id} [${c.spellSchool}] ${c.name}`));
console.log(`大小分类不符: ${classMismatch.length}`);
classMismatch.slice(0, 15).forEach(m => console.log(`  ${m.cardId} ${m.name} reg=${m.reg} auth=${m.auth}`));
console.log("─".repeat(60));
console.log(`分类: rated ${rated} / unrated ${unrated} (${report.classification.unrated_pct})`);
console.log("unrated样本(前10):");
unratedList.slice(0, 10).forEach(u => console.log(`  ${u.cardId}[${u.sec}] ${u.name} score${u.score} "${u.text}"`));
console.log("═".repeat(60));
console.log("报告: data/_authority/trinket_diff_report.json");

"use strict";

const fs = require("fs");
const path = require("path");
const MIN_FIRESTONE_POINTS = 1000;

function readJson(file) {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

function readJsonLines(file) {
    if (!fs.existsSync(file)) return [];
    return parseJsonLines(fs.readFileSync(file, "utf8"));
}

function parseJsonLines(text) {
    return String(text || "").replace(/^\uFEFF/, "").split(/\r?\n/)
        .filter(line => line.trim()).map(line => JSON.parse(line));
}

function averageRanks(items, compare, tieKey) {
    const sorted = items.slice().sort(compare);
    const ranks = new Map();
    for (let i = 0; i < sorted.length;) {
        let j = i + 1;
        while (j < sorted.length && tieKey(sorted[j]) === tieKey(sorted[i])) j++;
        const rank = ((i + 1) + j) / 2;
        for (let k = i; k < j; k++) ranks.set(sorted[k].cardId, rank);
        i = j;
    }
    return ranks;
}

function spearman(items, rankA, rankB) {
    if (items.length < 2) return null;
    const a = items.map(x => rankA.get(x.cardId));
    const b = items.map(x => rankB.get(x.cardId));
    const ma = a.reduce((s, x) => s + x, 0) / a.length;
    const mb = b.reduce((s, x) => s + x, 0) / b.length;
    let num = 0, da = 0, db = 0;
    for (let i = 0; i < a.length; i++) {
        const xa = a[i] - ma, xb = b[i] - mb;
        num += xa * xb; da += xa * xa; db += xb * xb;
    }
    return da > 0 && db > 0 ? num / Math.sqrt(da * db) : null;
}

function pct(value) { return `${(value * 100).toFixed(1)}%`; }
function num(value, digits = 2) { return Number(value).toFixed(digits); }
function cell(value) { return String(value == null ? "" : value).replace(/\|/g, "\\|"); }

function buildCatalog(registry, statMap) {
    const result = [];
    for (const [kind, rows] of [["lesser", registry.lesser], ["greater", registry.greater]]) {
        for (const row of rows) {
            const stat = statMap.get(row.cardId);
            if (!stat) continue;
            result.push({
                cardId: row.cardId,
                name: row.name_cn || row.cardId,
                kind,
                rated: !row.unrated,
                schemeScore: row.unrated ? 0 : Number(row.score || 0),
                placement: Number(stat.AveragePlacement),
                pickRate: Number(stat.PickRate),
                dataPoints: Number(stat.DataPoints),
            });
        }
    }
    return result;
}

function rankCatalog(rows) {
    const schemeCompare = (a, b) =>
        Number(b.rated) - Number(a.rated) || b.schemeScore - a.schemeScore || a.cardId.localeCompare(b.cardId);
    const fireCompare = (a, b) => a.placement - b.placement || a.cardId.localeCompare(b.cardId);
    const schemeRanks = averageRanks(rows, schemeCompare, x => `${x.rated ? 1 : 0}:${x.schemeScore}`);
    const fireRanks = averageRanks(rows, fireCompare, x => x.placement.toFixed(9));
    return rows.map(x => ({
        ...x,
        schemeRank: schemeRanks.get(x.cardId),
        fireRank: fireRanks.get(x.cardId),
        rankGap: schemeRanks.get(x.cardId) - fireRanks.get(x.cardId),
    }));
}

function offerTopByScheme(offers) {
    return offers.slice().sort((a, b) =>
        Number(!b.IsUnrated) - Number(!a.IsUnrated) || Number(b.score) - Number(a.score)
        || String(a.CardId).localeCompare(String(b.CardId)))[0];
}

function offerTopByFirestone(offers, statMap) {
    return offers.filter(x => statMap.has(x.CardId)
        && statMap.get(x.CardId).DataPoints >= MIN_FIRESTONE_POINTS).sort((a, b) =>
        statMap.get(a.CardId).AveragePlacement - statMap.get(b.CardId).AveragePlacement
        || String(a.CardId).localeCompare(String(b.CardId)))[0];
}

function generateReport(active, registry, shadows, generatedAt = new Date()) {
    if (!active || active.StatusReason !== "verified" || !Array.isArray(active.Stats))
        throw new Error("refusing report: active trinket stats snapshot is not verified");
    const statMap = new Map(active.Stats.map(x => [x.TrinketCardId, x]));
    const catalog = buildCatalog(registry, statMap);
    const reliableCatalog = catalog.filter(x => x.dataPoints >= MIN_FIRESTONE_POINTS);
    const ranked = ["lesser", "greater"].flatMap(kind => rankCatalog(reliableCatalog.filter(x => x.kind === kind)));
    const eligible = shadows.filter(x => x.schemaVersion === 2 && x.eligibleForCalibration === true
        && x.completionStatus === "completed" && x.selectedCardId && Array.isArray(x.offers));

    const offerRows = eligible.map((sample, index) => {
        const scheme = offerTopByScheme(sample.offers);
        const fire = offerTopByFirestone(sample.offers, statMap);
        const selectedStat = statMap.get(sample.selectedCardId);
        const fireStat = fire && statMap.get(fire.CardId);
        return {
            index: index + 1,
            sample,
            scheme,
            fire,
            selected: sample.offers.find(x => x.CardId === sample.selectedCardId),
            schemeFireAgree: !!fire && scheme.CardId === fire.CardId,
            playerSchemeAgree: scheme.CardId === sample.selectedCardId,
            playerFireAgree: !!fire && fire.CardId === sample.selectedCardId,
            playerPlacementGap: selectedStat && fireStat
                ? selectedStat.AveragePlacement - fireStat.AveragePlacement : null,
        };
    });

    const lines = [];
    lines.push("# P1.5 饰品统计离线差异报告", "");
    lines.push(`生成时间：${generatedAt.toISOString()}`);
    lines.push(`游戏 Build：${active.GameBuild}；Firestone 上游时间：${active.LastUpdateDateUtc}`);
    lines.push(`Firestone：${active.Stats.length} 条，${Number(active.TotalDataPoints).toLocaleString("en-US")} 数据点；内容 SHA-256：\`${active.ContentSha256}\``);
    lines.push(`可靠性门槛：单条至少 ${MIN_FIRESTONE_POINTS} 样本；${reliableCatalog.length}/${catalog.length} 条进入主排序，${catalog.length - reliableCatalog.length} 条低样本仅保留覆盖记录。`);
    lines.push(`shadow：${eligible.length} 个有效选择批次（小饰品 ${eligible.filter(x => x.selectionContext === "scheduled_lesser").length}，大饰品 ${eligible.filter(x => x.selectionContext === "scheduled_greater").length}）。`, "");
    lines.push("## 结论边界", "");
    lines.push("- 本报告只做离线诊断，不修改生产评分或 UI 排序。", "- Firestone 平均名次受英雄、阵容、费用、玩家水平和选择倾向影响，不等于饰品的因果强度。", `- 主排序和shadow中的Firestone首选均排除少于 ${MIN_FIRESTONE_POINTS} 样本的条目。`, "- 全局比较使用 scheme B 注册表基础分；shadow 报价比较使用实战时已经计算并落盘的动态 scheme B 分数。", "- shadow 样本量仍小，只能定位冲突案例，不能据此拟合权重。", "");

    lines.push("## 全局排序一致性", "");
    lines.push("| 类型 | 覆盖 | rated | unrated | Spearman ρ |", "|---|---:|---:|---:|---:|");
    for (const kind of ["lesser", "greater"]) {
        const rows = ranked.filter(x => x.kind === kind);
        const sr = averageRanks(rows, (a, b) => a.schemeRank - b.schemeRank, x => x.schemeRank);
        const fr = averageRanks(rows, (a, b) => a.fireRank - b.fireRank, x => x.fireRank);
        const rho = spearman(rows, sr, fr);
        lines.push(`| ${kind === "lesser" ? "小饰品" : "大饰品"} | ${rows.length} | ${rows.filter(x => x.rated).length} | ${rows.filter(x => !x.rated).length} | ${rho == null ? "n/a" : num(rho, 3)} |`);
    }
    lines.push("");

    for (const kind of ["lesser", "greater"]) {
        const label = kind === "lesser" ? "小饰品" : "大饰品";
        const rows = ranked.filter(x => x.kind === kind);
        const under = rows.slice().sort((a, b) => b.rankGap - a.rankGap).slice(0, 10);
        const over = rows.slice().sort((a, b) => a.rankGap - b.rankGap).slice(0, 10);
        for (const [title, list] of [["Firestone明显强于scheme B", under], ["scheme B明显强于Firestone", over]]) {
            lines.push(`### ${label}：${title}`, "");
            lines.push("| 饰品 | rated | B分 | B排名 | Firestone排名 | 平均名次 | 选取率 | 样本 | 排名差(B-F) |", "|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (const x of list) lines.push(`| ${cell(x.name)} | ${x.rated ? "是" : "否"} | ${num(x.schemeScore, 1)} | ${num(x.schemeRank, 1)} | ${num(x.fireRank, 1)} | ${num(x.placement, 3)} | ${pct(x.pickRate)} | ${x.dataPoints} | ${num(x.rankGap, 1)} |`);
            lines.push("");
        }
    }

    const count = key => offerRows.filter(x => x[key]).length;
    const gaps = offerRows.map(x => x.playerPlacementGap).filter(x => x != null);
    lines.push("## shadow 报价级对照", "");
    lines.push(`- scheme B 与 Firestone 首选一致：${count("schemeFireAgree")}/${offerRows.length}`);
    lines.push(`- 玩家实选与 scheme B 首选一致：${count("playerSchemeAgree")}/${offerRows.length}`);
    lines.push(`- 玩家实选与 Firestone 首选一致：${count("playerFireAgree")}/${offerRows.length}`);
    lines.push(`- 玩家实选相对报价内 Firestone 最优的平均名次差：${gaps.length ? num(gaps.reduce((a, b) => a + b, 0) / gaps.length, 3) : "n/a"}（正值表示群体平均名次更差）。`, "");
    lines.push("| # | 类型/回合 | choice | scheme B首选 | Firestone首选 | 玩家实选 | B=F | 玩家=B | 玩家=F |", "|---:|---|---:|---|---|---|---:|---:|---:|");
    for (const x of offerRows) {
        const s = x.sample;
        const type = s.selectionContext === "scheduled_lesser" ? "小" : "大";
        const fireName = x.fire ? (x.fire.Name || x.fire.CardId) : "缺数据";
        const selectedName = x.selected ? (x.selected.Name || x.selected.CardId) : s.selectedCardId;
        lines.push(`| ${x.index} | ${type}/T${s.turn} | ${s.choiceId} | ${cell(x.scheme.Name || x.scheme.CardId)} (${num(x.scheme.score, 1)}) | ${cell(fireName)} | ${cell(selectedName)} | ${x.schemeFireAgree ? "✓" : ""} | ${x.playerSchemeAgree ? "✓" : ""} | ${x.playerFireAgree ? "✓" : ""} |`);
    }
    lines.push("", "## 下一步门禁", "", "1. 先人工复核排名差最大的饰品，区分注册表漏评、阵容条件差异与Firestone选择偏差。", "2. 在没有完成分层（至少大小饰品、MMR、选取率/样本量可靠性）前，不把平均名次直接换算成加分。", "3. 若进入评分实验，只允许离线回放或shadow候选分支；生产分支继续保持隔离，并设置有界影响上限和一键失效关闭。", "");
    return lines.join("\n");
}

function parseArgs(argv) {
    const args = {};
    for (let i = 0; i < argv.length; i += 2) args[argv[i]] = argv[i + 1];
    return args;
}

if (require.main === module) {
    const root = path.resolve(__dirname, "..");
    const repoRoot = path.resolve(root, "..");
    const args = parseArgs(process.argv.slice(2));
    const activePath = args["--active"] || path.join(process.env.APPDATA || "", "bob-coach", "data", "trinket-stats", "active.json");
    const shadowPath = args["--shadow"] || path.join(process.env.APPDATA || "", "bob-coach", "trinket_shadow.jsonl");
    const registryPath = args["--registry"] || path.join(root, "data", "trinket_registry.json");
    const outputPath = args["--output"] || path.join(repoRoot, "docs", "P1.5_饰品统计离线差异报告_2026-07-11.md");
    const report = generateReport(readJson(activePath), readJson(registryPath), readJsonLines(shadowPath));
    fs.writeFileSync(outputPath, report, "utf8");
    console.log(`Trinket stats diff report written: ${outputPath}`);
}

module.exports = { generateReport, parseJsonLines, MIN_FIRESTONE_POINTS };

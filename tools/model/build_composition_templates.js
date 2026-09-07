'use strict';
// P0: build composition_templates.json from three comp data sources (C-12 组合推理层前置数据归一)
// Usage: node build_composition_templates.js --data-root <evidence-root> [--out <relative.json>]
// Data root convention: assertExternalDataRoot + resolveDataPath (contracts.js)
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { assertExternalDataRoot, resolveDataPath } = require('./contracts');

// ---- args ----
const ALLOWED = new Set(['--data-root', '--out']);
const args = {};
for (let i = 2; i < process.argv.length; i++) {
  const a = process.argv[i];
  if (ALLOWED.has(a)) { args[a] = process.argv[i + 1]; i++; }
  else throw new Error('unknown arg ' + a);
}
if (!args['--data-root']) throw new Error('--data-root is required');
const REPO = path.resolve(__dirname, '..', '..');
const dataRoot = assertExternalDataRoot(args['--data-root'], REPO);
const outRel = args['--out'] || 'knowledge-base/composition_templates.json';

// ---- sources (relative to data root; actual evidence layout) ----
// NOTE(2026-09-07 实施勘误): 中文23体系结构化卡源=guide_weight_reference.guideReferences;
// comp_strategies 中文23条为占位(cardId=1..6)。英文22体系=comp_strategies(真实卡ID)。
function loadJson(p) { return JSON.parse(fs.readFileSync(p, 'utf8')); }
function tryResolve(rel) {
  try { return resolveDataPath(dataRoot, rel, 'source'); } catch (e) { return null; }
}

const guideWeightsRel = 'season14/generated/season14_guide_weight_reference.json';
const guidesRel = 'season14/generated/registries/season14_comp_guides.json';
const compStrategiesCandidates = [
  'simulation-season14/season14-data/comp_strategies.json',
  'simulation-season14/season14-data-250339/comp_strategies.json',
];
const srcs = { strategies: null, guides: null, weights: null, paths: {} };
let p = tryResolve(guideWeightsRel);
if (p) { srcs.weights = loadJson(p); srcs.paths.weights = guideWeightsRel; }
p = tryResolve(guidesRel);
if (p) { srcs.guides = loadJson(p); srcs.paths.guides = guidesRel; }
for (const rel of compStrategiesCandidates) {
  const q = tryResolve(rel);
  if (q) { srcs.strategies = loadJson(q); srcs.paths.strategies = rel; break; }
}
const missing = Object.keys(srcs).filter(k => k !== 'paths' && !srcs[k]);
if (missing.length) throw new Error('missing sources: ' + missing.join(','));

const guidesArr = srcs.guides.guides || [];
const refs = (srcs.weights.guideReferences || []);
const refByGuide = new Map(refs.map(r => [r.guideId, r]));
const strategiesArr = Array.isArray(srcs.strategies) ? srcs.strategies : (srcs.strategies.strategies || []);

// ---- normalize engine card ids with provenance ----
function englishTemplate(c) {
  // comp_strategies english entry (real card ids). tips[].whenToCommit -> activation; freshness.exists filters stale
  const cards = (c.cards || []).filter(x => typeof x.cardId === 'string' && x.cardId.length > 0 && x.cardId !== '1');
  const core = cards.filter(x => x.status === 'CORE').map(x => x.cardId);
  const addon = cards.filter(x => x.status === 'ADDON').map(x => x.cardId);
  const tip = (c.tips || [])[0];
  return {
    compId: c.compId, nameCn: c.name_cn || c.name || c.compId,
    provenance: { src: 'comp_strategies', patch: c.patchNumber || null, tier: c.powerLevel || null },
    forcedTribes: Array.isArray(c.forcedTribes) ? c.forcedTribes : [],
    direction: inferDirection(c.compId, c.name_cn || ''),
    medianCoreTier: medianTierOf(core, c),
    engineCardIds: core.length ? core : addon,
    addonCardIds: addon,
    missingLinkType: inferMissingLinks(c.compId, core, addon),
    ceilingPower: null,
    endgame: { tier: (c.powerLevel === 'S' || c.powerLevel === 'A') ? 6 : 5, template: null },
    activation: tip && tip.whenToCommit ? { requires: tip.whenToCommit, windowTurn: null } : null,
    counters: [],
  };
}
function chineseTemplate(guideId) {
  const ref = refByGuide.get(guideId);
  const g = guidesArr.find(x => x.id === guideId) || {};
  if (!ref) return null;
  const core = (ref.coreCards || []).map(x => x.officialCardId).filter(Boolean);
  const trinkets = (ref.trinkets || []).map(x => x.officialCardId).filter(Boolean);
  return {
    compId: guideId, nameCn: g.name || ref.guideName || guideId,
    provenance: { src: 'comp_guides', patch: null, tier: ref.tier || g.tier || null },
    forcedTribes: Array.isArray(ref.tribes) ? ref.tribes : (Array.isArray(g.tribes) ? g.tribes : []),
    direction: inferDirection(guideId, g.name || ''),
    medianCoreTier: 99,
    engineCardIds: core, addonCardIds: [],
    trinketCardIds: trinkets,
    missingLinkType: inferMissingLinks(guideId, core, []),
    ceilingPower: null,
    endgame: { tier: (ref.tier === 'T0' || ref.tier === 'T1') ? 6 : 5, template: null },
    activation: null,
    counters: [],
  };
}

// median tier of engine cards (freshness.currentTier when available; else 99=unknown)
function medianTierOf(ids, comp) {
  var tiers = [];
  for (var ti = 0; ti < (comp.cards || []).length; ti++) {
    var cc = comp.cards[ti];
    if (!cc || !cc.cardId) continue;
    var raw = String(cc.cardId).replace(/_G$/, '');
    if (ids.indexOf(raw) >= 0 || ids.indexOf(cc.cardId) >= 0) {
      var t = cc.freshness && cc.freshness.currentTier ? cc.freshness.currentTier : 0;
      if (t) tiers.push(t);
    }
  }
  if (!tiers.length) return 99;
  tiers.sort(function (a, b) { return a - b; });
  return tiers[Math.floor(tiers.length / 2)];
}

function inferDirection(compId, nameCn) {
  const s = (compId + ' ' + nameCn);
  if (/毒|烈毒|圣盾|scam|鱼人.*毒|murloc_scam/.test(s)) return 'tech';
  if (/经济|电表|海盗|倒转|econ|bounty|pirates/.test(s)) return 'econ';
  if (/亡灵|亡语|end_of_turn|undead|野兽.*白狼|beast_self/.test(s)) return 'engine';
  if (/元素|小粉|雷暴|纳迦|naga|elemental/.test(s)) return 'stats';
  if (/机械|法术|magnets|automaton|mech/.test(s)) return 'stats';
  if (/龙|卡雷|kalecgos|dragon|野猪|quill|抉择/.test(s)) return 'engine';
  return 'engine';
}
function inferMissingLinks(compId, core, addon) {
  const links = [];
  if (core.length <= 2) links.push('engine');
  if (/双|对子|triple|pair/.test(compId)) links.push('pair_triple');
  if (/法术|spell|generator|咒术|理性癫狂/.test(compId)) links.push('spellgen');
  if (/经济|econ|电表/.test(compId)) links.push('economy');
  if (!links.length) links.push('engine');
  return links;
}

// ---- build template list ----
const templates = [];
const seen = new Set();
for (const c of strategiesArr) {
  const isCn = /[\u4e00-\u9fff]/.test(c.compId || '');
  if (isCn) continue; // chinese placeholders handled via guide refs
  if (seen.has(c.compId)) continue;
  templates.push(englishTemplate(c));
  seen.add(c.compId);
}
for (const g of guidesArr) {
  if (seen.has(g.id)) continue;
  const t = chineseTemplate(g.id);
  if (t) { templates.push(t); seen.add(g.id); }
}

// ---- contract header (from guides _meta; sha computed over templates body) ----
const body = JSON.stringify({ templates }, null, 1);
const sha = crypto.createHash('sha256').update(body, 'utf8').digest('hex');
const meta = srcs.guides._meta || {};
const header = {
  schemaVersion: 1,
  season: meta.season || 'season14',
  gameBuild: meta.gameBuild || '36.2.248348',
  cardPoolIdentity: meta.cardPoolIdentity || 'unknown',
  source: [
    { name: 'comp_strategies', path: srcs.paths.strategies, buildOrDate: 'n/a' },
    { name: 'season14_comp_guides', path: srcs.paths.guides, buildOrDate: meta.capturedAt || null },
    { name: 'season14_guide_weight_reference', path: srcs.paths.weights, buildOrDate: srcs.weights.generatedAt || null },
  ],
  capturedAt: new Date().toISOString().replace(/\.\d+Z$/, 'Z'),
  sha256: sha,
  provenance: { generator: 'tools/model/build_composition_templates.js', inputs: srcs.paths },
  note: 'engineCardIds 来自 guideReferences(中文)/comp_strategies(英文); ceilingPower 数值位 null(=unknown, 36.4.2 权威卡表缺失); 玩家动作=观测',
};
const output = Object.assign({}, header, { templates });
const outAbs = resolveDataPath(dataRoot, outRel, 'output', { allowMissing: true });
fs.mkdirSync(path.dirname(outAbs), { recursive: true });
fs.writeFileSync(outAbs, JSON.stringify(output, null, 1) + '\n', 'utf8');

// ---- validation report (fail-closed on invariant violations) ----
const errors = [];
if (templates.length !== 45) errors.push('template count ' + templates.length + ' != 45');
for (const t of templates) {
  if (!t.compId || !t.nameCn) errors.push('missing compId/nameCn in ' + JSON.stringify(t.compId));
  if (!Array.isArray(t.engineCardIds) || t.engineCardIds.length === 0)
    errors.push('no engineCardIds in ' + t.compId);
}
const dup = templates.length - new Set(templates.map(t => t.compId)).size;
if (dup !== 0) errors.push('duplicate compIds: ' + dup);
const dirs = new Set(templates.map(t => t.direction));
for (const d of dirs) if (!['engine', 'tech', 'econ', 'stats'].includes(d)) errors.push('bad direction ' + d);

const report = {
  generatedAt: header.capturedAt, templateCount: templates.length, duplicateCompIds: dup,
  directions: Array.from(dirs).sort(), errors, sha256: sha,
};
const reportPath = outAbs.replace(/\.json$/, '.report.json');
fs.writeFileSync(reportPath, JSON.stringify(report, null, 1) + '\n', 'utf8');
console.log('templates:', templates.length, '| dup:', dup, '| directions:', Array.from(dirs).join(','));
if (errors.length) { console.log('VALIDATION FAILED:'); errors.forEach(e => console.log(' - ' + e)); process.exit(1); }
console.log('written:', outAbs);
console.log('report:', reportPath);
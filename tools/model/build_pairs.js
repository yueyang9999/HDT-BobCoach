'use strict';
const fs = require('fs');
const path = require('path');
const { assertExternalDataRoot, resolveDataPath } = require('./contracts');

const gradeOrder = Object.freeze({ preferred: 3, acceptable: 2, weak: 1, 'severe-error': 0 });
const knownGrades = new Set(Object.keys(gradeOrder).concat('not-evaluable'));
const knownActionTypes = new Set([
  'BuyMinion', 'BuySpell', 'Refresh', 'Upgrade', 'SellMinion', 'FreezeShop',
  'UseHeroPower', 'PickTrinket', 'PickDiscover', 'SendGoldToTeammate'
]);

function buildPairs(scenario) {
  if (!scenario || typeof scenario !== 'object') throw new Error('scenario is required');
  requireText(scenario.gameId, 'gameId');
  requireText(scenario.capturedAtUtc, 'capturedAtUtc');
  requireText(scenario.scenarioId, 'scenarioId');
  const provenance = resolvedProvenance(scenario);
  if (!Array.isArray(scenario.candidates) || !Array.isArray(scenario.actionLabels))
    throw new Error('candidates and actionLabels are required');

  const candidates = new Map();
  scenario.candidates.forEach(candidate => {
    validateCandidate(candidate);
    if (candidates.has(candidate.actionKey)) throw new Error('duplicate candidate actionKey');
    candidates.set(candidate.actionKey, sanitizeCandidate(candidate));
  });
  if (!Array.isArray(scenario.acceptedActionKeys))
    throw new Error('acceptedActionKeys are required');
  const acceptedActionKeys = new Set();
  scenario.acceptedActionKeys.forEach(actionKey => {
    requireText(actionKey, 'accepted action key');
    if (!candidates.has(actionKey)) throw new Error('accepted action references missing candidate');
    if (acceptedActionKeys.has(actionKey)) throw new Error('duplicate accepted action key');
    acceptedActionKeys.add(actionKey);
  });
  const grades = new Map();
  scenario.actionLabels.forEach(label => {
    if (!label || !knownGrades.has(label.grade))
      throw new Error('explicit expert action grade is required');
    requireText(label.actionKey, 'action label key');
    if (!candidates.has(label.actionKey)) throw new Error('label references missing candidate');
    if (grades.has(label.actionKey)) throw new Error('duplicate action label');
    grades.set(label.actionKey, label.grade);
  });
  if (grades.size !== candidates.size) throw new Error('every candidate requires an explicit expert action grade');

  const labeled = Array.from(grades.entries())
    .filter(([, grade]) => grade !== 'not-evaluable')
    .map(([actionKey, grade]) => ({ candidate: candidates.get(actionKey), grade }));
  const pairs = [];
  for (let left = 0; left < labeled.length; left++) {
    for (let right = left + 1; right < labeled.length; right++) {
      const first = labeled[left];
      const second = labeled[right];
      if (first.grade === second.grade) continue;
      const preferred = gradeOrder[first.grade] > gradeOrder[second.grade] ? first : second;
      const other = preferred === first ? second : first;
      if (!acceptedActionKeys.has(preferred.candidate.actionKey)) continue;
      pairs.push({
        gameId: scenario.gameId,
        capturedAtUtc: scenario.capturedAtUtc,
        scenarioId: scenario.scenarioId,
        gameBuild: provenance.gameBuild,
        seasonId: provenance.seasonId,
        cardPoolIdentity: provenance.cardPoolIdentity,
        stateFingerprint: provenance.stateFingerprint,
        preferred: preferred.candidate,
        other: other.candidate,
        label: 1
      });
    }
  }
  return pairs;
}

function resolvedProvenance(scenario) {
  const state = scenario.state || {};
  const values = {
    gameBuild: state.gameBuild || scenario.gameBuild,
    seasonId: state.seasonId || scenario.seasonId,
    cardPoolIdentity: state.cardPoolIdentity || scenario.cardPoolIdentity,
    stateFingerprint: scenario.stateFingerprint || state.fingerprint
  };
  Object.keys(values).forEach(key => requireResolved(values[key], key));
  return values;
}

function validateCandidate(candidate) {
  if (!candidate || typeof candidate !== 'object') throw new Error('candidate is required');
  requireText(candidate.actionKey, 'candidate actionKey');
  if (!knownActionTypes.has(candidate.actionType)) throw new Error('candidate actionType');
  const actionKey = parseActionKey(candidate.actionKey);
  if (actionKey.actionType !== candidate.actionType)
    throw new Error('candidate actionKey type must match candidate actionType');
  if (!Array.isArray(candidate.features) || candidate.features.length === 0
    || candidate.features.some(value => !Number.isFinite(value))) throw new Error('candidate features');
  requireResolved(candidate.candidateFingerprint, 'candidateFingerprint');
}

function parseActionKey(value) {
  const parts = value.split('|');
  if (parts.length !== 4 || !knownActionTypes.has(parts[0]))
    throw new Error('candidate actionKey must use the frozen four-part identity');
  if (!/^(?:0|-?[1-9]\d*)$/.test(parts[1]) || !Number.isSafeInteger(Number(parts[1])))
    throw new Error('candidate actionKey TargetIndex must be a canonical integer');
  return { actionType: parts[0] };
}

function sanitizeCandidate(candidate) {
  return {
    actionKey: candidate.actionKey,
    actionType: candidate.actionType,
    candidateFingerprint: candidate.candidateFingerprint,
    features: candidate.features.slice()
  };
}

function parseArguments(args) {
  const values = {};
  for (let index = 0; index < args.length; index += 2) {
    if (!args[index].startsWith('--') || !args[index + 1]) throw new Error('expected --data-root --scenarios --output');
    values[args[index].slice(2)] = args[index + 1];
  }
  if (!values['data-root'] || !values.scenarios || !values.output)
    throw new Error('expected --data-root --scenarios --output');
  return values;
}

function main(args) {
  const options = parseArguments(args);
  const repositoryRoot = path.resolve(__dirname, '..', '..');
  const dataRoot = assertExternalDataRoot(options['data-root'], repositoryRoot);
  const scenarioPath = resolveDataPath(dataRoot, options.scenarios, 'scenarios');
  const outputPath = resolveDataPath(dataRoot, options.output, 'output', { allowMissing: true });
  const scenarios = fs.readFileSync(scenarioPath, 'utf8').trim().split(/\r?\n/).filter(Boolean).map(JSON.parse);
  const pairs = scenarios.flatMap(buildPairs);
  fs.writeFileSync(outputPath, pairs.map(pair => JSON.stringify(pair)).join('\n') + (pairs.length ? '\n' : ''), 'utf8');
}

function requireText(value, name) { if (typeof value !== 'string' || !value.trim()) throw new Error(name + ' is required'); }
function requireResolved(value, name) {
  requireText(value, name);
  if (value.trim().toLowerCase() === 'unknown') throw new Error(name + ' must be version-matched');
}
if (require.main === module) main(process.argv.slice(2));
module.exports = { buildPairs, main };

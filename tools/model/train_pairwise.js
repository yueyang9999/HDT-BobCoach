'use strict';
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { assertExternalDataRoot, resolveDataPath } = require('./contracts');

const actionTypes = Object.freeze([
  'BuyMinion', 'BuySpell', 'Refresh', 'Upgrade', 'SellMinion', 'FreezeShop',
  'UseHeroPower', 'PickTrinket', 'PickDiscover', 'SendGoldToTeammate'
]);

function train(rows, options) {
  const settings = Object.assign({ epochs: 200, learningRate: 0.05, lambda: 0.01, seed: 7 }, options);
  validateSettings(settings);
  const normalizedRows = validateRows(rows);
  const featureCount = normalizedRows[0].preferred.features.length;
  const weights = new Array(featureCount + actionTypes.length).fill(0);
  const random = seededRandom(settings.seed);
  for (let epoch = 0; epoch < settings.epochs; epoch++) {
    const order = normalizedRows.slice();
    for (let index = order.length - 1; index > 0; index--) {
      const other = Math.floor(random() * (index + 1));
      const value = order[index]; order[index] = order[other]; order[other] = value;
    }
    order.forEach(row => {
      const gradient = pairLossGradient(row.preferred, row.other, row.label, weights, settings.lambda);
      for (let index = 0; index < weights.length; index++) weights[index] -= settings.learningRate * gradient[index];
    });
  }
  const payload = Buffer.from(JSON.stringify({
    kind: 'test-only-synthetic-training-result-v1', featureCount, actionTypes, weights
  }), 'utf8');
  return Buffer.from(JSON.stringify({
    kind: 'test-only-synthetic-training-envelope-v1',
    payloadSha256: crypto.createHash('sha256').update(payload).digest('hex'),
    payloadBase64: payload.toString('base64')
  }), 'utf8');
}

function pairLossGradient(preferred, other, label, weights, lambda) {
  const delta = preferred.features.map((value, index) => value - other.features[index]);
  const trainingDelta = delta.concat(oneHotDelta(preferred.actionType, other.actionType));
  const signedMargin = label * dot(weights, trainingDelta);
  const boundedMargin = Math.max(-60, Math.min(60, signedMargin));
  const scale = -label / (1 + Math.exp(boundedMargin));
  return weights.map((weight, index) => scale * trainingDelta[index] + 2 * lambda * weight);
}

function oneHotDelta(preferred, other) {
  const values = new Array(actionTypes.length).fill(0);
  const preferredIndex = actionTypes.indexOf(preferred);
  const otherIndex = actionTypes.indexOf(other);
  if (preferredIndex < 0 || otherIndex < 0) throw new Error('unknown action type');
  values[preferredIndex] += 1;
  values[otherIndex] -= 1;
  return values;
}

function validateRows(rows) {
  if (!Array.isArray(rows) || rows.length === 0) throw new Error('pair rows are required');
  let featureCount = null;
  return rows.map(row => {
    if (!row || (row.label !== 1 && row.label !== -1) || !row.preferred || !row.other)
      throw new Error('invalid pair row');
    [row.preferred, row.other].forEach(candidate => {
      if (!Array.isArray(candidate.features) || candidate.features.length === 0
        || candidate.features.some(value => !Number.isFinite(value))
        || !actionTypes.includes(candidate.actionType)) throw new Error('invalid pair candidate');
      if (featureCount === null) featureCount = candidate.features.length;
      if (candidate.features.length !== featureCount) throw new Error('feature length mismatch');
    });
    return row;
  });
}

function validateSettings(settings) {
  ['epochs', 'learningRate', 'lambda', 'seed'].forEach(key => {
    if (!Number.isFinite(settings[key])) throw new Error('invalid ' + key);
  });
  if (!Number.isInteger(settings.epochs) || settings.epochs < 1 || settings.learningRate <= 0 || settings.lambda < 0)
    throw new Error('invalid training settings');
}

function seededRandom(seed) {
  let value = (seed >>> 0) || 1;
  return () => {
    value ^= value << 13; value >>>= 0;
    value ^= value >>> 17; value >>>= 0;
    value ^= value << 5; value >>>= 0;
    return value / 4294967296;
  };
}

function dot(left, right) { return left.reduce((total, value, index) => total + value * right[index], 0); }

function main(args) {
  const options = parseArguments(args);
  const repositoryRoot = path.resolve(__dirname, '..', '..');
  const dataRoot = assertExternalDataRoot(options['data-root'], repositoryRoot);
  const input = resolveDataPath(dataRoot, options.input, 'input');
  const rows = fs.readFileSync(input, 'utf8').trim().split(/\r?\n/).filter(Boolean).map(JSON.parse);
  if (rows.some(row => !versionMatched(row))) throw new Error('blocked-by-season-data: version-matched expert labels are required');
  train(rows, options);
  throw new Error('blocked-by-season-data: candidate production is disabled until effect acceptance');
}

function parseArguments(args) {
  const values = {};
  for (let index = 0; index < args.length; index += 2) values[args[index].replace(/^--/, '')] = args[index + 1];
  if (!values['data-root'] || !values.input) throw new Error('expected --data-root and --input');
  return values;
}

function versionMatched(row) {
  return row && ['gameBuild', 'seasonId', 'cardPoolIdentity'].every(key =>
    typeof row[key] === 'string' && row[key].trim() && row[key].trim().toLowerCase() !== 'unknown');
}
if (require.main === module) main(process.argv.slice(2));
module.exports = { train, pairLossGradient, oneHotDelta };

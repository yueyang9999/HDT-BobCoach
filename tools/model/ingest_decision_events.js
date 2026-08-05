'use strict';

const childProcess = require('child_process');
const crypto = require('crypto');
const path = require('path');

const { assertExternalDataRoot, resolveDataPath } = require('./contracts');
const { readEventText, indexEvents } = require('./compare_decision_events');

const EVENT_TYPES = [
  'DecisionCreated',
  'PlayerActionObserved',
  'RecommendationIssued',
  'CombatOutcomeObserved',
  'ShadowComparison',
];

const IDENTITY_FIELDS = [
  ['GameBuild', 'gameBuild'],
  ['SeasonId', 'seasonId'],
  ['CardPoolIdentity', 'cardPoolIdentity'],
  ['HeroCardId', 'heroCardId'],
  ['AnomalyId', 'anomalyId'],
  ['CurrentCompGuideTier', 'currentCompGuideTier'],
];

function normalizeIdentity(value) {
  if (value === null || value === undefined || value === 'unknown') return 'unknown';
  if (typeof value !== 'string') return 'unknown';
  return value;
}

function buildAggregate(rawBytes, events, byDecision, inputRel) {
  if (!Buffer.isBuffer(rawBytes)) {
    throw new Error('buildAggregate requires raw input bytes');
  }
  if (!events || !Array.isArray(events)) {
    throw new Error('buildAggregate requires an events array');
  }
  if (!byDecision || typeof byDecision.size !== 'number') {
    throw new Error('buildAggregate requires an indexed decision map');
  }
  if (typeof inputRel !== 'string' || inputRel.length === 0) {
    throw new Error('buildAggregate requires a relative input path');
  }

  const eventTypeCounts = {};
  for (const eventType of EVENT_TYPES) eventTypeCounts[eventType] = 0;
  for (const event of events) {
    if (!Object.prototype.hasOwnProperty.call(eventTypeCounts, event.eventType)) {
      throw new Error(`unknown event type "${event.eventType}"`);
    }
    eventTypeCounts[event.eventType] += 1;
  }

  const identitySets = new Map(
    IDENTITY_FIELDS.map(([, outputName]) => [outputName, new Set()]));
  let trinketOfferDecisionCount = 0;
  let pickTrinketActionCount = 0;
  for (const event of events) {
    if (event.eventType === 'DecisionCreated') {
      const state = event.payload && event.payload.State;
      for (const [sourceName, outputName] of IDENTITY_FIELDS) {
        identitySets.get(outputName).add(
          normalizeIdentity(state ? state[sourceName] : undefined));
      }
      const offer = state && state.TrinketOffer;
      if (Array.isArray(offer) && offer.length > 0) trinketOfferDecisionCount += 1;
    } else if (event.eventType === 'PlayerActionObserved'
        && typeof event.payload.actionKey === 'string'
        && event.payload.actionKey.startsWith('PickTrinket|')) {
      pickTrinketActionCount += 1;
    }
  }

  const identity = {};
  for (const [sourceName, outputName] of IDENTITY_FIELDS) {
    const values = [...identitySets.get(outputName)].sort();
    identity[outputName] = {
      distinctCount: values.length,
      values,
    };
  }

  return {
    schemaVersion: 1,
    inputPath: inputRel,
    inputFileSha256: crypto.createHash('sha256').update(rawBytes).digest('hex'),
    eventCount: events.length,
    decisionCount: byDecision.size,
    eventTypeCounts,
    identity,
    trinketOfferDecisionCount,
    pickTrinketActionCount,
  };
}

function captureExternalInputs(dataRoot, files) {
  const reader = path.join(__dirname, 'secure_read_evidence.ps1');
  const read = childProcess.spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy',
    'Bypass', '-File', reader], {
    input: JSON.stringify({ dataRoot, files }),
    encoding: 'utf8',
    windowsHide: true,
    maxBuffer: 64 * 1024 * 1024,
  });
  if (read.error || read.status !== 0) {
    throw new Error(`secure evidence read failed: ${read.error ? read.error.message : read.stderr.trim()}`);
  }
  let encoded;
  try {
    encoded = JSON.parse(read.stdout);
  } catch (_) {
    throw new Error('secure evidence reader returned invalid JSON');
  }
  const names = Object.keys(files);
  if (!encoded || typeof encoded !== 'object' || Array.isArray(encoded)
      || Object.keys(encoded).length !== names.length) {
    throw new Error('secure evidence reader returned an invalid snapshot');
  }
  const captured = {};
  for (const name of names) {
    if (typeof encoded[name] !== 'string') {
      throw new Error(`secure evidence reader omitted ${name}`);
    }
    captured[name] = Buffer.from(encoded[name], 'base64');
  }
  return captured;
}

function writeAggregate(report, dataRoot, outputPath) {
  const writer = path.join(__dirname, 'secure_write_evidence.ps1');
  const write = childProcess.spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy',
    'Bypass', '-File', writer, '-DataRoot', dataRoot, '-OutputPath', outputPath], {
    input: `${JSON.stringify(report, null, 2)}\n`,
    encoding: 'utf8',
    windowsHide: true,
  });
  if (write.error || write.status !== 0) {
    throw new Error(`secure evidence write failed: ${write.error ? write.error.message : write.stderr.trim()}`);
  }
}

function parseCliArgs(args) {
  const options = {};
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg === '--data-root' || arg === '--input' || arg === '--output') {
      const value = args[index + 1];
      if (!value || value.startsWith('--')) {
        throw new Error(`required option: ${arg} <${arg.slice(2).replace('-', ' ')}>`);
      }
      options[arg.slice(2)] = value;
      index += 1;
    } else {
      throw new Error(`unknown option: ${arg}`);
    }
  }
  return options;
}

function main(argv) {
  const args = parseCliArgs(argv);
  const requiredOptions = [
    ['data-root', 'data root'],
    ['input', 'input'],
    ['output', 'output'],
  ];
  const missing = requiredOptions.filter(([key]) => !args[key]);
  if (missing.length > 0) {
    throw new Error(`required option: ${missing.map(
      ([key, label]) => `--${key} <${label}>`).join(', ')}`);
  }

  const repositoryRoot = path.resolve(__dirname, '..', '..');
  const dataRoot = assertExternalDataRoot(args['data-root'], repositoryRoot);
  const inputPath = resolveDataPath(dataRoot, args['input'], 'input');
  const outputPath = resolveDataPath(dataRoot, args['output'], 'output',
    { allowMissing: true });
  const rawBytes = captureExternalInputs(dataRoot, { input: inputPath }).input;
  const events = readEventText(rawBytes.toString('utf8'), 'input');
  const byDecision = indexEvents(events, 'input');
  const report = buildAggregate(rawBytes, events, byDecision, args['input']);
  writeAggregate(report, dataRoot, outputPath);
  resolveDataPath(dataRoot, args['output'], 'output');
}

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = {
  buildAggregate,
};

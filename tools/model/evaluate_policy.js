'use strict';

const fs = require('fs');
const path = require('path');
const { assertExternalDataRoot } = require('./contracts');

const HEX64 = /^[0-9a-f]{64}$/;
const REQUIRED_PROBES = [
  'model-unavailable',
  'critical-state-missing',
  'unsupported-mechanic',
  'out-of-distribution',
  'low-confidence',
  'no-safe-action',
  'planner-timeout'
];
const REQUIRED_PROBE_OUTCOMES = new Map([
  ['model-unavailable', ['ModelUnavailable', 'NotRequested']],
  ['critical-state-missing', ['CriticalStateMissing', 'NotRequested']],
  ['unsupported-mechanic', ['UnsupportedMechanic', 'NotRequested']],
  ['out-of-distribution', ['OutOfDistribution', 'NotRequested']],
  ['low-confidence', ['LowConfidence', 'NotRequested']],
  ['no-safe-action', ['NoSafeAction', 'NotRequested']],
  ['planner-timeout', ['None', 'TimedOut']]
]);
const CATEGORIES = ['level', 'refresh', 'buy', 'sell', 'freeze', 'play', 'transition'];
const SEVERITIES = new Set(['normal', 'high-impact', 'severe']);
const SEVERITY_WEIGHTS = new Map([['normal', 1], ['high-impact', 2], ['severe', 3]]);
const ACTION_GRADES = new Set(['preferred', 'acceptable', 'weak', 'severe-error', 'not-evaluable']);
const GRADE_RANK = new Map([['preferred', 0], ['acceptable', 1], ['weak', 2], ['severe-error', 3]]);

function fail(message) {
  throw new Error(`invalid evidence: ${message}`);
}

function requireString(value, name) {
  if (typeof value !== 'string' || value.trim() === '') fail(`${name} identity is missing`);
  return value;
}

function requireFingerprint(value, name) {
  if (typeof value !== 'string' || !HEX64.test(value)) fail(`${name} fingerprint is invalid`);
}

function uniqueBy(rows, key, name) {
  if (!Array.isArray(rows) || rows.length === 0) fail(`${name} evidence is empty`);
  const result = new Map();
  for (const row of rows) {
    if (!row || typeof row !== 'object') fail(`${name} row is invalid`);
    const value = requireString(row[key], `${name}.${key}`);
    if (result.has(value)) fail(`duplicate ${name} ${key}: ${value}`);
    result.set(value, row);
  }
  return result;
}

function validatePrediction(row, expectedBenchmark, kind) {
  if (row.evidenceSchemaVersion !== 1) fail(`${kind} schema version mismatch`);
  if (requireString(row.benchmarkId, `${kind}.benchmarkId`) !== expectedBenchmark) {
    fail(`${kind} benchmark identity mismatch`);
  }
  requireString(row.gameId, `${kind}.gameId`);
  requireString(row.policyId, `${kind}.policyId`);
  const modelId = requireString(row.modelId, `${kind}.modelId`);
  const modelChecksum = requireString(row.modelPayloadSha256, `${kind}.modelPayloadSha256`);
  if (kind === 'candidate' && (modelId === 'unknown' || !HEX64.test(modelChecksum))) {
    fail('candidate model identity is not checksum-valid');
  }
  requireFingerprint(row.stateFingerprint, `${kind}.stateFingerprint`);
  requireFingerprint(row.candidateFingerprint, `${kind}.candidateFingerprint`);
  if (!Array.isArray(row.topActionKeys) || row.topActionKeys.length > 3) {
    fail(`${kind} Top-N evidence is invalid`);
  }
  const selected = requireString(row.selectedActionKey, `${kind}.selectedActionKey`);
  if (row.topActionKeys.length === 0 || row.topActionKeys.some(value => typeof value !== 'string' || value === '')
      || new Set(row.topActionKeys).size !== row.topActionKeys.length || row.topActionKeys[0] !== selected) {
    fail(`${kind} selected action and Top-N evidence are inconsistent`);
  }
  requireString(row.fallbackReason, `${kind}.fallbackReason`);
  requireString(row.plannerStatus, `${kind}.plannerStatus`);
}

function validateLatency(evidence, benchmarkId, scenarioIds) {
  if (!evidence || evidence.latencyEvidenceSchemaVersion !== 1) fail('latency schema mismatch');
  if (evidence.benchmarkId !== benchmarkId) fail('latency benchmark identity mismatch');
  if (evidence.measurementsPerPolicyPerScenario !== 10) fail('latency sample count mismatch');
  const rows = uniqueBy(evidence.scenarios, 'scenarioId', 'latency scenario');
  if (rows.size !== scenarioIds.size) fail('latency scenario set mismatch');
  for (const id of scenarioIds) {
    const row = rows.get(id);
    if (!row) fail(`latency scenario is missing: ${id}`);
    for (const name of ['baselineSamplesMilliseconds', 'candidateSamplesMilliseconds']) {
      if (!Array.isArray(row[name]) || row[name].length !== 10
          || row[name].some(value => !Number.isFinite(value) || value < 0)) {
        fail(`latency evidence is invalid for ${id}`);
      }
    }
    for (const name of ['baselineP50Milliseconds', 'baselineP95Milliseconds', 'baselineP99Milliseconds',
      'candidateP50Milliseconds', 'candidateP95Milliseconds', 'candidateP99Milliseconds']) {
      if (!Number.isFinite(row[name]) || row[name] < 0) fail(`latency percentile is invalid for ${id}`);
    }
  }
}

function validateSafety(evidence, benchmarkId) {
  if (!evidence || evidence.safetyEvidenceSchemaVersion !== 1) fail('safety schema mismatch');
  if (evidence.benchmarkId !== benchmarkId) fail('safety benchmark identity mismatch');
  const probes = uniqueBy(evidence.probes, 'probeId', 'safety probe');
  if (probes.size !== REQUIRED_PROBES.length) fail('safety probe set mismatch');
  for (const id of REQUIRED_PROBES) {
    const probe = probes.get(id);
    if (!probe) fail(`safety probe is missing: ${id}`);
    requireFingerprint(probe.stateFingerprint, `safety.${id}.stateFingerprint`);
    requireFingerprint(probe.candidateFingerprint, `safety.${id}.candidateFingerprint`);
    const [fallback, planner] = REQUIRED_PROBE_OUTCOMES.get(id);
    if (probe.expectedFallbackReason !== fallback || probe.actualFallbackReason !== fallback
        || probe.expectedPlannerStatus !== planner || probe.actualPlannerStatus !== planner
        || probe.passed !== true) {
      fail(`safety probe failed: ${id}`);
    }
    if (id === 'planner-timeout'
        && (typeof probe.actualSelectedActionKey !== 'string'
          || probe.actualSelectedActionKey.trim() === ''
          || !Array.isArray(probe.actualTopActionKeys)
          || probe.actualTopActionKeys.length === 0
          || probe.actualTopActionKeys[0] !== probe.actualSelectedActionKey)) {
      fail('planner-timeout safety completion path is missing');
    }
  }
}

function validateLabels(row) {
  if (!CATEGORIES.includes(row.category)) fail(`scenario category is invalid: ${row.category}`);
  if (!SEVERITIES.has(row.severity)) fail(`scenario severity is invalid: ${row.severity}`);
  if (!Array.isArray(row.actionLabels) || row.actionLabels.length === 0) {
    fail('scenario action labels are missing');
  }
  const labels = new Map();
  for (const label of row.actionLabels) {
    if (!label || typeof label !== 'object') fail('scenario action label is invalid');
    const key = requireString(label.actionKey, 'scenario.actionLabels.actionKey');
    const grade = requireString(label.grade, 'scenario.actionLabels.grade');
    if (!ACTION_GRADES.has(grade)) fail(`scenario action grade is invalid: ${grade}`);
    if (labels.has(key)) fail(`duplicate scenario action label: ${key}`);
    labels.set(key, grade);
  }
  if (!Array.isArray(row.acceptedActionKeys) || row.acceptedActionKeys.length === 0
      || new Set(row.acceptedActionKeys).size !== row.acceptedActionKeys.length) {
    fail('scenario accepted-action labels are missing or duplicated');
  }
  for (const key of row.acceptedActionKeys) {
    if (!labels.has(key) || !['preferred', 'acceptable'].includes(labels.get(key))) {
      fail(`accepted action is not backed by an acceptable label: ${key}`);
    }
  }
  return labels;
}

function selectedGrade(prediction, labels, kind) {
  const grade = labels.get(prediction.selectedActionKey);
  if (!grade || grade === 'not-evaluable') {
    fail(`${kind} selected action is not evaluable or labeled`);
  }
  for (const key of prediction.topActionKeys) {
    if (!labels.has(key)) fail(`${kind} Top-N action is not labeled: ${key}`);
  }
  return grade;
}

function rate(count, total) {
  return total === 0 ? 0 : count / total;
}

function percentile(values, fraction) {
  if (values.length === 0) fail('latency samples are empty');
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.max(0, Math.ceil(sorted.length * fraction) - 1)];
}

function calculateMetrics(scenarios, baseline, candidate, safetyEvidence, latencyEvidence) {
  let baselineSevere = 0;
  let candidateSevere = 0;
  let baselineTop1 = 0;
  let candidateTop1 = 0;
  let baselineTop3 = 0;
  let candidateTop3 = 0;
  let baselineRegret = 0;
  let candidateRegret = 0;
  let totalSeverityWeight = 0;
  let overrides = 0;
  let better = 0;
  let neutral = 0;
  let worse = 0;
  const newSevereErrors = [];
  const categoryCounts = new Map(CATEGORIES.map(name => [name, { total: 0, baseline: 0, candidate: 0 }]));

  for (const [id, scenario] of scenarios) {
    const labels = scenario._validatedLabels;
    const left = baseline.get(id);
    const right = candidate.get(id);
    const leftGrade = selectedGrade(left, labels, 'baseline');
    const rightGrade = selectedGrade(right, labels, 'candidate');
    const accepted = new Set(scenario.acceptedActionKeys);
    const leftTop1 = accepted.has(left.topActionKeys[0]);
    const rightTop1 = accepted.has(right.topActionKeys[0]);
    const leftTop3 = left.topActionKeys.some(key => accepted.has(key));
    const rightTop3 = right.topActionKeys.some(key => accepted.has(key));
    baselineSevere += leftGrade === 'severe-error' ? 1 : 0;
    candidateSevere += rightGrade === 'severe-error' ? 1 : 0;
    baselineTop1 += leftTop1 ? 1 : 0;
    candidateTop1 += rightTop1 ? 1 : 0;
    baselineTop3 += leftTop3 ? 1 : 0;
    candidateTop3 += rightTop3 ? 1 : 0;
    const severityWeight = SEVERITY_WEIGHTS.get(scenario.severity);
    const bestAcceptedGrade = Math.min(...scenario.acceptedActionKeys.map(key => GRADE_RANK.get(labels.get(key))));
    baselineRegret += severityWeight * Math.max(0, GRADE_RANK.get(leftGrade) - bestAcceptedGrade);
    candidateRegret += severityWeight * Math.max(0, GRADE_RANK.get(rightGrade) - bestAcceptedGrade);
    totalSeverityWeight += severityWeight;
    const category = categoryCounts.get(scenario.category);
    category.total += 1;
    category.baseline += leftTop1 ? 1 : 0;
    category.candidate += rightTop1 ? 1 : 0;
    if (left.selectedActionKey !== right.selectedActionKey) {
      overrides += 1;
      const comparison = GRADE_RANK.get(rightGrade) - GRADE_RANK.get(leftGrade);
      if (comparison < 0) better += 1;
      else if (comparison === 0) neutral += 1;
      else worse += 1;
    }
    if (leftGrade !== 'severe-error' && rightGrade === 'severe-error') {
      newSevereErrors.push({
        scenarioId: id,
        gameId: scenario.gameId,
        category: scenario.category,
        baselineActionKey: left.selectedActionKey,
        candidateActionKey: right.selectedActionKey
      });
    }
  }

  const total = scenarios.size;
  const baselineSevereRate = rate(baselineSevere, total);
  const candidateSevereRate = rate(candidateSevere, total);
  const perCategoryRegression = {};
  for (const name of CATEGORIES) {
    const value = categoryCounts.get(name);
    if (value.total === 0) fail(`real evidence category is missing: ${name}`);
    perCategoryRegression[name] = rate(value.candidate, value.total) - rate(value.baseline, value.total);
  }
  const baselineSamples = latencyEvidence.scenarios.flatMap(row => row.baselineSamplesMilliseconds);
  const candidateSamples = latencyEvidence.scenarios.flatMap(row => row.candidateSamplesMilliseconds);
  return {
    severeErrorRate: {
      baseline: baselineSevereRate,
      candidate: candidateSevereRate,
      relativeReduction: baselineSevereRate === 0
        ? (candidateSevereRate === 0 ? 0 : -1)
        : (baselineSevereRate - candidateSevereRate) / baselineSevereRate
    },
    acceptedTop1Rate: {
      baseline: rate(baselineTop1, total),
      candidate: rate(candidateTop1, total),
      improvement: rate(candidateTop1 - baselineTop1, total)
    },
    acceptedTop3Rate: {
      baseline: rate(baselineTop3, total),
      candidate: rate(candidateTop3, total),
      improvement: rate(candidateTop3 - baselineTop3, total)
    },
    severityWeightedRegret: {
      baseline: baselineRegret / totalSeverityWeight,
      candidate: candidateRegret / totalSeverityWeight
    },
    perCategoryRegression,
    overridePrecision: {
      overrides,
      better,
      neutral,
      worse,
      precision: rate(better, overrides)
    },
    fallbackPassRate: rate(safetyEvidence.probes.filter(probe => probe.passed === true).length,
      safetyEvidence.probes.length),
    latencyMilliseconds: {
      baseline: { p50: percentile(baselineSamples, 0.5), p95: percentile(baselineSamples, 0.95), p99: percentile(baselineSamples, 0.99) },
      candidate: { p50: percentile(candidateSamples, 0.5), p95: percentile(candidateSamples, 0.95), p99: percentile(candidateSamples, 0.99) }
    },
    newSevereErrors
  };
}

function passesEffectGates(metrics) {
  return metrics.severeErrorRate.relativeReduction >= 0.2
    && metrics.acceptedTop1Rate.improvement >= 0.1
    && metrics.acceptedTop3Rate.improvement >= 0.15
    && Object.values(metrics.perCategoryRegression).every(value => value >= -0.05)
    && metrics.latencyMilliseconds.candidate.p95 <= 10
    && metrics.fallbackPassRate === 1
    && metrics.newSevereErrors.length === 0;
}

function evaluate(input) {
  if (!input || typeof input !== 'object') fail('root object is missing');
  const benchmarkId = requireString(input.benchmarkId, 'benchmarkId');
  if (input.scenarioSchemaVersion !== 1) fail('scenario schema version mismatch');

  const scenarios = uniqueBy(input.scenarios, 'scenarioId', 'scenario');
  const gamePartitions = new Map();
  const fingerprintPairs = new Set();
  for (const row of scenarios.values()) {
    const gameId = requireString(row.gameId, 'scenario.gameId');
    const partition = row.partition == null ? 'unspecified' : requireString(row.partition, 'scenario.partition');
    if (gamePartitions.has(gameId) && gamePartitions.get(gameId) !== partition) {
      fail(`game is split across partitions: ${gameId}`);
    }
    gamePartitions.set(gameId, partition);
    requireString(row.category, 'scenario.category');
    requireString(row.severity, 'scenario.severity');
    requireFingerprint(row.stateFingerprint, 'scenario.stateFingerprint');
    requireFingerprint(row.candidateFingerprint, 'scenario.candidateFingerprint');
    const pair = `${row.stateFingerprint}:${row.candidateFingerprint}`;
    if (fingerprintPairs.has(pair)) fail('duplicate scenario fingerprint pair');
    fingerprintPairs.add(pair);
    row._validatedLabels = validateLabels(row);
  }

  const baseline = uniqueBy(input.baselinePredictions, 'scenarioId', 'baseline prediction');
  const candidate = uniqueBy(input.candidatePredictions, 'scenarioId', 'candidate prediction');
  if (baseline.size !== scenarios.size || candidate.size !== scenarios.size) {
    fail('prediction scenario set mismatch');
  }
  for (const [id, scenario] of scenarios) {
    const left = baseline.get(id);
    const right = candidate.get(id);
    if (!left || !right) fail(`paired prediction is missing for scenario ${id}`);
    validatePrediction(left, benchmarkId, 'baseline');
    validatePrediction(right, benchmarkId, 'candidate');
    if (left.gameId !== scenario.gameId || right.gameId !== scenario.gameId
        || left.stateFingerprint !== scenario.stateFingerprint
        || right.stateFingerprint !== scenario.stateFingerprint
        || left.candidateFingerprint !== scenario.candidateFingerprint
        || right.candidateFingerprint !== scenario.candidateFingerprint) {
      fail(`paired evidence fingerprint or game mismatch for scenario ${id}`);
    }
    selectedGrade(left, scenario._validatedLabels, 'baseline');
    selectedGrade(right, scenario._validatedLabels, 'candidate');
  }

  const baselinePolicyIds = new Set([...baseline.values()].map(row => row.policyId));
  const candidateModels = new Set([...candidate.values()].map(row => `${row.modelId}:${row.modelPayloadSha256}`));
  if (baselinePolicyIds.size !== 1 || !baselinePolicyIds.has('legacy-v1.1.0')) {
    fail('baseline policy identity mismatch');
  }
  if (candidateModels.size !== 1) fail('candidate model identity mismatch');
  validateLatency(input.latencyEvidence, benchmarkId, new Set(scenarios.keys()));
  validateSafety(input.safetyEvidence, benchmarkId);

  const trustworthy = input.evidenceKind === 'real-season-evaluation'
    && input.realTrustworthyVersionMatchedExpertLabels === true
    && scenarios.size >= 300
    && input.shadowComparisonsRepresentative === true
    && input.humanSevereErrorReviewComplete === true
    && !['buildIdentity', 'seasonIdentity', 'cardPoolIdentity']
      .some(name => !input[name] || input[name] === 'unknown');

  const missingPrerequisites = [];
  if (input.evidenceKind !== 'real-season-evaluation') missingPrerequisites.push('real-season-evidence');
  if (input.realTrustworthyVersionMatchedExpertLabels !== true || scenarios.size < 300) {
    missingPrerequisites.push('300-real-trustworthy-version-matched-expert-labels');
  }
  if (input.shadowComparisonsRepresentative !== true) missingPrerequisites.push('representative-shadow-comparisons');
  if (input.humanSevereErrorReviewComplete !== true) missingPrerequisites.push('human-severe-error-review');
  for (const name of ['buildIdentity', 'seasonIdentity', 'cardPoolIdentity']) {
    if (!input[name] || input[name] === 'unknown') missingPrerequisites.push(name);
  }

  const metrics = trustworthy
    ? calculateMetrics(scenarios, baseline, candidate, input.safetyEvidence, input.latencyEvidence)
    : null;
  return {
    evidenceSchemaVersion: 1,
    evidenceKind: input.evidenceKind || 'unknown',
    benchmarkId,
    effectAcceptanceStatus: trustworthy
      ? (passesEffectGates(metrics) ? 'accepted' : 'rejected')
      : 'blocked-by-season-data',
    metrics,
    missingPrerequisites
  };
}

function parseArgs(argv) {
  const allowed = new Set(['--data-root', '--scenarios', '--baseline-evidence', '--candidate-evidence',
    '--latency-evidence', '--safety-evidence', '--output']);
  const result = {};
  for (let index = 0; index < argv.length; index += 2) {
    const key = argv[index];
    if (!allowed.has(key) || index + 1 >= argv.length) fail(`unsupported command argument: ${key}`);
    result[key] = argv[index + 1];
  }
  for (const key of allowed) if (!result[key]) fail(`required command argument is missing: ${key}`);
  return result;
}

function resolveBelow(root, relativePath, name) {
  if (path.isAbsolute(relativePath)) fail(`${name} must be relative to data root`);
  const resolved = path.resolve(root, relativePath);
  const prefix = root.endsWith(path.sep) ? root : `${root}${path.sep}`;
  if (!resolved.startsWith(prefix)) fail(`${name} escapes data root`);
  return resolved;
}

function readJsonLines(file) {
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
}

function readScenarioBundle(file, benchmarkId) {
  const scenarios = readJsonLines(file);
  if (scenarios.length === 0) fail('scenario evidence is empty');
  const first = scenarios[0];
  const contextNames = ['evidenceKind', 'buildIdentity', 'seasonIdentity', 'cardPoolIdentity'];
  for (const row of scenarios) {
    if (row.scenarioSchemaVersion !== 1) fail('scenario schema version mismatch');
    if (row.benchmarkId !== benchmarkId) fail('scenario benchmark identity mismatch');
    for (const name of contextNames) {
      if (requireString(row[name], `scenario.${name}`) !== first[name]) {
        fail(`mixed scenario ${name}`);
      }
    }
    for (const name of ['realTrustworthyVersionMatchedExpertLabels', 'shadowComparisonsRepresentative',
      'humanSevereErrorReviewComplete']) {
      if (typeof row[name] !== 'boolean' || row[name] !== first[name]) fail(`mixed or missing scenario ${name}`);
    }
  }
  return {
    evidenceKind: first.evidenceKind,
    benchmarkId,
    scenarioSchemaVersion: 1,
    buildIdentity: first.buildIdentity,
    seasonIdentity: first.seasonIdentity,
    cardPoolIdentity: first.cardPoolIdentity,
    realTrustworthyVersionMatchedExpertLabels: first.realTrustworthyVersionMatchedExpertLabels,
    shadowComparisonsRepresentative: first.shadowComparisonsRepresentative,
    humanSevereErrorReviewComplete: first.humanSevereErrorReviewComplete,
    scenarios
  };
}

function main(argv) {
  const args = parseArgs(argv);
  const dataRoot = assertExternalDataRoot(args['--data-root'], path.resolve(__dirname, '..', '..'));
  const baselinePredictions = readJsonLines(resolveBelow(dataRoot, args['--baseline-evidence'], 'baseline evidence'));
  const benchmarkId = requireString(baselinePredictions[0] && baselinePredictions[0].benchmarkId,
    'baseline benchmarkId');
  const fixture = {
    ...readScenarioBundle(resolveBelow(dataRoot, args['--scenarios'], 'scenarios'), benchmarkId),
    baselinePredictions,
    candidatePredictions: readJsonLines(resolveBelow(dataRoot, args['--candidate-evidence'], 'candidate evidence')),
    latencyEvidence: JSON.parse(fs.readFileSync(resolveBelow(dataRoot, args['--latency-evidence'], 'latency evidence'), 'utf8')),
    safetyEvidence: JSON.parse(fs.readFileSync(resolveBelow(dataRoot, args['--safety-evidence'], 'safety evidence'), 'utf8'))
  };
  const output = resolveBelow(dataRoot, args['--output'], 'output');
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, `${JSON.stringify(evaluate(fixture), null, 2)}\n`, 'utf8');
}

module.exports = { evaluate };
if (require.main === module) main(process.argv.slice(2));

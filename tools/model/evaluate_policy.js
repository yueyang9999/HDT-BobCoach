'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
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
const FALLBACK_REASONS = new Set(['None', 'ModelUnavailable', 'CriticalStateMissing',
  'UnsupportedMechanic', 'OutOfDistribution', 'LowConfidence', 'NoSafeAction']);
const PLANNER_STATUSES = new Set(['NotRequested', 'Completed', 'TimedOut', 'Unsupported']);
const MISSING_PROTOCOL = 'owner-approved-versioned-frozen-checksum-bound-evaluation-protocol';
const MISSING_PROVENANCE = 'owner-approved-independent-provenance-trust-root';
const APPROVAL_REGISTRY_FILE = path.join(__dirname, 'evaluation_protocol_approvals.json');
const PROTOCOL_VERSION = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const UTC_TIMESTAMP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/;
const EXTERNAL_PROTOCOL_SOURCE = Symbol('external-evaluation-protocol');
const EFFECT_THRESHOLD_BINDINGS = [
  ['minimumSevereErrorRelativeReduction', metrics => metrics.severeErrorRate.relativeReduction, 'minimum'],
  ['minimumAcceptedTop1Improvement', metrics => metrics.acceptedTop1Rate.improvement, 'minimum'],
  ['minimumAcceptedTop3Improvement', metrics => metrics.acceptedTop3Rate.improvement, 'minimum'],
  ['minimumPerCategoryTop1Regression', metrics => Math.min(...Object.values(metrics.perCategoryRegression)), 'minimum'],
  ['maximumCandidateP95LatencyMilliseconds', metrics => metrics.latencyMilliseconds.candidate.p95, 'maximum'],
  ['requiredFallbackPassRate', metrics => metrics.fallbackPassRate, 'minimum'],
  ['maximumNewSevereErrors', metrics => metrics.newSevereErrors.length, 'maximum']
];

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

function isFiniteInRange(value, minimum, maximum) {
  return Number.isFinite(value) && value >= minimum && value <= maximum;
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
  const policyId = requireString(row.policyId, `${kind}.policyId`);
  const modelId = requireString(row.modelId, `${kind}.modelId`);
  const modelChecksum = requireString(row.modelPayloadSha256, `${kind}.modelPayloadSha256`);
  if (kind === 'baseline') {
    if (policyId !== 'legacy-v1.1.0' || modelId !== 'unknown' || modelChecksum !== 'unknown') {
      fail('baseline policy or model identity mismatch');
    }
  } else if (policyId !== 'unified-linear-v1'
      || modelId === 'unknown' || !HEX64.test(modelChecksum)) {
    fail('candidate policy or model identity is not checksum-valid');
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
  if (!isFiniteInRange(row.confidence, 0, 1)) fail(`${kind} confidence is invalid`);
  const fallbackReason = requireString(row.fallbackReason, `${kind}.fallbackReason`);
  const plannerStatus = requireString(row.plannerStatus, `${kind}.plannerStatus`);
  if (!FALLBACK_REASONS.has(fallbackReason)) fail(`${kind} fallback reason is invalid`);
  if (!PLANNER_STATUSES.has(plannerStatus)) fail(`${kind} planner status is invalid`);
}

function validateLatency(evidence, benchmarkId, scenarioIds, candidateIdentity) {
  if (!evidence || evidence.latencyEvidenceSchemaVersion !== 1) fail('latency schema mismatch');
  if (evidence.benchmarkId !== benchmarkId) fail('latency benchmark identity mismatch');
  if (evidence.baselinePolicyId !== 'legacy-v1.1.0'
      || evidence.candidatePolicyId !== 'unified-linear-v1'
      || evidence.candidateModelId !== candidateIdentity.modelId
      || evidence.candidateModelPayloadSha256 !== candidateIdentity.modelPayloadSha256) {
    fail('latency policy or model identity mismatch');
  }
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
    const expectedModelSource = id === 'model-unavailable' ? 'none' : 'candidate';
    let semanticsPass = probe.expectedFallbackReason === fallback
      && probe.actualFallbackReason === fallback
      && probe.expectedPlannerStatus === planner
      && probe.actualPlannerStatus === planner
      && probe.modelSource === expectedModelSource
      && FALLBACK_REASONS.has(probe.actualFallbackReason)
      && PLANNER_STATUSES.has(probe.actualPlannerStatus)
      && Array.isArray(probe.actualBestCompletePath);
    if (id === 'planner-timeout') {
      semanticsPass = semanticsPass
        && typeof probe.actualSelectedActionKey === 'string'
        && probe.actualSelectedActionKey.trim() !== ''
        && Array.isArray(probe.actualTopActionKeys)
        && probe.actualTopActionKeys.length > 0
        && probe.actualTopActionKeys[0] === probe.actualSelectedActionKey
        && probe.actualBestCompletePath.length > 0
        && probe.actualBestCompletePath[0] === probe.actualSelectedActionKey;
    } else {
      semanticsPass = semanticsPass && probe.actualBestCompletePath.length === 0;
    }
    if (probe.passed !== semanticsPass || !semanticsPass) fail(`safety probe failed: ${id}`);
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

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort()
      .map(key => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function sha256Hex(value) {
  return crypto.createHash('sha256').update(value, 'utf8').digest('hex');
}

function hasEngineeringOnlyIdentity(input, candidateIdentity) {
  const identities = [input.evidenceKind, input.buildIdentity, input.seasonIdentity,
    input.cardPoolIdentity, candidateIdentity.modelId];
  for (const scenario of input.scenarios) {
    identities.push(scenario.scenarioId, scenario.gameId,
      scenario.state && scenario.state.gameBuild,
      scenario.state && scenario.state.seasonId,
      scenario.state && scenario.state.cardPoolIdentity);
  }
  return input.evidenceKind !== 'real-season-evaluation'
    || identities.some(value => typeof value === 'string'
      && /(synthetic|prior-season|previous-season)/i.test(value));
}

function validateThresholds(thresholds) {
  if (!thresholds || typeof thresholds !== 'object') return null;
  const ranges = [
    ['minimumSevereErrorRelativeReduction', -1, 1],
    ['minimumAcceptedTop1Improvement', -1, 1],
    ['minimumAcceptedTop3Improvement', -1, 1],
    ['minimumPerCategoryTop1Regression', -1, 1],
    ['maximumCandidateP95LatencyMilliseconds', 0, Number.MAX_VALUE],
    ['requiredFallbackPassRate', 0, 1]
  ];
  for (const [name, minimum, maximum] of ranges) {
    if (!isFiniteInRange(thresholds[name], minimum, maximum)) return null;
  }
  if (!Number.isInteger(thresholds.maximumNewSevereErrors)
      || thresholds.maximumNewSevereErrors < 0) return null;
  return thresholds;
}

function isResolvedProtocolIdentity(value) {
  return typeof value === 'string' && value.trim() !== ''
    && value.trim().toLowerCase() !== 'unknown'
    && !/^synthetic(?:-|$)/i.test(value.trim());
}

function validateEvaluationProtocol(context, input, candidateIdentity) {
  try {
    if (!context || context.source !== EXTERNAL_PROTOCOL_SOURCE) return null;
    const protocol = context.protocol;
    if (!protocol || typeof protocol !== 'object' || Array.isArray(protocol)) return null;
    if (protocol.protocolSchemaVersion !== 1
        || !isResolvedProtocolIdentity(protocol.protocolId)
        || typeof protocol.protocolVersion !== 'string'
        || !PROTOCOL_VERSION.test(protocol.protocolVersion)
        || protocol.approvalStatus !== 'owner-approved'
        || !isResolvedProtocolIdentity(protocol.approvedBy)) return null;
    const approvedAt = Date.parse(protocol.approvedAtUtc);
    const frozenAt = Date.parse(protocol.frozenAtUtc);
    const holdoutOpenedAt = Date.parse(protocol.holdoutOpenedAtUtc);
    if (![protocol.approvedAtUtc, protocol.frozenAtUtc, protocol.holdoutOpenedAtUtc]
      .every(value => typeof value === 'string' && UTC_TIMESTAMP.test(value))
        || ![approvedAt, frozenAt, holdoutOpenedAt].every(Number.isFinite)
        || approvedAt > frozenAt || frozenAt > holdoutOpenedAt) return null;
    if (protocol.benchmarkId !== 'season-2026-next-v1'
        || protocol.benchmarkId !== input.benchmarkId
        || protocol.buildIdentity !== input.buildIdentity
        || protocol.seasonIdentity !== input.seasonIdentity
        || protocol.cardPoolIdentity !== input.cardPoolIdentity
        || protocol.baselinePolicyId !== 'legacy-v1.1.0'
        || protocol.candidatePolicyId !== 'unified-linear-v1'
        || protocol.candidateModelId !== candidateIdentity.modelId
        || protocol.candidateModelPayloadSha256 !== candidateIdentity.modelPayloadSha256
        || protocol.holdoutChecksumSha256 !== context.holdoutFileSha256
        || !isResolvedProtocolIdentity(protocol.buildIdentity)
        || !isResolvedProtocolIdentity(protocol.seasonIdentity)
        || !isResolvedProtocolIdentity(protocol.cardPoolIdentity)
        || !isResolvedProtocolIdentity(protocol.candidateModelId)
        || !HEX64.test(protocol.candidateModelPayloadSha256)
        || !HEX64.test(protocol.holdoutChecksumSha256)
        || !HEX64.test(protocol.protocolSha256)
        || !HEX64.test(context.protocolFileSha256)
        || protocol.protocolSha256 !== context.protocolCanonicalSha256) return null;
    if (!isResolvedProtocolIdentity(protocol.holdoutIdentity)
        || !isResolvedProtocolIdentity(protocol.trainingSetIdentity)
        || !isResolvedProtocolIdentity(protocol.seasonRange)) return null;
    const calibration = protocol.confidenceCalibration;
    const calibrationApproval = protocol.calibrationApproval;
    if (!calibration || typeof calibration !== 'object' || Array.isArray(calibration)
        || !calibrationApproval || typeof calibrationApproval !== 'object'
        || Array.isArray(calibrationApproval)
        || calibration.status !== 'accepted'
        || calibration.gameBuild !== input.buildIdentity
        || calibration.seasonId !== input.seasonIdentity
        || calibration.cardPoolIdentity !== input.cardPoolIdentity
        || calibration.trainingSetIdentity !== protocol.trainingSetIdentity
        || calibration.holdoutIdentity !== protocol.holdoutIdentity
        || calibration.modelId !== candidateIdentity.modelId
        || !isResolvedProtocolIdentity(calibration.gameBuild)
        || !isResolvedProtocolIdentity(calibration.seasonId)
        || !isResolvedProtocolIdentity(calibration.cardPoolIdentity)
        || !isResolvedProtocolIdentity(calibration.trainingSetIdentity)
        || !isResolvedProtocolIdentity(calibration.holdoutIdentity)
        || !isResolvedProtocolIdentity(calibration.calibrationIdentity)
        || !isResolvedProtocolIdentity(calibration.modelId)
        || !Number.isFinite(calibration.marginScale) || calibration.marginScale <= 0
        || !isFiniteInRange(calibration.semanticCoverageFloor, 0, 1)
        || !isFiniteInRange(calibration.confidenceThreshold, 0, 1)
        || (calibration.planningThreshold !== null
          && !isFiniteInRange(calibration.planningThreshold, 0, 1))
        || calibrationApproval.holdoutIdentity !== protocol.holdoutIdentity
        || calibrationApproval.calibrationIdentity !== calibration.calibrationIdentity
        || calibrationApproval.modelId !== candidateIdentity.modelId
        || !isResolvedProtocolIdentity(calibrationApproval.holdoutIdentity)
        || !isResolvedProtocolIdentity(calibrationApproval.calibrationIdentity)
        || !isResolvedProtocolIdentity(calibrationApproval.modelId)) return null;
    const thresholds = validateThresholds(protocol.thresholds);
    return thresholds ? { protocol, thresholds } : null;
  } catch (_) {
    return null;
  }
}

function loadApprovalRegistry() {
  const bytes = fs.readFileSync(APPROVAL_REGISTRY_FILE);
  const text = bytes.toString('utf8');
  rejectDuplicateJsonProperties(text);
  const registry = JSON.parse(text);
  if (!registry || registry.registrySchemaVersion !== 1
      || !Array.isArray(registry.approvals)) {
    fail('evaluation approval registry is invalid');
  }
  const identities = new Set();
  for (const approval of registry.approvals) {
    if (!approval || typeof approval !== 'object' || Array.isArray(approval)
        || !isResolvedProtocolIdentity(approval.approvalId)
        || typeof approval.approvalVersion !== 'string'
        || !PROTOCOL_VERSION.test(approval.approvalVersion)
        || approval.approvalStatus !== 'owner-approved'
        || !isResolvedProtocolIdentity(approval.approvedBy)
        || typeof approval.approvedAtUtc !== 'string'
        || !UTC_TIMESTAMP.test(approval.approvedAtUtc)
        || !Number.isFinite(Date.parse(approval.approvedAtUtc))) {
      fail('evaluation approval registry entry is invalid');
    }
    for (const name of ['protocolId', 'protocolVersion', 'candidateModelId', 'benchmarkId',
      'buildIdentity', 'seasonIdentity', 'cardPoolIdentity', 'trainingSetIdentity',
      'holdoutIdentity', 'seasonRange', 'calibrationIdentity']) {
      if (!isResolvedProtocolIdentity(approval[name])) {
        fail(`evaluation approval registry ${name} is invalid`);
      }
    }
    for (const name of ['protocolFileSha256', 'protocolCanonicalSha256',
      'holdoutLabelsFileSha256', 'candidateModelPayloadSha256']) {
      if (!HEX64.test(approval[name])) {
        fail(`evaluation approval registry ${name} is invalid`);
      }
    }
    const identity = `${approval.approvalId}:${approval.approvalVersion}`;
    if (identities.has(identity)) fail('duplicate evaluation approval registry identity');
    identities.add(identity);
  }
  return {
    approvals: registry.approvals,
    registrySha256: crypto.createHash('sha256').update(bytes).digest('hex')
  };
}

function findApprovedProvenance(context, validatedProtocol, candidateIdentity) {
  if (!context || !validatedProtocol) return null;
  const protocol = validatedProtocol.protocol;
  const registry = loadApprovalRegistry();
  const approval = registry.approvals.find(entry =>
    entry.protocolId === protocol.protocolId
      && entry.protocolVersion === protocol.protocolVersion
      && entry.protocolFileSha256 === context.protocolFileSha256
      && entry.protocolCanonicalSha256 === context.protocolCanonicalSha256
      && entry.holdoutLabelsFileSha256 === context.holdoutFileSha256
      && entry.candidateModelId === candidateIdentity.modelId
      && entry.candidateModelPayloadSha256 === candidateIdentity.modelPayloadSha256
      && entry.benchmarkId === protocol.benchmarkId
      && entry.buildIdentity === protocol.buildIdentity
      && entry.seasonIdentity === protocol.seasonIdentity
      && entry.cardPoolIdentity === protocol.cardPoolIdentity
      && entry.trainingSetIdentity === protocol.trainingSetIdentity
      && entry.holdoutIdentity === protocol.holdoutIdentity
      && entry.seasonRange === protocol.seasonRange
      && entry.calibrationIdentity === protocol.confidenceCalibration.calibrationIdentity);
  return approval ? { approval, registrySha256: registry.registrySha256 } : null;
}

function meetsFrozenProtocolThresholds(metrics, thresholds) {
  return EFFECT_THRESHOLD_BINDINGS.every(([name, readMetric, direction]) => {
    const value = readMetric(metrics);
    return direction === 'minimum' ? value >= thresholds[name] : value <= thresholds[name];
  });
}

function evaluate(input, protocolContext = null) {
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

  const candidateModels = new Set([...candidate.values()].map(row => `${row.modelId}:${row.modelPayloadSha256}`));
  if (candidateModels.size !== 1) fail('candidate model identity mismatch');
  const firstCandidate = candidate.values().next().value;
  const candidateIdentity = {
    modelId: firstCandidate.modelId,
    modelPayloadSha256: firstCandidate.modelPayloadSha256
  };
  validateLatency(input.latencyEvidence, benchmarkId, new Set(scenarios.keys()), candidateIdentity);
  validateSafety(input.safetyEvidence, benchmarkId);

  const engineeringOnly = hasEngineeringOnlyIdentity(input, candidateIdentity);
  const protocol = validateEvaluationProtocol(protocolContext, input, candidateIdentity);
  const approvedProvenance = findApprovedProvenance(protocolContext, protocol, candidateIdentity);
  const trustworthy = !engineeringOnly
    && input.realTrustworthyVersionMatchedExpertLabels === true
    && scenarios.size >= 300
    && input.shadowComparisonsRepresentative === true
    && input.humanSevereErrorReviewComplete === true
    && protocol !== null
    && approvedProvenance !== null
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
  if (!protocol) missingPrerequisites.push(MISSING_PROTOCOL);
  if (!approvedProvenance) missingPrerequisites.push(MISSING_PROVENANCE);

  const metrics = trustworthy
    ? calculateMetrics(scenarios, baseline, candidate, input.safetyEvidence, input.latencyEvidence)
    : null;
  return {
    evidenceSchemaVersion: 1,
    evidenceKind: input.evidenceKind || 'unknown',
    benchmarkId,
    effectAcceptanceStatus: trustworthy
      ? (meetsFrozenProtocolThresholds(metrics, protocol.thresholds) ? 'accepted' : 'rejected')
      : 'blocked-by-season-data',
    evaluationProtocol: protocol ? {
      protocolId: protocol.protocol.protocolId,
      protocolVersion: protocol.protocol.protocolVersion,
      protocolSha256: protocol.protocol.protocolSha256,
      protocolFileSha256: protocolContext.protocolFileSha256,
      holdoutChecksumSha256: protocol.protocol.holdoutChecksumSha256,
      provenanceApprovalId: approvedProvenance
        ? approvedProvenance.approval.approvalId : null,
      approvalRegistrySha256: approvedProvenance
        ? approvedProvenance.registrySha256 : null
    } : null,
    metrics,
    missingPrerequisites
  };
}

function parseArgs(argv) {
  const allowed = new Set(['--data-root', '--scenarios', '--baseline-evidence', '--candidate-evidence',
    '--latency-evidence', '--safety-evidence', '--evaluation-protocol', '--output']);
  const required = [...allowed].filter(key => key !== '--evaluation-protocol');
  const result = {};
  for (let index = 0; index < argv.length; index += 2) {
    const key = argv[index];
    if (!allowed.has(key) || index + 1 >= argv.length) fail(`unsupported command argument: ${key}`);
    result[key] = argv[index + 1];
  }
  for (const key of required) if (!result[key]) fail(`required command argument is missing: ${key}`);
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

function rejectDuplicateJsonProperties(text) {
  let index = 0;
  const skipWhitespace = () => {
    while (/\s/.test(text[index] || '')) index += 1;
  };
  const parseString = () => {
    const start = index;
    index += 1;
    let escaped = false;
    while (index < text.length) {
      const character = text[index++];
      if (escaped) {
        escaped = false;
      } else if (character === '\\') {
        escaped = true;
      } else if (character === '"') {
        return JSON.parse(text.slice(start, index));
      }
    }
    fail('evaluation protocol JSON is invalid');
  };
  const parseValue = () => {
    skipWhitespace();
    if (text[index] === '{') {
      parseObject();
      return;
    }
    if (text[index] === '[') {
      index += 1;
      skipWhitespace();
      if (text[index] === ']') {
        index += 1;
        return;
      }
      while (index < text.length) {
        parseValue();
        skipWhitespace();
        if (text[index] === ']') {
          index += 1;
          return;
        }
        if (text[index++] !== ',') fail('evaluation protocol JSON is invalid');
      }
      fail('evaluation protocol JSON is invalid');
    }
    if (text[index] === '"') {
      parseString();
      return;
    }
    const start = index;
    while (index < text.length && !/[\s,\]}]/.test(text[index])) index += 1;
    if (start === index) fail('evaluation protocol JSON is invalid');
  };
  const parseObject = () => {
    index += 1;
    const keys = new Set();
    skipWhitespace();
    if (text[index] === '}') {
      index += 1;
      return;
    }
    while (index < text.length) {
      skipWhitespace();
      if (text[index] !== '"') fail('evaluation protocol JSON is invalid');
      const key = parseString();
      if (keys.has(key)) fail(`evaluation protocol has duplicate property: ${key}`);
      keys.add(key);
      skipWhitespace();
      if (text[index++] !== ':') fail('evaluation protocol JSON is invalid');
      parseValue();
      skipWhitespace();
      if (text[index] === '}') {
        index += 1;
        return;
      }
      if (text[index++] !== ',') fail('evaluation protocol JSON is invalid');
    }
    fail('evaluation protocol JSON is invalid');
  };

  skipWhitespace();
  parseValue();
  skipWhitespace();
  if (index !== text.length) fail('evaluation protocol JSON is invalid');
}

function loadExternalProtocolContext(protocolFile, scenariosFile) {
  const protocolBytes = fs.readFileSync(protocolFile);
  const protocolText = protocolBytes.toString('utf8');
  rejectDuplicateJsonProperties(protocolText);
  const protocol = JSON.parse(protocolText);
  if (!protocol || typeof protocol !== 'object' || Array.isArray(protocol)) {
    fail('evaluation protocol root is invalid');
  }
  const canonicalPayload = { ...protocol };
  delete canonicalPayload.protocolSha256;
  return {
    source: EXTERNAL_PROTOCOL_SOURCE,
    protocol,
    protocolFileSha256: crypto.createHash('sha256').update(protocolBytes).digest('hex'),
    protocolCanonicalSha256: sha256Hex(canonicalJson(canonicalPayload)),
    holdoutFileSha256: crypto.createHash('sha256').update(fs.readFileSync(scenariosFile)).digest('hex')
  };
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
  const scenariosFile = resolveBelow(dataRoot, args['--scenarios'], 'scenarios');
  const baselinePredictions = readJsonLines(resolveBelow(dataRoot, args['--baseline-evidence'], 'baseline evidence'));
  const benchmarkId = requireString(baselinePredictions[0] && baselinePredictions[0].benchmarkId,
    'baseline benchmarkId');
  const fixture = {
    ...readScenarioBundle(scenariosFile, benchmarkId),
    baselinePredictions,
    candidatePredictions: readJsonLines(resolveBelow(dataRoot, args['--candidate-evidence'], 'candidate evidence')),
    latencyEvidence: JSON.parse(fs.readFileSync(resolveBelow(dataRoot, args['--latency-evidence'], 'latency evidence'), 'utf8')),
    safetyEvidence: JSON.parse(fs.readFileSync(resolveBelow(dataRoot, args['--safety-evidence'], 'safety evidence'), 'utf8'))
  };
  const protocolContext = args['--evaluation-protocol']
    ? loadExternalProtocolContext(
      resolveBelow(dataRoot, args['--evaluation-protocol'], 'evaluation protocol'), scenariosFile)
    : null;
  const output = resolveBelow(dataRoot, args['--output'], 'output');
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, `${JSON.stringify(evaluate(fixture, protocolContext), null, 2)}\n`, 'utf8');
}

module.exports = { evaluate, loadExternalProtocolContext };
if (require.main === module) main(process.argv.slice(2));

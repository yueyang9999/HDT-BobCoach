'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { assertExternalDataRoot, resolveDataPath } = require('./contracts');

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
const TRUSTED_OWNER_KEYS_FILE = path.join(__dirname, 'owner_approval_trusted_keys.json');
const PROTOCOL_VERSION = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const UTC_TIMESTAMP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/;
const EXTERNAL_PROTOCOL_SOURCE = Symbol('external-evaluation-protocol');
const TRUSTED_PROTOCOL_CONTEXTS = new WeakMap();
const TRUSTED_EVIDENCE_CONTEXTS = new WeakMap();
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
      || new Set(row.topActionKeys).size !== row.topActionKeys.length) {
    fail(`${kind} selected action and Top-N evidence are inconsistent`);
  }
  if (!isFiniteInRange(row.confidence, 0, 1)) fail(`${kind} confidence is invalid`);
  const fallbackReason = requireString(row.fallbackReason, `${kind}.fallbackReason`);
  const plannerStatus = requireString(row.plannerStatus, `${kind}.plannerStatus`);
  if (!FALLBACK_REASONS.has(fallbackReason)) fail(`${kind} fallback reason is invalid`);
  if (!PLANNER_STATUSES.has(plannerStatus)) fail(`${kind} planner status is invalid`);
  if (!Array.isArray(row.bestCompletePath)
      || row.bestCompletePath.some(value => typeof value !== 'string' || value === '')) {
    fail(`${kind} planning path evidence is invalid`);
  }
  if (plannerStatus === 'NotRequested') {
    if (row.bestCompletePath.length !== 0 || row.topActionKeys[0] !== selected) {
      fail(`${kind} non-planner action evidence is inconsistent`);
    }
  } else if (plannerStatus === 'Completed' || plannerStatus === 'TimedOut') {
    if (row.bestCompletePath.length === 0 || row.bestCompletePath[0] !== selected) {
      fail(`${kind} planning path and selected action are inconsistent`);
    }
  }
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

function deepFreeze(value) {
  if (!value || typeof value !== 'object' || Object.isFrozen(value)) return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
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

function validateEvaluationProtocol(context, input, candidateIdentity, holdoutCanonicalSha256) {
  try {
    const trustedContext = context && TRUSTED_PROTOCOL_CONTEXTS.get(context);
    if (!trustedContext || context.source !== EXTERNAL_PROTOCOL_SOURCE) return null;
    if (trustedContext.holdoutCanonicalSha256 !== holdoutCanonicalSha256) return null;
    const protocol = trustedContext.protocol;
    const canonicalPayload = { ...protocol };
    delete canonicalPayload.protocolSha256;
    if (sha256Hex(canonicalJson(canonicalPayload)) !== trustedContext.protocolCanonicalSha256) return null;
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
        || protocol.holdoutChecksumSha256 !== trustedContext.holdoutFileSha256
        || !isResolvedProtocolIdentity(protocol.buildIdentity)
        || !isResolvedProtocolIdentity(protocol.seasonIdentity)
        || !isResolvedProtocolIdentity(protocol.cardPoolIdentity)
        || !isResolvedProtocolIdentity(protocol.candidateModelId)
        || !HEX64.test(protocol.candidateModelPayloadSha256)
        || !HEX64.test(protocol.holdoutChecksumSha256)
        || !HEX64.test(protocol.protocolSha256)
        || !HEX64.test(trustedContext.protocolFileSha256)
        || protocol.protocolSha256 !== trustedContext.protocolCanonicalSha256) return null;
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
    return thresholds ? { protocol, thresholds, trustedContext } : null;
  } catch (_) {
    return null;
  }
}

function verifyDetachedOwnerApproval(artifact, trustedKeys) {
  try {
    if (!artifact || artifact.ownerApprovalSchemaVersion !== 1
        || artifact.signatureAlgorithm !== 'ed25519'
        || !isResolvedProtocolIdentity(artifact.keyId)
        || !artifact.payload || typeof artifact.payload !== 'object'
        || Array.isArray(artifact.payload)
        || typeof artifact.signatureBase64 !== 'string') return false;
    const key = Array.isArray(trustedKeys)
      ? trustedKeys.find(value => value && value.keyId === artifact.keyId) : null;
    if (!key || key.algorithm !== 'ed25519' || typeof key.publicKeySpkiPem !== 'string') return false;
    const signature = Buffer.from(artifact.signatureBase64, 'base64');
    if (signature.length !== 64 || signature.toString('base64') !== artifact.signatureBase64) return false;
    return crypto.verify(null, Buffer.from(canonicalJson(artifact.payload), 'utf8'),
      key.publicKeySpkiPem, signature);
  } catch (_) {
    return false;
  }
}

function loadTrustedOwnerKeys() {
  const keySet = readBoundJson(TRUSTED_OWNER_KEYS_FILE, 'trusted owner key set').value;
  if (!keySet || keySet.trustedKeySetSchemaVersion !== 1 || !Array.isArray(keySet.keys)) {
    fail('trusted owner key set is invalid');
  }
  const ids = new Set();
  for (const key of keySet.keys) {
    if (!key || !isResolvedProtocolIdentity(key.keyId) || key.algorithm !== 'ed25519'
        || typeof key.publicKeySpkiPem !== 'string' || ids.has(key.keyId)) {
      fail('trusted owner key entry is invalid');
    }
    ids.add(key.keyId);
  }
  return keySet.keys;
}

function exactStringSet(values, expected) {
  return Array.isArray(values) && values.every(value => typeof value === 'string' && value !== '')
    && values.length === new Set(values).size
    && values.length === expected.size
    && values.every(value => expected.has(value));
}

function validUtc(value) {
  return typeof value === 'string' && UTC_TIMESTAMP.test(value)
    && Number.isFinite(Date.parse(value));
}

function validateEvidenceManifests(context, input, scenarios, baseline, candidate,
  candidateIdentity, validatedProtocol, preliminaryMetrics) {
  const trusted = context && TRUSTED_EVIDENCE_CONTEXTS.get(context);
  if (!trusted || !validatedProtocol || !preliminaryMetrics
      || !isEvidenceContextBoundToInput(context, input)) return null;
  const scenarioIds = new Set(scenarios.keys());
  const commonIdentityMatches = manifest => manifest
    && manifest.buildIdentity === input.buildIdentity
    && manifest.seasonIdentity === input.seasonIdentity
    && manifest.cardPoolIdentity === input.cardPoolIdentity;

  const labels = trusted.labelProvenance;
  const labelRecords = labels && Array.isArray(labels.labelRecords) ? labels.labelRecords : [];
  if (!commonIdentityMatches(labels) || labels.labelProvenanceSchemaVersion !== 1
      || labels.evidenceKind !== 'real-season-evaluation'
      || labels.holdoutFileSha256 !== validatedProtocol.trustedContext.holdoutFileSha256
      || labels.holdoutCanonicalSha256 !== trusted.holdoutCanonicalSha256
      || !exactStringSet(labels.scenarioIds, scenarioIds)
      || !exactStringSet(labelRecords.map(value => value && value.scenarioId), scenarioIds)
      || labelRecords.some(value => !value || !isResolvedProtocolIdentity(value.labelerId)
        || !isResolvedProtocolIdentity(value.sourceEvidenceId)
        || !isResolvedProtocolIdentity(value.versionVerificationRecordId)
        || !validUtc(value.labeledAtUtc))) return null;

  const shadow = trusted.shadowComparisons;
  const comparisonRecords = shadow && Array.isArray(shadow.comparisons) ? shadow.comparisons : [];
  if (!commonIdentityMatches(shadow) || shadow.shadowComparisonSchemaVersion !== 1
      || shadow.baselineEvidenceFileSha256 !== trusted.baselineEvidenceFileSha256
      || shadow.candidateEvidenceFileSha256 !== trusted.candidateEvidenceFileSha256
      || !isResolvedProtocolIdentity(shadow.representativeRationale)
      || !exactStringSet(shadow.scenarioIds, scenarioIds)
      || !exactStringSet(comparisonRecords.map(value => value && value.scenarioId), scenarioIds)
      || comparisonRecords.some(value => {
        const left = value && baseline.get(value.scenarioId);
        const right = value && candidate.get(value.scenarioId);
        return !left || !right || value.baselineActionKey !== left.selectedActionKey
          || value.candidateActionKey !== right.selectedActionKey
          || !isResolvedProtocolIdentity(value.reviewedBy) || !validUtc(value.reviewedAtUtc);
      })) return null;

  const reviews = trusted.severeErrorReviews;
  const reviewRows = reviews && Array.isArray(reviews.reviews) ? reviews.reviews : [];
  const severeById = new Map(preliminaryMetrics.newSevereErrors.map(value => [value.scenarioId, value]));
  if (!commonIdentityMatches(reviews) || reviews.severeErrorReviewSchemaVersion !== 1
      || !exactStringSet(reviews.scenarioIds, scenarioIds)
      || !exactStringSet(reviewRows.map(value => value && value.scenarioId), new Set(severeById.keys()))
      || reviewRows.some(value => {
        const severe = value && severeById.get(value.scenarioId);
        return !severe || value.baselineActionKey !== severe.baselineActionKey
          || value.candidateActionKey !== severe.candidateActionKey
          || !isResolvedProtocolIdentity(value.reviewAction)
          || !isResolvedProtocolIdentity(value.reviewerId) || !validUtc(value.reviewedAtUtc)
          || !['accepted-risk', 'remediated', 'false-positive'].includes(value.disposition);
      })) return null;

  const approval = trusted.ownerApproval;
  const payload = approval && approval.payload;
  const expected = {
    approvalId: payload && payload.approvalId,
    approvalVersion: payload && payload.approvalVersion,
    approvedBy: payload && payload.approvedBy,
    approvedAtUtc: payload && payload.approvedAtUtc,
    protocolId: validatedProtocol.protocol.protocolId,
    protocolVersion: validatedProtocol.protocol.protocolVersion,
    protocolFileSha256: validatedProtocol.trustedContext.protocolFileSha256,
    protocolCanonicalSha256: validatedProtocol.trustedContext.protocolCanonicalSha256,
    holdoutFileSha256: validatedProtocol.trustedContext.holdoutFileSha256,
    holdoutCanonicalSha256: trusted.holdoutCanonicalSha256,
    candidateModelId: candidateIdentity.modelId,
    candidateModelPayloadSha256: candidateIdentity.modelPayloadSha256,
    labelProvenanceFileSha256: trusted.labelProvenanceFileSha256,
    shadowComparisonsFileSha256: trusted.shadowComparisonsFileSha256,
    severeErrorReviewsFileSha256: trusted.severeErrorReviewsFileSha256,
    latencyEvidenceFileSha256: trusted.latencyEvidenceFileSha256,
    safetyEvidenceFileSha256: trusted.safetyEvidenceFileSha256
  };
  if (!payload || !isResolvedProtocolIdentity(payload.approvalId)
      || typeof payload.approvalVersion !== 'string' || !PROTOCOL_VERSION.test(payload.approvalVersion)
      || !isResolvedProtocolIdentity(payload.approvedBy) || !validUtc(payload.approvedAtUtc)
      || Object.keys(expected).some(name => payload[name] !== expected[name])
      || !verifyDetachedOwnerApproval(approval, loadTrustedOwnerKeys())) return null;
  return { approval, checksums: trusted };
}

function meetsFrozenProtocolThresholds(metrics, thresholds) {
  return EFFECT_THRESHOLD_BINDINGS.every(([name, readMetric, direction]) => {
    const value = readMetric(metrics);
    return direction === 'minimum' ? value >= thresholds[name] : value <= thresholds[name];
  });
}

function evaluate(input, protocolContext = null, evidenceContext = null) {
  if (!input || typeof input !== 'object') fail('root object is missing');
  const benchmarkId = requireString(input.benchmarkId, 'benchmarkId');
  if (input.scenarioSchemaVersion !== 1) fail('scenario schema version mismatch');
  const holdoutCanonicalSha256 = Array.isArray(input.scenarios)
    ? sha256Hex(canonicalJson(input.scenarios)) : null;

  const scenarios = uniqueBy(input.scenarios, 'scenarioId', 'scenario');
  const validatedLabels = new Map();
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
    validatedLabels.set(row.scenarioId, validateLabels(row));
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
    selectedGrade(left, validatedLabels.get(id), 'baseline');
    selectedGrade(right, validatedLabels.get(id), 'candidate');
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
  const protocol = validateEvaluationProtocol(
    protocolContext, input, candidateIdentity, holdoutCanonicalSha256);
  const preliminaryMetrics = !engineeringOnly && scenarios.size >= 300 && protocol
    ? calculateMetrics(scenarios, baseline, candidate, input.safetyEvidence, input.latencyEvidence)
    : null;
  const approvedEvidence = validateEvidenceManifests(evidenceContext, input, scenarios,
    baseline, candidate, candidateIdentity, protocol, preliminaryMetrics);
  const trustworthy = !engineeringOnly && scenarios.size >= 300
    && protocol !== null && approvedEvidence !== null
    && !['buildIdentity', 'seasonIdentity', 'cardPoolIdentity']
      .some(name => !input[name] || input[name] === 'unknown');

  const missingPrerequisites = [];
  if (input.evidenceKind !== 'real-season-evaluation') missingPrerequisites.push('real-season-evidence');
  if (!approvedEvidence || scenarios.size < 300) {
    missingPrerequisites.push('300-real-trustworthy-version-matched-expert-labels');
  }
  if (!approvedEvidence) missingPrerequisites.push('representative-shadow-comparisons');
  if (!approvedEvidence) missingPrerequisites.push('human-severe-error-review');
  for (const name of ['buildIdentity', 'seasonIdentity', 'cardPoolIdentity']) {
    if (!input[name] || input[name] === 'unknown') missingPrerequisites.push(name);
  }
  if (!protocol) missingPrerequisites.push(MISSING_PROTOCOL);
  if (!approvedEvidence) missingPrerequisites.push(MISSING_PROVENANCE);

  const metrics = trustworthy ? preliminaryMetrics : null;
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
      protocolFileSha256: protocol.trustedContext.protocolFileSha256,
      holdoutChecksumSha256: protocol.protocol.holdoutChecksumSha256,
      provenanceApprovalId: approvedEvidence
        ? approvedEvidence.approval.payload.approvalId : null,
      ownerApprovalKeyId: approvedEvidence ? approvedEvidence.approval.keyId : null,
      labelProvenanceFileSha256: approvedEvidence
        ? approvedEvidence.checksums.labelProvenanceFileSha256 : null,
      shadowComparisonsFileSha256: approvedEvidence
        ? approvedEvidence.checksums.shadowComparisonsFileSha256 : null,
      severeErrorReviewsFileSha256: approvedEvidence
        ? approvedEvidence.checksums.severeErrorReviewsFileSha256 : null,
      latencyEvidenceFileSha256: approvedEvidence
        ? approvedEvidence.checksums.latencyEvidenceFileSha256 : null,
      safetyEvidenceFileSha256: approvedEvidence
        ? approvedEvidence.checksums.safetyEvidenceFileSha256 : null
    } : null,
    metrics,
    missingPrerequisites
  };
}

function parseArgs(argv) {
  const allowed = new Set(['--data-root', '--scenarios', '--baseline-evidence', '--candidate-evidence',
    '--latency-evidence', '--safety-evidence', '--evaluation-protocol', '--owner-approval',
    '--label-provenance', '--shadow-comparisons', '--severe-error-reviews', '--output']);
  const optional = new Set(['--evaluation-protocol', '--owner-approval', '--label-provenance',
    '--shadow-comparisons', '--severe-error-reviews']);
  const required = [...allowed].filter(key => !optional.has(key));
  const result = {};
  for (let index = 0; index < argv.length; index += 2) {
    const key = argv[index];
    if (!allowed.has(key) || index + 1 >= argv.length) fail(`unsupported command argument: ${key}`);
    result[key] = argv[index + 1];
  }
  for (const key of required) if (!result[key]) fail(`required command argument is missing: ${key}`);
  return result;
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

function createExternalProtocolContext(protocolBound, holdoutBound) {
  const protocol = protocolBound.value;
  const canonicalPayload = { ...protocol };
  delete canonicalPayload.protocolSha256;
  const trustedContext = Object.freeze({
    protocol,
    protocolFileSha256: protocolBound.sha256,
    protocolCanonicalSha256: sha256Hex(canonicalJson(canonicalPayload)),
    holdoutFileSha256: holdoutBound.sha256,
    holdoutCanonicalSha256: holdoutBound.canonicalSha256
  });
  const context = deepFreeze({
    source: EXTERNAL_PROTOCOL_SOURCE,
    protocol: trustedContext.protocol,
    protocolFileSha256: trustedContext.protocolFileSha256,
    protocolCanonicalSha256: trustedContext.protocolCanonicalSha256,
    holdoutFileSha256: trustedContext.holdoutFileSha256
  });
  TRUSTED_PROTOCOL_CONTEXTS.set(context, trustedContext);
  return context;
}

function readBoundJson(file, name) {
  const bytes = fs.readFileSync(file);
  const text = bytes.toString('utf8');
  rejectDuplicateJsonProperties(text);
  const value = JSON.parse(text);
  if (!value || typeof value !== 'object' || Array.isArray(value)) fail(`${name} root is invalid`);
  return Object.freeze({
    value: deepFreeze(value),
    sha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    canonicalSha256: sha256Hex(canonicalJson(value))
  });
}

function readBoundJsonLines(file, name) {
  const bytes = fs.readFileSync(file);
  const rows = bytes.toString('utf8').split(/\r?\n/).filter(Boolean).map(line => {
    rejectDuplicateJsonProperties(line);
    return JSON.parse(line);
  });
  if (rows.length === 0) fail(`${name} evidence is empty`);
  return Object.freeze({
    value: deepFreeze(rows),
    sha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    canonicalSha256: sha256Hex(canonicalJson(rows))
  });
}

function loadExternalProtocolContext(protocolFile, scenariosFile) {
  return createExternalProtocolContext(
    readBoundJson(protocolFile, 'evaluation protocol'),
    readBoundJsonLines(scenariosFile, 'scenario'));
}

function createExternalEvidenceContext(bound) {
  const trusted = Object.freeze({
    ownerApproval: bound.ownerApproval.value,
    labelProvenance: bound.labelProvenance.value,
    shadowComparisons: bound.shadowComparisons.value,
    severeErrorReviews: bound.severeErrorReviews.value,
    labelProvenanceFileSha256: bound.labelProvenance.sha256,
    shadowComparisonsFileSha256: bound.shadowComparisons.sha256,
    severeErrorReviewsFileSha256: bound.severeErrorReviews.sha256,
    holdoutCanonicalSha256: bound.scenarios.canonicalSha256,
    baselineEvidenceFileSha256: bound.baselineEvidence.sha256,
    candidateEvidenceFileSha256: bound.candidateEvidence.sha256,
    latencyEvidenceFileSha256: bound.latencyEvidence.sha256,
    safetyEvidenceFileSha256: bound.safetyEvidence.sha256,
    baselineEvidenceCanonicalSha256: bound.baselineEvidence.canonicalSha256,
    candidateEvidenceCanonicalSha256: bound.candidateEvidence.canonicalSha256,
    latencyEvidenceCanonicalSha256: bound.latencyEvidence.canonicalSha256,
    safetyEvidenceCanonicalSha256: bound.safetyEvidence.canonicalSha256
  });
  const context = deepFreeze({
    ownerApproval: trusted.ownerApproval,
    labelProvenance: trusted.labelProvenance,
    shadowComparisons: trusted.shadowComparisons,
    severeErrorReviews: trusted.severeErrorReviews
  });
  TRUSTED_EVIDENCE_CONTEXTS.set(context, trusted);
  return context;
}

function loadExternalEvidenceContext(files) {
  return createExternalEvidenceContext({
    ownerApproval: readBoundJson(files.ownerApproval, 'owner approval'),
    labelProvenance: readBoundJson(files.labelProvenance, 'label provenance'),
    shadowComparisons: readBoundJson(files.shadowComparisons, 'shadow comparisons'),
    severeErrorReviews: readBoundJson(files.severeErrorReviews, 'severe error reviews'),
    scenarios: readBoundJsonLines(files.scenarios, 'scenario'),
    baselineEvidence: readBoundJsonLines(files.baselineEvidence, 'baseline'),
    candidateEvidence: readBoundJsonLines(files.candidateEvidence, 'candidate'),
    latencyEvidence: readBoundJson(files.latencyEvidence, 'latency'),
    safetyEvidence: readBoundJson(files.safetyEvidence, 'safety')
  });
}

function isEvidenceContextBoundToInput(context, input) {
  try {
    const trusted = context && TRUSTED_EVIDENCE_CONTEXTS.get(context);
    return Boolean(trusted && input
      && trusted.holdoutCanonicalSha256 === sha256Hex(canonicalJson(input.scenarios))
      && trusted.baselineEvidenceCanonicalSha256
        === sha256Hex(canonicalJson(input.baselinePredictions))
      && trusted.candidateEvidenceCanonicalSha256
        === sha256Hex(canonicalJson(input.candidatePredictions))
      && trusted.latencyEvidenceCanonicalSha256 === sha256Hex(canonicalJson(input.latencyEvidence))
      && trusted.safetyEvidenceCanonicalSha256 === sha256Hex(canonicalJson(input.safetyEvidence)));
  } catch (_) {
    return false;
  }
}

function createScenarioBundle(scenarios, benchmarkId) {
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
  }
  return {
    evidenceKind: first.evidenceKind,
    benchmarkId,
    scenarioSchemaVersion: 1,
    buildIdentity: first.buildIdentity,
    seasonIdentity: first.seasonIdentity,
    cardPoolIdentity: first.cardPoolIdentity,
    scenarios
  };
}

function main(argv) {
  const args = parseArgs(argv);
  const dataRoot = assertExternalDataRoot(args['--data-root'], path.resolve(__dirname, '..', '..'));
  const files = {
    scenarios: resolveDataPath(dataRoot, args['--scenarios'], 'scenarios'),
    baselineEvidence: resolveDataPath(dataRoot, args['--baseline-evidence'], 'baseline evidence'),
    candidateEvidence: resolveDataPath(dataRoot, args['--candidate-evidence'], 'candidate evidence'),
    latencyEvidence: resolveDataPath(dataRoot, args['--latency-evidence'], 'latency evidence'),
    safetyEvidence: resolveDataPath(dataRoot, args['--safety-evidence'], 'safety evidence')
  };
  const bound = {
    scenarios: readBoundJsonLines(files.scenarios, 'scenario'),
    baselineEvidence: readBoundJsonLines(files.baselineEvidence, 'baseline'),
    candidateEvidence: readBoundJsonLines(files.candidateEvidence, 'candidate'),
    latencyEvidence: readBoundJson(files.latencyEvidence, 'latency'),
    safetyEvidence: readBoundJson(files.safetyEvidence, 'safety')
  };
  const benchmarkId = requireString(
    bound.baselineEvidence.value[0] && bound.baselineEvidence.value[0].benchmarkId,
    'baseline benchmarkId');
  const fixture = {
    ...createScenarioBundle(bound.scenarios.value, benchmarkId),
    baselinePredictions: bound.baselineEvidence.value,
    candidatePredictions: bound.candidateEvidence.value,
    latencyEvidence: bound.latencyEvidence.value,
    safetyEvidence: bound.safetyEvidence.value
  };
  const protocolContext = args['--evaluation-protocol']
    ? createExternalProtocolContext(
      readBoundJson(resolveDataPath(dataRoot, args['--evaluation-protocol'],
        'evaluation protocol'), 'evaluation protocol'), bound.scenarios)
    : null;
  const evidenceArgumentNames = ['--owner-approval', '--label-provenance',
    '--shadow-comparisons', '--severe-error-reviews'];
  const suppliedEvidenceArguments = evidenceArgumentNames.filter(name => args[name]);
  if (suppliedEvidenceArguments.length !== 0
      && suppliedEvidenceArguments.length !== evidenceArgumentNames.length) {
    fail('all external approval and evidence manifest arguments are required together');
  }
  const evidenceContext = suppliedEvidenceArguments.length === evidenceArgumentNames.length
    ? createExternalEvidenceContext({
      ...bound,
      ownerApproval: readBoundJson(resolveDataPath(dataRoot, args['--owner-approval'],
        'owner approval'), 'owner approval'),
      labelProvenance: readBoundJson(resolveDataPath(dataRoot, args['--label-provenance'],
        'label provenance'), 'label provenance'),
      shadowComparisons: readBoundJson(resolveDataPath(dataRoot, args['--shadow-comparisons'],
        'shadow comparisons'), 'shadow comparisons'),
      severeErrorReviews: readBoundJson(resolveDataPath(dataRoot, args['--severe-error-reviews'],
        'severe error reviews'), 'severe error reviews')
    }) : null;
  const output = resolveDataPath(dataRoot, args['--output'], 'output', { allowMissing: true });
  fs.mkdirSync(path.dirname(output), { recursive: true });
  resolveDataPath(dataRoot, args['--output'], 'output', { allowMissing: true });
  fs.writeFileSync(output, `${JSON.stringify(evaluate(fixture, protocolContext, evidenceContext), null, 2)}\n`, 'utf8');
  resolveDataPath(dataRoot, args['--output'], 'output');
}

module.exports = { evaluate, loadExternalProtocolContext, loadExternalEvidenceContext,
  isEvidenceContextBoundToInput, verifyDetachedOwnerApproval };
if (require.main === module) main(process.argv.slice(2));

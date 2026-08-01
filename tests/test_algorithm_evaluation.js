'use strict';
const assert = require('assert');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const root = path.resolve(__dirname, '..');
const fixture = JSON.parse(fs.readFileSync(
  path.join(__dirname, 'fixtures', 'algorithm', 'evaluation.synthetic.json'), 'utf8'));
const { evaluate, loadExternalProtocolContext } = require('../tools/model/evaluate_policy');
const clone = value => JSON.parse(JSON.stringify(value));
const categories = ['level', 'refresh', 'buy', 'sell', 'freeze', 'play', 'transition'];
const preferred = 'Upgrade|-1||';
const weak = 'Buy|0|CARD_001|shop';
const severe = 'Sell|0|CARD_002|board';

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort()
      .map(key => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function protocolContextFor(input, mutate = () => {}) {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bobcoach_protocol_contract_'));
  const scenariosFile = path.join(temporaryRoot, 'holdout.jsonl');
  const protocolFile = path.join(temporaryRoot, 'protocol.json');
  fs.writeFileSync(scenariosFile, `${JSON.stringify(input.scenarios[0])}\n`, 'utf8');
  const candidate = input.candidatePredictions[0];
  const protocol = {
    protocolSchemaVersion: 1,
    protocolId: 'season-2026-next-effect-v1',
    protocolVersion: '1.0.0',
    approvalStatus: 'owner-approved',
    approvedBy: 'owner',
    approvedAtUtc: '2026-07-30T00:00:00Z',
    frozenAtUtc: '2026-07-31T00:00:00Z',
    holdoutOpenedAtUtc: '2026-08-01T00:00:00Z',
    benchmarkId: input.benchmarkId,
    baselinePolicyId: 'legacy-v1.1.0',
    candidatePolicyId: 'unified-linear-v1',
    candidateModelId: candidate.modelId,
    candidateModelPayloadSha256: candidate.modelPayloadSha256,
    holdoutIdentity: 'season-2026-next-holdout-v1',
    holdoutChecksumSha256: crypto.createHash('sha256')
      .update(fs.readFileSync(scenariosFile)).digest('hex'),
    buildIdentity: input.buildIdentity,
    seasonIdentity: input.seasonIdentity,
    cardPoolIdentity: input.cardPoolIdentity,
    trainingSetIdentity: 'season-2026-next-training-v1',
    seasonRange: 'season-2026-next',
    confidenceCalibration: {
      status: 'accepted',
      gameBuild: input.buildIdentity,
      seasonId: input.seasonIdentity,
      cardPoolIdentity: input.cardPoolIdentity,
      trainingSetIdentity: 'season-2026-next-training-v1',
      holdoutIdentity: 'season-2026-next-holdout-v1',
      calibrationIdentity: 'season-2026-next-calibration-v1',
      modelId: candidate.modelId,
      marginScale: 1,
      semanticCoverageFloor: 0.5,
      confidenceThreshold: 0.5,
      planningThreshold: 0.5
    },
    calibrationApproval: {
      holdoutIdentity: 'season-2026-next-holdout-v1',
      calibrationIdentity: 'season-2026-next-calibration-v1',
      modelId: candidate.modelId
    },
    thresholds: {
      minimumSevereErrorRelativeReduction: 0,
      minimumAcceptedTop1Improvement: 0,
      minimumAcceptedTop3Improvement: 0,
      minimumPerCategoryTop1Regression: 0,
      maximumCandidateP95LatencyMilliseconds: 100,
      requiredFallbackPassRate: 1,
      maximumNewSevereErrors: 0
    }
  };
  mutate(protocol);
  protocol.protocolSha256 = crypto.createHash('sha256')
    .update(canonicalJson(protocol), 'utf8').digest('hex');
  fs.writeFileSync(protocolFile, JSON.stringify(protocol), 'utf8');
  const context = loadExternalProtocolContext(protocolFile, scenariosFile);
  fs.rmSync(temporaryRoot, { recursive: true, force: true });
  return context;
}

const report = evaluate(clone(fixture));
assert.strictEqual(report.evidenceSchemaVersion, 1);
assert.strictEqual(report.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(report.evidenceKind, 'engineering-self-test');
assert.strictEqual(report.metrics, null);
assert.strictEqual(report.evaluationProtocol, null);
assert(!Object.prototype.hasOwnProperty.call(report, 'activationDecision'));
assert(Array.isArray(report.missingPrerequisites) && report.missingPrerequisites.length > 0);
assert(!JSON.stringify(report).includes('improvementPercent'));

function forgedSyntheticEvidence() {
  const value = clone(fixture);
  value.evidenceKind = 'real-season-evaluation';
  value.buildIdentity = 'build-31.0.0.123456';
  value.seasonIdentity = 'season-2026-next';
  value.cardPoolIdentity = 'pool-2026-next-v1';
  value.realTrustworthyVersionMatchedExpertLabels = true;
  value.shadowComparisonsRepresentative = true;
  value.humanSevereErrorReviewComplete = true;
  value.evaluationProtocol = {
    protocolSchemaVersion: 1,
    protocolId: 'forged-protocol',
    protocolVersion: '1.0.0',
    approvalStatus: 'owner-approved',
    approvedBy: 'forged-owner',
    approvedAtUtc: '2026-07-31T00:00:00Z',
    frozenAtUtc: '2026-07-31T00:00:00Z',
    benchmarkId: value.benchmarkId,
    baselinePolicyId: 'legacy-v1.1.0',
    candidatePolicyId: 'unified-linear-v1',
    candidateModelId: 'synthetic-model',
    candidateModelPayloadSha256: 'b'.repeat(64),
    holdoutChecksumSha256: 'c'.repeat(64),
    thresholds: {},
    protocolSha256: 'd'.repeat(64)
  };
  value.scenarios = [];
  value.baselinePredictions = [];
  value.candidatePredictions = [];
  value.latencyEvidence.scenarios = [];
  for (let index = 0; index < 300; index += 1) {
    const suffix = String(index + 1).padStart(4, '0');
    const stateFingerprint = (index + 1).toString(16).padStart(64, '0');
    const candidateFingerprint = (index + 1001).toString(16).padStart(64, '0');
    const scenario = clone(fixture.scenarios[0]);
    scenario.scenarioId = `real-scenario-${suffix}`;
    scenario.gameId = `real-game-${suffix}`;
    scenario.category = categories[index % categories.length];
    scenario.severity = 'normal';
    scenario.stateFingerprint = stateFingerprint;
    scenario.candidateFingerprint = candidateFingerprint;
    scenario.state.gameBuild = value.buildIdentity;
    scenario.state.seasonId = value.seasonIdentity;
    scenario.state.cardPoolIdentity = value.cardPoolIdentity;
    value.scenarios.push(scenario);

    const baseline = clone(fixture.baselinePredictions[0]);
    Object.assign(baseline, {
      scenarioId: scenario.scenarioId,
      gameId: scenario.gameId,
      stateFingerprint,
      candidateFingerprint,
      selectedActionKey: index < 60 ? severe : weak,
      topActionKeys: index < 60 ? [severe, weak, preferred] : [weak]
    });
    value.baselinePredictions.push(baseline);

    const candidate = clone(fixture.candidatePredictions[0]);
    Object.assign(candidate, {
      scenarioId: scenario.scenarioId,
      gameId: scenario.gameId,
      stateFingerprint,
      candidateFingerprint,
      selectedActionKey: preferred,
      topActionKeys: [preferred]
    });
    value.candidatePredictions.push(candidate);

    const latency = clone(fixture.latencyEvidence.scenarios[0]);
    latency.scenarioId = scenario.scenarioId;
    latency.baselineSamplesMilliseconds = Array(10).fill(2);
    latency.candidateSamplesMilliseconds = Array(10).fill(1);
    Object.assign(latency, {
      baselineP50Milliseconds: 2,
      baselineP95Milliseconds: 2,
      baselineP99Milliseconds: 2,
      candidateP50Milliseconds: 1,
      candidateP95Milliseconds: 1,
      candidateP99Milliseconds: 1
    });
    value.latencyEvidence.scenarios.push(latency);
  }
  return value;
}

const forgedReport = evaluate(forgedSyntheticEvidence());
assert.strictEqual(forgedReport.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(forgedReport.metrics, null);
assert(forgedReport.missingPrerequisites.includes('owner-approved-versioned-frozen-checksum-bound-evaluation-protocol'));
assert(!JSON.stringify(forgedReport).includes('improvementPercent'));

const protocolInput = forgedSyntheticEvidence();
const validProtocolReport = evaluate(protocolInput, protocolContextFor(protocolInput));
assert(validProtocolReport.evaluationProtocol,
  'checksum-bound external protocol should be recognized even while synthetic evidence stays blocked');
assert.strictEqual(validProtocolReport.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(validProtocolReport.metrics, null);
const invalidProtocolMutations = [
  protocol => { protocol.protocolVersion = '01.0.0'; },
  protocol => { protocol.approvedAtUtc = '2026-07-30T00:00:00+00:00'; },
  protocol => { protocol.holdoutOpenedAtUtc = '2026-07-31T00:00:00'; },
  protocol => { protocol.benchmarkId = 'other-benchmark'; },
  protocol => { protocol.holdoutIdentity = 'other-holdout'; },
  protocol => { protocol.trainingSetIdentity = 'other-training'; },
  protocol => { protocol.seasonRange = 'unknown'; },
  protocol => { protocol.confidenceCalibration.status = 'pending'; },
  protocol => { protocol.confidenceCalibration.gameBuild = 'other-build'; },
  protocol => { protocol.confidenceCalibration.seasonId = 'other-season'; },
  protocol => { protocol.confidenceCalibration.cardPoolIdentity = 'other-pool'; },
  protocol => { protocol.confidenceCalibration.trainingSetIdentity = 'other-training'; },
  protocol => { protocol.confidenceCalibration.holdoutIdentity = 'other-holdout'; },
  protocol => { protocol.confidenceCalibration.calibrationIdentity = 'unknown'; },
  protocol => { protocol.confidenceCalibration.modelId = 'other-model'; },
  protocol => { protocol.confidenceCalibration.marginScale = 0; },
  protocol => { protocol.confidenceCalibration.semanticCoverageFloor = -0.01; },
  protocol => { protocol.confidenceCalibration.confidenceThreshold = 1.01; },
  protocol => { protocol.confidenceCalibration.planningThreshold = -0.01; },
  protocol => { protocol.calibrationApproval.holdoutIdentity = 'other-holdout'; },
  protocol => { protocol.calibrationApproval.calibrationIdentity = 'other-calibration'; },
  protocol => { protocol.calibrationApproval.modelId = 'other-model'; }
];
for (const mutate of invalidProtocolMutations) {
  const invalid = evaluate(protocolInput, protocolContextFor(protocolInput, mutate));
  assert.strictEqual(invalid.evaluationProtocol, null);
  assert.strictEqual(invalid.effectAcceptanceStatus, 'blocked-by-season-data');
  assert.strictEqual(invalid.metrics, null);
  assert(invalid.missingPrerequisites.includes(
    'owner-approved-versioned-frozen-checksum-bound-evaluation-protocol'));
}

const mutations = [
  value => { value.baselinePredictions[0].scenarioId = 'mismatch'; },
  value => { value.candidatePredictions[0].stateFingerprint = '2'.repeat(64); },
  value => { value.scenarios.push(clone(value.scenarios[0])); },
  value => { value.candidatePredictions[0].modelId = ''; },
  value => { value.baselinePredictions[0].modelId = 'candidate-model'; },
  value => { value.baselinePredictions[0].modelPayloadSha256 = 'b'.repeat(64); },
  value => { value.candidatePredictions[0].policyId = 'legacy-v1.1.0'; },
  value => { value.candidatePredictions[0].confidence = NaN; },
  value => { value.candidatePredictions[0].confidence = Infinity; },
  value => { value.candidatePredictions[0].confidence = -0.01; },
  value => { value.candidatePredictions[0].confidence = 1.01; },
  value => { value.candidatePredictions[0].plannerStatus = 'Invented'; },
  value => { value.candidatePredictions[0].fallbackReason = 'Invented'; },
  value => { value.latencyEvidence.scenarios[0].candidateSamplesMilliseconds[0] = Infinity; },
  value => { value.latencyEvidence.candidatePolicyId = 'legacy-v1.1.0'; },
  value => { value.safetyEvidence.probes.pop(); },
  value => { value.safetyEvidence.probes[0].probeId = 'unexpected-probe'; },
  value => { value.safetyEvidence.probes[0].expectedFallbackReason = '999'; },
  value => { value.safetyEvidence.probes[0].modelSource = 'candidate'; },
  value => { value.safetyEvidence.probes[1].modelSource = 'none'; },
  value => { value.safetyEvidence.probes[0].actualFallbackReason = 'None'; },
  value => { value.safetyEvidence.probes[0].passed = false; },
  value => {
    const timeout = value.safetyEvidence.probes.find(probe => probe.probeId === 'planner-timeout');
    timeout.actualBestCompletePath = [];
  },
  value => {
    const timeout = value.safetyEvidence.probes.find(probe => probe.probeId === 'planner-timeout');
    timeout.actualBestCompletePath = [weak];
  },
  value => { value.safetyEvidence.probes[0].actualBestCompletePath = [preferred]; },
  value => { value.baselinePredictions[0].evidenceSchemaVersion = 2; },
  value => { value.scenarios[0].gameId = ''; },
  value => { value.scenarios[0].actionLabels.push(clone(value.scenarios[0].actionLabels[0])); },
  value => { value.scenarios[0].actionLabels[0].grade = 'invented'; },
  value => { value.candidatePredictions[0].selectedActionKey = 'Refresh|-1||'; }
];
for (const mutate of mutations) {
  const changed = clone(fixture);
  mutate(changed);
  assert.throws(() => evaluate(changed), /evidence|identity|fingerprint|scenario|latency|safety|game/i);
}

const source = fs.readFileSync(path.join(root, 'tools', 'model', 'evaluate_policy.js'), 'utf8');
assert(source.includes('protocolSha256: protocol.protocol.protocolSha256'),
  'acceptance report does not record the validated protocol checksum');
for (const forbidden of ['train(', 'activationDecision', 'Resources/DecisionModels']) {
  assert(!source.includes(forbidden), `evaluator contains prohibited side effect: ${forbidden}`);
}
console.log('PASS evaluator validates structure, reports blocked evidence, and fails closed on invalid inputs');

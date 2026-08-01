'use strict';
const assert = require('assert');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const root = path.resolve(__dirname, '..');
const fixture = JSON.parse(fs.readFileSync(
  path.join(__dirname, 'fixtures', 'algorithm', 'evaluation.synthetic.json'), 'utf8'));
const { evaluate, loadExternalProtocolContext, loadExternalEvidenceContext,
  isEvidenceContextBoundToInput, verifyDetachedOwnerApproval }
  = require('../tools/model/evaluate_policy');
const clone = value => JSON.parse(JSON.stringify(value));
const categories = ['level', 'refresh', 'buy', 'sell', 'freeze', 'play', 'transition'];
const preferred = 'Upgrade|-1||';
const weak = 'Buy|0|CARD_001|shop';
const severe = 'Sell|0|CARD_002|board';
const missingProvenanceTrustRoot = 'owner-approved-independent-provenance-trust-root';

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort()
      .map(key => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function canonicalProtocolJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalProtocolJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort()
      .map(key => `${JSON.stringify(key)}:${canonicalProtocolJson(value[key])}`).join(',')}}`;
  }
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || (Number.isInteger(value) && !Number.isSafeInteger(value))) {
      throw new Error('protocol number is not safely representable');
    }
    const [mantissa, rawExponent] = (Object.is(value, -0) ? 0 : value).toExponential(16).split('e');
    const exponent = Number(rawExponent);
    return `${mantissa}e${exponent >= 0 ? '+' : ''}${exponent}`;
  }
  return JSON.stringify(value);
}

function protocolContextFor(input, mutate = () => {}, transformRaw = value => value,
  canonicalize = canonicalProtocolJson) {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bobcoach_protocol_contract_'));
  const scenariosFile = path.join(temporaryRoot, 'holdout.jsonl');
  const protocolFile = path.join(temporaryRoot, 'protocol.json');
  fs.writeFileSync(scenariosFile,
    `${input.scenarios.map(scenario => JSON.stringify(scenario)).join('\n')}\n`, 'utf8');
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
    .update(canonicalize(protocol), 'utf8').digest('hex');
  fs.writeFileSync(protocolFile, transformRaw(JSON.stringify(protocol)), 'utf8');
  try {
    return loadExternalProtocolContext(protocolFile, scenariosFile);
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
}

function evidenceContextFor(input) {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bobcoach_evidence_contract_'));
  const files = {
    ownerApproval: path.join(temporaryRoot, 'owner-approval.json'),
    labelProvenance: path.join(temporaryRoot, 'label-provenance.json'),
    shadowComparisons: path.join(temporaryRoot, 'shadow-comparisons.json'),
    severeErrorReviews: path.join(temporaryRoot, 'severe-error-reviews.json'),
    scenarios: path.join(temporaryRoot, 'holdout.jsonl'),
    baselineEvidence: path.join(temporaryRoot, 'baseline.jsonl'),
    candidateEvidence: path.join(temporaryRoot, 'candidate.jsonl'),
    latencyEvidence: path.join(temporaryRoot, 'latency.json'),
    safetyEvidence: path.join(temporaryRoot, 'safety.json')
  };
  for (const name of ['ownerApproval', 'labelProvenance', 'shadowComparisons',
    'severeErrorReviews']) {
    fs.writeFileSync(files[name], '{}\n', 'utf8');
  }
  for (const [name, rows] of [
    ['scenarios', input.scenarios],
    ['baselineEvidence', input.baselinePredictions],
    ['candidateEvidence', input.candidatePredictions]
  ]) {
    fs.writeFileSync(files[name], `${rows.map(row => JSON.stringify(row)).join('\n')}\n`, 'utf8');
  }
  fs.writeFileSync(files.latencyEvidence, `${JSON.stringify(input.latencyEvidence)}\n`, 'utf8');
  fs.writeFileSync(files.safetyEvidence, `${JSON.stringify(input.safetyEvidence)}\n`, 'utf8');
  try {
    return loadExternalEvidenceContext(files);
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
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

const plannedPredictionInput = clone(fixture);
plannedPredictionInput.candidatePredictions[0].plannerStatus = 'Completed';
plannedPredictionInput.candidatePredictions[0].selectedActionKey = preferred;
plannedPredictionInput.candidatePredictions[0].topActionKeys = [preferred, weak];
plannedPredictionInput.candidatePredictions[0].bestCompletePath = [weak, preferred];
assert.doesNotThrow(() => evaluate(plannedPredictionInput),
  'planner diagnostics must not replace the production SelectedAction used by effect metrics');
const plannerActionSubstitutedForProduction = clone(plannedPredictionInput);
plannerActionSubstitutedForProduction.candidatePredictions[0].selectedActionKey = weak;
assert.throws(() => evaluate(plannerActionSubstitutedForProduction), /selected action/i,
  'effect evidence must reject a planner action substituted for production SelectedAction');
const missingPlanningPath = clone(plannedPredictionInput);
delete missingPlanningPath.candidatePredictions[0].bestCompletePath;
assert.throws(() => evaluate(missingPlanningPath), /planning path/i);
const mismatchedPlanningPath = clone(plannedPredictionInput);
mismatchedPlanningPath.candidatePredictions[0].bestCompletePath = [];
assert.throws(() => evaluate(mismatchedPlanningPath), /planning path/i);

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
protocolInput.evidenceKind = 'engineering-self-test';
for (const prediction of protocolInput.candidatePredictions) {
  prediction.modelId = 'season-2026-next-linear-v1';
}
protocolInput.latencyEvidence.candidateModelId = 'season-2026-next-linear-v1';
const validProtocolReport = evaluate(protocolInput, protocolContextFor(protocolInput));
assert(validProtocolReport.evaluationProtocol,
  'checksum-bound external protocol should be recognized even while synthetic evidence stays blocked');
assert.strictEqual(validProtocolReport.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(validProtocolReport.metrics, null);
assert(validProtocolReport.missingPrerequisites.includes(missingProvenanceTrustRoot));

for (const numericSpelling of ['1.0', '1e0']) {
  const numericProtocol = protocolContextFor(protocolInput, () => {},
    raw => raw.replace('"marginScale":1', `"marginScale":${numericSpelling}`),
    canonicalProtocolJson);
  assert(evaluate(protocolInput, numericProtocol).evaluationProtocol,
    `protocol numeric spelling ${numericSpelling} must use the cross-language canonical hash`);
}

const trustedProtocolContext = protocolContextFor(protocolInput);
assert(Object.isFrozen(trustedProtocolContext), 'loaded protocol context must be immutable');
assert(Object.isFrozen(trustedProtocolContext.protocol), 'loaded protocol payload must be immutable');
assert(Object.isFrozen(trustedProtocolContext.protocol.thresholds),
  'nested protocol payload must be immutable');
assert.throws(() => { trustedProtocolContext.protocol = {}; }, /read only|Cannot assign/i);
assert.throws(() => { trustedProtocolContext.protocol.thresholds.maximumNewSevereErrors = 999; },
  /read only|Cannot assign/i);
assert.throws(() => { trustedProtocolContext.protocolCanonicalSha256 = 'f'.repeat(64); },
  /read only|Cannot assign/i);

const forgedProtocolContext = { ...trustedProtocolContext };
const forgedContextReport = evaluate(protocolInput, forgedProtocolContext);
assert.strictEqual(forgedContextReport.evaluationProtocol, null,
  'a copied or manually constructed protocol context must not be trusted');

const mismatchedHoldoutInput = clone(protocolInput);
mismatchedHoldoutInput.scenarios[0].category = 'refresh';
const mismatchedHoldoutReport = evaluate(mismatchedHoldoutInput, trustedProtocolContext);
assert.strictEqual(mismatchedHoldoutReport.evaluationProtocol, null,
  'loaded protocol context must be bound to the evaluated holdout scenarios');
assert.strictEqual(mismatchedHoldoutReport.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(mismatchedHoldoutReport.metrics, null);

const trustedEvidenceContext = evidenceContextFor(protocolInput);
assert(Object.isFrozen(trustedEvidenceContext), 'loaded evidence context must be immutable');
assert.strictEqual(isEvidenceContextBoundToInput(trustedEvidenceContext, protocolInput), true,
  'loaded evidence context must bind the exact evaluated evidence content');
for (const mutate of [
  value => { value.scenarios[0].category = 'refresh'; },
  value => { value.baselinePredictions[0].selectedActionKey = weak; },
  value => { value.candidatePredictions[0].selectedActionKey = weak; },
  value => { value.latencyEvidence.scenarios[0].candidateSamplesMilliseconds[0] = 99; },
  value => { value.safetyEvidence.probes[0].passed = false; }
]) {
  const changed = clone(protocolInput);
  mutate(changed);
  assert.strictEqual(isEvidenceContextBoundToInput(trustedEvidenceContext, changed), false,
    'evidence context must reject reuse with changed evaluated content');
}
assert.strictEqual(isEvidenceContextBoundToInput({ ...trustedEvidenceContext }, protocolInput), false,
  'a copied or manually constructed evidence context must not be trusted');

const renamedSyntheticInput = JSON.parse(JSON.stringify(protocolInput), (_, value) =>
  typeof value === 'string'
    ? value.replace(/synthetic/gi, 'current-season').replace(/forged/gi, 'declared')
    : value);
const renamedSyntheticReport = evaluate(renamedSyntheticInput,
  protocolContextFor(renamedSyntheticInput));
assert.strictEqual(renamedSyntheticReport.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(renamedSyntheticReport.metrics, null);
assert(renamedSyntheticReport.missingPrerequisites.includes(missingProvenanceTrustRoot));

for (const transformRaw of [
  raw => raw.replace('{"protocolSchemaVersion":1',
    '{"_postChecksumTopLevel":"tampered","protocolSchemaVersion":1'),
  raw => raw.replace('"status":"accepted"',
    '"status":"accepted","_postChecksumNested":"tampered"')
]) {
  const tamperedProtocolReport = evaluate(protocolInput,
    protocolContextFor(protocolInput, () => {}, transformRaw));
  assert.strictEqual(tamperedProtocolReport.evaluationProtocol, null,
    'underscore-prefixed protocol fields must remain checksum-bound');
  assert.strictEqual(tamperedProtocolReport.effectAcceptanceStatus, 'blocked-by-season-data');
  assert.strictEqual(tamperedProtocolReport.metrics, null);
}
const nullablePlanningProtocolReport = evaluate(protocolInput,
  protocolContextFor(protocolInput,
    protocol => { protocol.confidenceCalibration.planningThreshold = null; }));
assert(nullablePlanningProtocolReport.evaluationProtocol,
  'null planning threshold should keep planning disabled without invalidating calibration');
assert.throws(() => protocolContextFor(protocolInput, () => {}, raw => raw.replace(
  '"protocolVersion":"1.0.0"',
  '"protocolVersion":"1.0.0","protocolVersion":"1.0.1"')),
/duplicate.*property/i);
assert.throws(() => protocolContextFor(protocolInput, () => {}, raw => raw.replace(
  '"planningThreshold":0.5',
  '"planningThreshold":0.5,"planningThreshold":0.6')),
/duplicate.*property/i);
assert.doesNotThrow(() => protocolContextFor(protocolInput, () => {}, raw => raw.replace(
  '{"protocolSchemaVersion":1',
  '{"_arrayProbe":[{"escaped\\u004bey":1}],"protocolSchemaVersion":1')));
assert.throws(() => protocolContextFor(protocolInput, () => {}, raw => raw.replace(
  '{"protocolSchemaVersion":1',
  '{"_arrayProbe":[{"probe":1,"pr\\u006fbe":2}],"protocolSchemaVersion":1')),
/duplicate.*property/i);
const invalidProtocolMutations = [
  protocol => { protocol.protocolId = 'unknown'; },
  protocol => { protocol.approvedBy = 'synthetic-owner'; },
  protocol => { protocol.protocolVersion = '01.0.0'; },
  protocol => { protocol.approvedAtUtc = '2026-07-30T00:00:00+00:00'; },
  protocol => { protocol.holdoutOpenedAtUtc = '2026-07-31T00:00:00'; },
  protocol => { protocol.benchmarkId = 'other-benchmark'; },
  protocol => { protocol.buildIdentity = 'unknown'; },
  protocol => { protocol.seasonIdentity = 'synthetic-season'; },
  protocol => { protocol.cardPoolIdentity = 'unknown'; },
  protocol => { protocol.candidateModelId = 'unknown'; },
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
assert(source.includes('secure_write_evidence.ps1')
  && !source.includes('fs.writeFileSync(output'),
  'evaluator output must be written through the locked secure evidence writer');
const trustedOwnerKeys = JSON.parse(fs.readFileSync(
  path.join(root, 'tools', 'model', 'owner_approval_trusted_keys.json'), 'utf8'));
assert.strictEqual(trustedOwnerKeys.trustedKeySetSchemaVersion, 1);
assert.deepStrictEqual(trustedOwnerKeys.keys, [],
  'Task 12 must not trust an owner key before real season evidence is available');
assert(!source.includes('evaluation_protocol_approvals.json'),
  'a repository-owned approval registry must not be an approval trust root');
assert(!source.includes('realTrustworthyVersionMatchedExpertLabels')
  && !source.includes('shadowComparisonsRepresentative')
  && !source.includes('humanSevereErrorReviewComplete'),
  'readiness must not depend on caller-controlled booleans');
for (const option of ['--owner-approval', '--label-provenance', '--shadow-comparisons',
  '--severe-error-reviews']) {
  assert(source.includes(option), `evaluator CLI is missing ${option}`);
}
const { publicKey, privateKey } = crypto.generateKeyPairSync('ed25519');
const approvalPayload = {
  approvalId: 'owner-approval-test',
  protocolCanonicalSha256: 'a'.repeat(64),
  labelProvenanceFileSha256: 'b'.repeat(64),
  shadowComparisonsFileSha256: 'c'.repeat(64),
  severeErrorReviewsFileSha256: 'd'.repeat(64),
  latencyEvidenceFileSha256: 'e'.repeat(64),
  safetyEvidenceFileSha256: 'f'.repeat(64)
};
const signedApproval = {
  ownerApprovalSchemaVersion: 1,
  keyId: 'owner-test-key',
  signatureAlgorithm: 'ed25519',
  payload: approvalPayload,
  signatureBase64: crypto.sign(null, Buffer.from(canonicalJson(approvalPayload), 'utf8'), privateKey)
    .toString('base64')
};
const testKeys = [{ keyId: 'owner-test-key', algorithm: 'ed25519',
  publicKeySpkiPem: publicKey.export({ type: 'spki', format: 'pem' }) }];
assert.strictEqual(verifyDetachedOwnerApproval(signedApproval, testKeys), true,
  'valid detached owner signature should verify against an explicitly supplied test key');
signedApproval.payload.protocolCanonicalSha256 = 'e'.repeat(64);
assert.strictEqual(verifyDetachedOwnerApproval(signedApproval, testKeys), false,
  'owner signature must bind the complete canonical approval payload');
for (const checksumName of ['latencyEvidenceFileSha256', 'safetyEvidenceFileSha256']) {
  assert(source.includes(`${checksumName}: trusted.${checksumName}`),
    `owner approval must bind ${checksumName}`);
  assert(source.includes(`approvedEvidence.checksums.${checksumName}`),
    `acceptance report must record ${checksumName}`);
}
assert(source.includes('protocolSha256: protocol.protocol.protocolSha256'),
  'acceptance report does not record the validated protocol checksum');
for (const forbidden of ['train(', 'activationDecision', 'Resources/DecisionModels']) {
  assert(!source.includes(forbidden), `evaluator contains prohibited side effect: ${forbidden}`);
}
console.log('PASS evaluator validates structure, reports blocked evidence, and fails closed on invalid inputs');

'use strict';
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const fixture = JSON.parse(fs.readFileSync(
  path.join(__dirname, 'fixtures', 'algorithm', 'evaluation.synthetic.json'), 'utf8'));
const { evaluate } = require('../tools/model/evaluate_policy');
const clone = value => JSON.parse(JSON.stringify(value));
const categories = ['level', 'refresh', 'buy', 'sell', 'freeze', 'play', 'transition'];
const preferred = 'Upgrade|-1||';
const weak = 'Buy|0|CARD_001|shop';
const severe = 'Sell|0|CARD_002|board';

const report = evaluate(clone(fixture));
assert.strictEqual(report.evidenceSchemaVersion, 1);
assert.strictEqual(report.effectAcceptanceStatus, 'blocked-by-season-data');
assert.strictEqual(report.evidenceKind, 'engineering-self-test');
assert.strictEqual(report.metrics, null);
assert(!Object.prototype.hasOwnProperty.call(report, 'activationDecision'));
assert(Array.isArray(report.missingPrerequisites) && report.missingPrerequisites.length > 0);
assert(!JSON.stringify(report).includes('improvementPercent'));

function eligibleEvidence() {
  const value = clone(fixture);
  value.evidenceKind = 'real-season-evaluation';
  value.buildIdentity = 'build-31.0.0.123456';
  value.seasonIdentity = 'season-2026-next';
  value.cardPoolIdentity = 'pool-2026-next-v1';
  value.realTrustworthyVersionMatchedExpertLabels = true;
  value.shadowComparisonsRepresentative = true;
  value.humanSevereErrorReviewComplete = true;
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

const eligibleReport = evaluate(eligibleEvidence());
assert.strictEqual(eligibleReport.effectAcceptanceStatus, 'accepted');
assert(!Object.prototype.hasOwnProperty.call(eligibleReport, 'activationDecision'));
assert.deepStrictEqual(eligibleReport.missingPrerequisites, []);
assert.deepStrictEqual(eligibleReport.metrics.severeErrorRate, {
  baseline: 0.2, candidate: 0, relativeReduction: 1
});
assert.deepStrictEqual(eligibleReport.metrics.acceptedTop1Rate, {
  baseline: 0, candidate: 1, improvement: 1
});
assert.deepStrictEqual(eligibleReport.metrics.acceptedTop3Rate, {
  baseline: 0.2, candidate: 1, improvement: 0.8
});
assert.deepStrictEqual(eligibleReport.metrics.severityWeightedRegret, {
  baseline: 2.2, candidate: 0
});
for (const category of categories) {
  assert.strictEqual(eligibleReport.metrics.perCategoryRegression[category], 1);
}
assert.deepStrictEqual(eligibleReport.metrics.overridePrecision, {
  overrides: 300, better: 300, neutral: 0, worse: 0, precision: 1
});
assert.strictEqual(eligibleReport.metrics.fallbackPassRate, 1);
assert.deepStrictEqual(eligibleReport.metrics.latencyMilliseconds, {
  baseline: { p50: 2, p95: 2, p99: 2 },
  candidate: { p50: 1, p95: 1, p99: 1 }
});
assert.deepStrictEqual(eligibleReport.metrics.newSevereErrors, []);

const mutations = [
  value => { value.baselinePredictions[0].scenarioId = 'mismatch'; },
  value => { value.candidatePredictions[0].stateFingerprint = '2'.repeat(64); },
  value => { value.scenarios.push(clone(value.scenarios[0])); },
  value => { value.candidatePredictions[0].modelId = ''; },
  value => { value.latencyEvidence.scenarios[0].candidateSamplesMilliseconds[0] = Infinity; },
  value => { value.safetyEvidence.probes.pop(); },
  value => { value.safetyEvidence.probes[0].probeId = 'unexpected-probe'; },
  value => { value.safetyEvidence.probes[0].expectedFallbackReason = '999'; },
  value => {
    const timeout = value.safetyEvidence.probes.find(probe => probe.probeId === 'planner-timeout');
    timeout.actualSelectedActionKey = '';
    timeout.actualTopActionKeys = [];
  },
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
for (const forbidden of ['train(', 'activationDecision', 'Resources/DecisionModels']) {
  assert(!source.includes(forbidden), `evaluator contains prohibited side effect: ${forbidden}`);
}
console.log('PASS evaluator validates structure, reports blocked evidence, and fails closed on invalid inputs');

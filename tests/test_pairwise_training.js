'use strict';
const assert = require('assert');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { buildPairs } = require('../tools/model/build_pairs');
const { train } = require('../tools/model/train_pairwise');
const { splitRows } = require('../tools/model/split_dataset');

const fixturePath = path.join(__dirname, 'fixtures', 'algorithm', 'pairs.synthetic.jsonl');
const loadSyntheticPairs = () => fs.readFileSync(fixturePath, 'utf8').trim().split(/\r?\n/).map(JSON.parse);
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');

const first = train(loadSyntheticPairs(), { epochs: 200, learningRate: 0.05, lambda: 0.01, seed: 7 });
const second = train(loadSyntheticPairs(), { epochs: 200, learningRate: 0.05, lambda: 0.01, seed: 7 });
assert.deepStrictEqual(first, second);
assert.strictEqual(sha256(first), sha256(second));
const trainingEnvelope = JSON.parse(first.toString('utf8'));
assert.strictEqual(trainingEnvelope.kind, 'test-only-synthetic-training-envelope-v1');
const trainingPayload = Buffer.from(trainingEnvelope.payloadBase64, 'base64');
assert.strictEqual(trainingEnvelope.payloadSha256, sha256(trainingPayload));

const labeledScenario = {
  gameId: 'synthetic-game-003', capturedAtUtc: '2026-01-03T00:00:00Z', scenarioId: 'synthetic-scenario-003',
  observedPlayerActionKey: 'Refresh|-1||',
  acceptedActionKeys: ['BuyMinion|0|SYNTHETIC_C|shop'],
  state: { gameBuild: 'synthetic-build', seasonId: 'synthetic-season', cardPoolIdentity: 'synthetic-pool', fingerprint: 'synthetic-state-003' },
  candidates: [
    { actionKey: 'BuyMinion|0|SYNTHETIC_C|shop', actionType: 'BuyMinion', candidateFingerprint: 'synthetic-candidate-005', features: [1, 0, 0] },
    { actionKey: 'Refresh|-1||', actionType: 'Refresh', candidateFingerprint: 'synthetic-candidate-006', features: [0, 0, 0] },
    { actionKey: 'Upgrade|-1||', actionType: 'Upgrade', candidateFingerprint: 'synthetic-candidate-007', features: [0, 1, 0] }
  ],
  actionLabels: [
    { actionKey: 'BuyMinion|0|SYNTHETIC_C|shop', grade: 'preferred' },
    { actionKey: 'Refresh|-1||', grade: 'weak' },
    { actionKey: 'Upgrade|-1||', grade: 'not-evaluable' }
  ]
};
const labeledPairs = buildPairs(labeledScenario);
assert.strictEqual(labeledPairs.length, 1);
assert.strictEqual(labeledPairs[0].preferred.actionKey, labeledScenario.acceptedActionKeys[0]);
assert(!JSON.stringify(labeledPairs).includes('observedPlayerActionKey'));
assert(!JSON.stringify(labeledPairs).includes('synthetic-candidate-007'));

const unacceptedHigherGrade = JSON.parse(JSON.stringify(labeledScenario));
unacceptedHigherGrade.acceptedActionKeys = ['Refresh|-1||'];
assert.strictEqual(buildPairs(unacceptedHigherGrade).length, 0,
  'a higher grade is not an accepted action unless acceptedActionKeys says so');

const malformedActionKey = JSON.parse(JSON.stringify(labeledScenario));
malformedActionKey.candidates[0].actionKey = 'forged';
malformedActionKey.acceptedActionKeys[0] = 'forged';
malformedActionKey.actionLabels[0].actionKey = 'forged';
assert.throws(() => buildPairs(malformedActionKey), /actionKey/,
  'candidate actionKey must use the frozen four-part identity');

const mismatchedActionType = JSON.parse(JSON.stringify(labeledScenario));
mismatchedActionType.candidates[0].actionKey = 'Refresh|0|SYNTHETIC_C|shop';
mismatchedActionType.acceptedActionKeys[0] = 'Refresh|0|SYNTHETIC_C|shop';
mismatchedActionType.actionLabels[0].actionKey = 'Refresh|0|SYNTHETIC_C|shop';
assert.throws(() => buildPairs(mismatchedActionType), /actionKey/,
  'candidate actionKey type must match candidate actionType');

const unknownKeyActionType = JSON.parse(JSON.stringify(labeledScenario));
unknownKeyActionType.candidates[1].actionKey = 'ForgedAction|-1||';
unknownKeyActionType.actionLabels[1].actionKey = 'ForgedAction|-1||';
assert.throws(() => buildPairs(unknownKeyActionType), /actionKey/,
  'candidate actionKey type must be a known frozen action type');

const nonCanonicalTargetIndex = JSON.parse(JSON.stringify(labeledScenario));
nonCanonicalTargetIndex.candidates[1].actionKey = 'Refresh|01||';
nonCanonicalTargetIndex.actionLabels[1].actionKey = 'Refresh|01||';
assert.throws(() => buildPairs(nonCanonicalTargetIndex), /actionKey/,
  'candidate actionKey TargetIndex must be a canonical integer');

const rowsWithSameGameAcrossPartitions = loadSyntheticPairs().concat([
  Object.assign({}, loadSyntheticPairs()[0], { partition: 'holdout' })
]);
assert.throws(() => splitRows(rowsWithSameGameAcrossPartitions), /whole-game split/);

const duplicatePreferredCandidate = Object.assign({}, loadSyntheticPairs()[0], {
  other: Object.assign({}, loadSyntheticPairs()[0].other, {
    candidateFingerprint: 'synthetic-candidate-duplicate-other'
  })
});
const split = splitRows(loadSyntheticPairs().concat([duplicatePreferredCandidate]), 1);
assert.strictEqual(split.training.length, 1,
  'split deduplicates stateFingerprint|preferred.candidateFingerprint');
assert.strictEqual(split.holdout.length, 1, 'newest whole game is held out');

const duplicateBase = loadSyntheticPairs()[0];
const oldDuplicate = Object.assign({}, duplicateBase, {
  gameId: 'old', capturedAtUtc: '2026-01-01T00:00:00Z'
});
const middleUnique = Object.assign({}, loadSyntheticPairs()[1], {
  gameId: 'middle', capturedAtUtc: '2026-01-02T00:00:00Z'
});
const freshDuplicate = Object.assign({}, duplicateBase, {
  gameId: 'fresh', capturedAtUtc: '2026-01-03T00:00:00Z'
});
const chronological = splitRows([oldDuplicate, middleUnique, freshDuplicate], 1);
const reversed = splitRows([freshDuplicate, middleUnique, oldDuplicate], 1);
assert.deepStrictEqual(chronological, reversed,
  'duplicate selection must not depend on source row order');
assert.deepStrictEqual(chronological.training.map(row => row.gameId), ['middle']);
assert.deepStrictEqual(chronological.holdout.map(row => row.gameId), ['fresh'],
  'the newest duplicate remains eligible for whole-game holdout');

const tieFirst = Object.assign({}, duplicateBase, {
  gameId: 'tie-alpha', capturedAtUtc: '2026-01-03T00:00:00Z'
});
const tieSecond = Object.assign({}, duplicateBase, {
  gameId: 'tie-zulu', capturedAtUtc: '2026-01-03T00:00:00Z'
});
const tiedChronological = splitRows([tieFirst, middleUnique, tieSecond], 1);
const tiedReversed = splitRows([tieSecond, middleUnique, tieFirst], 1);
assert.deepStrictEqual(tiedChronological, tiedReversed,
  'equal-timestamp duplicate selection uses a stable tie-break');
assert.deepStrictEqual(tiedChronological.holdout.map(row => row.gameId), ['tie-zulu']);

const ordinalUppercaseGame = Object.assign({}, duplicateBase, {
  gameId: 'game-Z', capturedAtUtc: '2026-01-04T00:00:00Z',
  stateFingerprint: 'ordinal-state-uppercase',
  preferred: Object.assign({}, duplicateBase.preferred, {
    candidateFingerprint: 'ordinal-candidate-uppercase'
  })
});
const ordinalLowercaseGame = Object.assign({}, duplicateBase, {
  gameId: 'game-a', capturedAtUtc: '2026-01-04T00:00:00Z',
  stateFingerprint: 'ordinal-state-lowercase',
  preferred: Object.assign({}, duplicateBase.preferred, {
    candidateFingerprint: 'ordinal-candidate-lowercase'
  })
});
const ordinalSplit = splitRows([ordinalLowercaseGame, middleUnique, ordinalUppercaseGame], 1);
assert.deepStrictEqual(ordinalSplit.holdout.map(row => row.gameId), ['game-a'],
  'equal-timestamp independent games use ordinal gameId ordering');
console.log('PASS pairwise trainer is deterministic and split-safe; no strategy quality asserted');

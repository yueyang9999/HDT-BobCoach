'use strict';

const assert = require('assert');
const childProcess = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const repositoryRoot = path.resolve(__dirname, '..');
const compareTool = path.join(repositoryRoot, 'tools', 'model', 'compare_decision_events.js');
const { compareDecisionEventFiles } = require(compareTool);

const tempPrefix = 'bobcoach-decision-compare-test-';
const modelId = 'synthetic-policy';
const modelPayloadSha256 = 'a'.repeat(64);
const observedAtUtc = '2026-08-06T00:00:00Z';

function run(script, args) {
  return childProcess.spawnSync(process.execPath, [script, ...args], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  });
}

function writeJsonl(root, name, events) {
  const file = path.join(root, name);
  fs.writeFileSync(file, `${events.map(event => JSON.stringify(event)).join('\n')}\n`, 'utf8');
  return file;
}

function decisionCreated(decisionId, overrides = {}) {
  return {
    eventKey: `${decisionId}|created`,
    eventType: 'DecisionCreated',
    modelId,
    modelPayloadSha256,
    payload: {
      DecisionId: decisionId,
      GameId: decisionId.split(':')[0],
      State: {
        GameActive: true,
        IsDuos: false,
        GameBuild: '35.6.0.0',
        SeasonId: 'season-14',
        CardPoolIdentity: 'pool-14',
        HeroCardId: 'HERO_01',
        AnomalyId: '',
        Gold: 5,
        MaxGold: 10,
        TavernTier: 3,
        TavernUpgradeCost: 7,
        Health: 40,
        MaxHealth: 40,
        Armor: 0,
        Turn: 6,
        Phase: 'shop',
        FrozenShop: false,
        FreeRefreshCount: 1,
        FirstPurchaseUsedThisTurn: false,
        FirstMinionPurchaseUsedThisTurn: false,
        CurrentCompGuideTier: 'T1',
        AvailableTribes: ['Beast', 'Dragon'],
        BoardMinions: [],
        HandMinions: [],
        ShopMinions: [],
        TrinketOffer: [{
          CardId: 'TB2_100',
          TrinketName: 'Lesser Test',
          EntityId: 1,
          IsLesser: true,
          Tier: 2,
          Attack: 0,
          Health: 0,
          Tribes: ['Neutral'],
        }],
        ActiveTrinkets: [],
      },
      CandidateKeys: ['Refresh|-1||'],
      StateFingerprint: '0'.repeat(64),
      CandidateFingerprint: '1'.repeat(64),
      ...overrides,
    },
  };
}

function playerAction(decisionId, actionKey, occurrenceOrdinal = 0) {
  return {
    eventKey: `${decisionId}|action|${occurrenceOrdinal}|${actionKey}`,
    eventType: 'PlayerActionObserved',
    modelId,
    modelPayloadSha256,
    payload: {
      decisionId,
      observedAtUtc,
      actionKey,
    },
  };
}

function recommendation(decisionId, policyId = 'legacy-v1.1.0') {
  return {
    eventKey: `${decisionId}|recommendation`,
    eventType: 'RecommendationIssued',
    modelId,
    modelPayloadSha256,
    payload: {
      decisionId,
      observedAtUtc,
      result: {
        PolicyId: policyId,
        SelectedAction: null,
        RankedActions: [],
        Confidence: 0.5,
        PlannerStatus: 'NotRequested',
        FallbackReason: 'None',
        Explanation: '',
      },
    },
  };
}

function shadowComparison(decisionId, legacyAction, unifiedAction, agrees = false) {
  return {
    eventKey: `${decisionId}|shadow-comparison`,
    eventType: 'ShadowComparison',
    modelId,
    modelPayloadSha256,
    payload: {
      DecisionId: decisionId,
      ComparisonId: '2'.repeat(64),
      StateFingerprint: '0'.repeat(64),
      ComparedActionFingerprint: '3'.repeat(64),
      LegacyPolicyId: 'legacy-v1.1.0',
      UnifiedPolicyId: 'unified-linear-v1',
      LegacySelectedActionKey: legacyAction,
      UnifiedSelectedActionKey: unifiedAction,
      Agrees: agrees,
      LegacyConfidence: 0.5,
      UnifiedConfidence: 0.6,
      LegacyPlannerStatus: 'NotRequested',
      UnifiedPlannerStatus: 'NotRequested',
      LegacyFallbackReason: 'None',
      UnifiedFallbackReason: 'None',
      ScoreMargin: 0.1,
    },
  };
}

function combatOutcome(decisionId, resultSummary = 'placement:3') {
  return {
    eventKey: `${decisionId}|combat`,
    eventType: 'CombatOutcomeObserved',
    modelId,
    modelPayloadSha256,
    payload: {
      decisionId,
      observedAtUtc,
      outcome: {
        ResultSummary: resultSummary,
      },
    },
  };
}

function fullLog(decisionId, actionKey, options = {}) {
  return [
    decisionCreated(decisionId, options.created),
    recommendation(decisionId, options.policyId),
    playerAction(decisionId, actionKey),
    shadowComparison(decisionId, 'Refresh|-1||', actionKey),
    combatOutcome(decisionId, options.placement || 'placement:3'),
  ];
}

function removeTemp(root) {
  const tempRoot = path.resolve(os.tmpdir()) + path.sep;
  const resolved = path.resolve(root);
  assert.ok(resolved.startsWith(tempRoot), `unsafe temporary cleanup path: ${resolved}`);
  assert.ok(path.basename(resolved).startsWith(tempPrefix), `unexpected temporary directory: ${resolved}`);
  fs.rmSync(resolved, { recursive: true, force: true });
}

const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), tempPrefix));
try {
  const missingArgs = run(compareTool, []);
  assert.notStrictEqual(missingArgs.status, 0, 'comparison tool accepted implicit inputs');
  assert.match(missingArgs.stderr, /required option: --baseline/i);
  assert.match(missingArgs.stderr, /--candidate <path>/i);

  const baselinePath = writeJsonl(tempRoot, 'baseline.jsonl', fullLog('game-1:1', 'Refresh|-1||'));
  const candidatePath = writeJsonl(tempRoot, 'candidate.jsonl', fullLog('game-1:1', 'Refresh|-1||'));

  const identical = compareDecisionEventFiles(baselinePath, candidatePath);
  assert.strictEqual(identical.equal, true);
  assert.deepStrictEqual(identical.differences, []);
  assert.strictEqual(identical.baseline.eventCount, 5);
  assert.strictEqual(identical.candidate.eventCount, 5);

  const missingFile = path.join(tempRoot, 'missing.jsonl');
  assert.throws(() => compareDecisionEventFiles(missingFile, candidatePath),
    /does not exist|ENOENT/i, 'missing baseline must fail closed');
  assert.throws(() => compareDecisionEventFiles(baselinePath, missingFile),
    /does not exist|ENOENT/i, 'missing candidate must fail closed');

  const emptyPath = writeJsonl(tempRoot, 'empty.jsonl', []);
  assert.throws(() => compareDecisionEventFiles(emptyPath, candidatePath),
    /empty/i, 'empty baseline must fail closed');
  assert.throws(() => compareDecisionEventFiles(baselinePath, emptyPath),
    /empty/i, 'empty candidate must fail closed');

  const malformedPath = writeJsonl(tempRoot, 'malformed.jsonl', [
    fullLog('game-1:1', 'Refresh|-1||')[0],
    { malformed: true },
  ]);
  assert.throws(() => compareDecisionEventFiles(malformedPath, candidatePath),
    /malformed|JSON/i, 'malformed JSONL must fail closed');

  const blankPath = path.join(tempRoot, 'blank.jsonl');
  fs.writeFileSync(blankPath,
    `${JSON.stringify(fullLog('game-1:1', 'Refresh|-1||')[0])}\n\n`, 'utf8');
  assert.throws(() => compareDecisionEventFiles(blankPath, candidatePath),
    /blank line|empty line/i, 'blank JSONL line must fail closed');

  const unknownEventTypePath = writeJsonl(tempRoot, 'unknown-event-type.jsonl', [
    fullLog('game-1:1', 'Refresh|-1||')[0],
    {
      eventKey: 'game-1:1|invented',
      eventType: 'InventedEvent',
      modelId,
      modelPayloadSha256,
      payload: { DecisionId: 'game-1:1' },
    },
  ]);
  assert.throws(() => compareDecisionEventFiles(unknownEventTypePath, candidatePath),
    /unknown event type/i, 'unknown event type must fail closed');

  const unknownTopLevelPath = writeJsonl(tempRoot, 'unknown-top-level.jsonl', [
    {
      ...fullLog('game-1:1', 'Refresh|-1||')[0],
      _tampered: true,
    },
  ]);
  assert.throws(() => compareDecisionEventFiles(unknownTopLevelPath, candidatePath),
    /unknown field/i, 'unknown top-level field must fail closed');

  const unknownPayloadPath = writeJsonl(tempRoot, 'unknown-payload.jsonl', [
    {
      ...fullLog('game-1:1', 'Refresh|-1||')[0],
      payload: {
        ...fullLog('game-1:1', 'Refresh|-1||')[0].payload,
        _tampered: true,
      },
    },
  ]);
  assert.throws(() => compareDecisionEventFiles(unknownPayloadPath, candidatePath),
    /unknown field/i, 'unknown payload field must fail closed');

  const unknownStatePath = writeJsonl(tempRoot, 'unknown-state.jsonl', [
    {
      ...fullLog('game-1:1', 'Refresh|-1||')[0],
      payload: {
        ...fullLog('game-1:1', 'Refresh|-1||')[0].payload,
        State: {
          ...fullLog('game-1:1', 'Refresh|-1||')[0].payload.State,
          _tampered: true,
        },
      },
    },
  ]);
  assert.throws(() => compareDecisionEventFiles(unknownStatePath, candidatePath),
    /unknown field/i, 'unknown nested state field must fail closed');

  const missingDecisionIdPath = writeJsonl(tempRoot, 'missing-decision-id.jsonl', [
    {
      ...fullLog('game-1:1', 'Refresh|-1||')[0],
      payload: { GameId: 'game-1' },
    },
  ]);
  assert.throws(() => compareDecisionEventFiles(missingDecisionIdPath, candidatePath),
    /[Dd]ecision[Ii]d/i, 'missing decision id must fail closed');

  const duplicateEventPath = writeJsonl(tempRoot, 'duplicate-event.jsonl', [
    fullLog('game-1:1', 'Refresh|-1||')[0],
    fullLog('game-1:1', 'Refresh|-1||')[0],
  ]);
  assert.throws(() => compareDecisionEventFiles(duplicateEventPath, candidatePath),
    /duplicate event/i, 'duplicate event key must fail closed');

  const orphanActionPath = writeJsonl(tempRoot, 'orphan-action.jsonl', [
    playerAction('game-9:1', 'Refresh|-1||'),
  ]);
  assert.throws(() => compareDecisionEventFiles(orphanActionPath, candidatePath),
    /unknown decision/i, 'action for an unknown decision must fail closed');

  const tierPath = writeJsonl(tempRoot, 'tier.jsonl', [
    decisionCreated('game-2:1', {
      State: {
        ...decisionCreated('game-2:1').payload.State,
        CurrentCompGuideTier: 'T2',
      },
      StateFingerprint: '4'.repeat(64),
    }),
    playerAction('game-2:1', 'Refresh|-1||'),
  ]);
  const tierDiff = compareDecisionEventFiles(
    writeJsonl(tempRoot, 'tier-baseline.jsonl',
      [decisionCreated('game-2:1'), playerAction('game-2:1', 'Refresh|-1||')]),
    tierPath);
  assert(tierDiff.differences.some(diff => diff.kind === 'CurrentCompGuideTier'
    && diff.decisionId === 'game-2:1' && diff.baseline === 'T1' && diff.candidate === 'T2'),
  'tier difference must be reported');

  const trinketPath = writeJsonl(tempRoot, 'trinket.jsonl', [
    decisionCreated('game-3:1', {
      State: {
        ...decisionCreated('game-3:1').payload.State,
        TrinketOffer: [{
          CardId: 'TB2_101',
          TrinketName: 'Greater Test',
          EntityId: 2,
          IsLesser: false,
          Tier: 4,
          Attack: 0,
          Health: 0,
          Tribes: ['Neutral'],
        }],
      },
      StateFingerprint: '5'.repeat(64),
    }),
    playerAction('game-3:1', 'PickTrinket|1|TB2_101|offer'),
  ]);
  const trinketDiff = compareDecisionEventFiles(
    writeJsonl(tempRoot, 'trinket-baseline.jsonl',
      [decisionCreated('game-3:1'), playerAction('game-3:1', 'PickTrinket|0|TB2_100|offer')]),
    trinketPath);
  assert(trinketDiff.differences.some(diff => diff.kind === 'TrinketOffer'
    && diff.decisionId === 'game-3:1'),
  'trinket offer difference must be reported');
  assert(trinketDiff.differences.some(diff => diff.kind === 'PickTrinket'
    && diff.decisionId === 'game-3:1'
    && diff.baseline[0] === 'PickTrinket|0|TB2_100|offer'
    && diff.candidate[0] === 'PickTrinket|1|TB2_101|offer'),
  'pick trinket action difference must be reported');

  const actionPath = writeJsonl(tempRoot, 'action.jsonl',
    fullLog('game-4:1', 'BuyMinion|0|CARD_001|shop'));
  const actionDiff = compareDecisionEventFiles(
    writeJsonl(tempRoot, 'action-baseline.jsonl', fullLog('game-4:1', 'Refresh|-1||')),
    actionPath);
  assert(actionDiff.differences.some(diff => diff.kind === 'PlayerAction'
    && diff.decisionId === 'game-4:1'
    && diff.baseline[0] === 'Refresh|-1||'
    && diff.candidate[0] === 'BuyMinion|0|CARD_001|shop'),
  'observed action difference must be reported');

  const setDiff = compareDecisionEventFiles(
    writeJsonl(tempRoot, 'set-baseline.jsonl',
      [decisionCreated('game-5:1'), playerAction('game-5:1', 'Refresh|-1||')]),
    writeJsonl(tempRoot, 'set-candidate.jsonl',
      [decisionCreated('game-6:1'), playerAction('game-6:1', 'Refresh|-1||')]));
  assert(setDiff.differences.some(diff => diff.kind === 'DecisionSet'),
    'decision set mismatch must be reported');

  const cliPath = writeJsonl(tempRoot, 'cli-candidate.jsonl', fullLog('game-1:1', 'Refresh|-1||'));
  const cli = run(compareTool, ['--baseline', baselinePath, '--candidate', cliPath]);
  assert.strictEqual(cli.status, 0, cli.stderr);
  const cliReport = JSON.parse(cli.stdout);
  assert.strictEqual(cliReport.equal, true);
} finally {
  removeTemp(tempRoot);
}

console.log('PASS decision event comparison requires explicit inputs and reports behavioral differences');

'use strict';

const assert = require('assert');
const childProcess = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const repositoryRoot = path.resolve(__dirname, '..');
const ingestTool = path.join(repositoryRoot, 'tools', 'model', 'ingest_decision_events.js');
const compareTool = path.join(repositoryRoot, 'tools', 'model', 'compare_decision_events.js');
const { buildAggregate } = require(ingestTool);
const { readEventText, indexEvents } = require(compareTool);

const evidenceGenerated = 'E:\\HDT-BobCoach-evidence\\season14\\generated';
const tempPrefix = 't10-ingest-test-';
const modelId = 'synthetic-policy';
const modelPayloadSha256 = 'a'.repeat(64);
const observedAtUtc = '2026-08-06T00:00:00Z';

function run(script, args) {
  return childProcess.spawnSync(process.execPath, [script, ...args], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  });
}

function runCli(dataRoot, inputRel, outputRel) {
  return run(ingestTool, [
    '--data-root', dataRoot,
    '--input', inputRel,
    '--output', outputRel,
  ]);
}

function writeText(root, name, text) {
  const file = path.join(root, name);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, 'utf8');
  return file;
}

function writeJsonl(root, name, events) {
  return writeText(root, name, `${events.map(event => JSON.stringify(event)).join('\n')}\n`);
}

function fingerprint(seed) {
  return crypto.createHash('sha256').update(seed, 'utf8').digest('hex');
}

function baseState(overrides = {}) {
  return {
    GameActive: true,
    IsDuos: false,
    GameBuild: '36.2.248348',
    SeasonId: 'season14',
    CardPoolIdentity: '0bcc979ae21df891',
    HeroCardId: 'TB2_100',
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
    TrinketOffer: [],
    ActiveTrinkets: [],
    ...overrides,
  };
}

function trinket(overrides = {}) {
  return {
    CardId: 'TB2_100',
    TrinketName: 'Lesser Test',
    EntityId: 1,
    IsLesser: true,
    Tier: 2,
    Attack: 0,
    Health: 0,
    Tribes: ['Neutral'],
    ...overrides,
  };
}

function decisionCreated(decisionId, overrides = {}) {
  const state = baseState(overrides.State || {});
  return {
    eventKey: `${decisionId}|created`,
    eventType: 'DecisionCreated',
    modelId,
    modelPayloadSha256,
    payload: {
      DecisionId: decisionId,
      GameId: decisionId.split(':')[0],
      State: state,
      CandidateKeys: ['Refresh|-1||'],
      StateFingerprint: fingerprint(`${decisionId}|state`),
      CandidateFingerprint: fingerprint(`${decisionId}|candidate`),
      ...overrides,
      State: state,
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

function playerAction(decisionId, actionKey, ordinal = 0) {
  return {
    eventKey: `${decisionId}|action|${ordinal}|${actionKey}`,
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

function fullEvents() {
  return [
    decisionCreated('game-1:1', {
      State: { TrinketOffer: [trinket()] },
    }),
    recommendation('game-1:1'),
    playerAction('game-1:1', 'PickTrinket|1|TB2_100|offer'),
    combatOutcome('game-1:1'),
    decisionCreated('game-2:1', {
      State: {
        TrinketOffer: [trinket({ CardId: 'TB2_101', EntityId: 2 })],
      },
    }),
    recommendation('game-2:1'),
    playerAction('game-2:1', 'Refresh|-1||'),
    combatOutcome('game-2:1'),
    decisionCreated('game-3:1', {
      State: { TrinketOffer: [] },
    }),
    playerAction('game-3:1', 'Refresh|-1||'),
  ];
}

function makeTempRoot() {
  return fs.mkdtempSync(path.join(evidenceGenerated, tempPrefix));
}

function removeTempRoot(root) {
  const resolved = path.resolve(root);
  const expectedBase = path.resolve(evidenceGenerated) + path.sep;
  assert.ok(resolved.startsWith(expectedBase), `unsafe temporary cleanup path: ${resolved}`);
  assert.ok(path.basename(resolved).startsWith(tempPrefix), `unexpected temporary directory: ${resolved}`);
  fs.rmSync(resolved, { recursive: true, force: true });
}

function assertCliError(result, pattern, message) {
  assert.notStrictEqual(result.status, 0, message);
  assert.match(result.stderr, pattern, message);
}

const source = fs.readFileSync(ingestTool, 'utf8');
assert.ok(source.includes('secure_read_evidence.ps1'), 'ingest tool must use secure evidence reader');
assert.ok(source.includes('secure_write_evidence.ps1'), 'ingest tool must use secure evidence writer');
assert.ok(!source.includes("require('fs')"), 'ingest tool must not require fs directly');
assert.ok(!source.includes('fs.readFileSync'), 'ingest tool must not read external files directly');
assert.ok(!source.includes('fs.writeFileSync'), 'ingest tool must not write external files directly');

const directAggregate = buildAggregate(
  Buffer.from('direct'),
  [{
    eventKey: 'd|created',
    eventType: 'DecisionCreated',
    payload: {
      DecisionId: 'd',
      State: {
        GameBuild: null,
        SeasonId: undefined,
        CardPoolIdentity: 'unknown',
        HeroCardId: '',
        AnomalyId: undefined,
        CurrentCompGuideTier: undefined,
      },
    },
  }],
  new Map([['d', {}]]),
  'inputs/direct.jsonl');
assert.deepStrictEqual(directAggregate.identity.gameBuild, { distinctCount: 1, values: ['unknown'] });
assert.deepStrictEqual(directAggregate.identity.seasonId, { distinctCount: 1, values: ['unknown'] });
assert.deepStrictEqual(directAggregate.identity.cardPoolIdentity, { distinctCount: 1, values: ['unknown'] });
assert.deepStrictEqual(directAggregate.identity.heroCardId, { distinctCount: 1, values: [''] });
assert.deepStrictEqual(directAggregate.identity.anomalyId, { distinctCount: 1, values: ['unknown'] });
assert.deepStrictEqual(directAggregate.identity.currentCompGuideTier, {
  distinctCount: 1,
  values: ['unknown'],
});

const tempRoot = makeTempRoot();
try {
  const missingArgs = run(ingestTool, []);
  assertCliError(missingArgs, /required option: --data-root <data root>/i,
    'missing --data-root must fail closed');
  assert.match(missingArgs.stderr, /--input <input>/i);
  assert.match(missingArgs.stderr, /--output <output>/i);

  const systemDriveRoot = path.join(process.env.SystemDrive || 'C:', 'Windows', 'Temp');
  assertCliError(runCli(systemDriveRoot, 'input.jsonl', 'out.json'),
    /system drive/i, 'system drive data root must fail closed');
  assertCliError(runCli(repositoryRoot, 'input.jsonl', 'out.json'),
    /outside the repository/i, 'repository-contained data root must fail closed');

  const inputRel = 'inputs/decision-events.jsonl';
  const outputRel = 'reports/decision-events-aggregate.json';
  const inputPath = writeJsonl(tempRoot, inputRel, fullEvents());

  const absoluteInput = runCli(tempRoot, inputPath, outputRel);
  assertCliError(absoluteInput, /input must be relative to data root/i,
    'absolute input path must fail closed');

  const missingInput = runCli(tempRoot, 'inputs/missing.jsonl', outputRel);
  assertCliError(missingInput, /input does not exist/i, 'missing input must fail closed');

  writeText(tempRoot, 'inputs/empty.jsonl', '');
  assertCliError(runCli(tempRoot, 'inputs/empty.jsonl', outputRel),
    /empty/i, 'empty input must fail closed');

  writeText(tempRoot, 'inputs/malformed.jsonl', '{not-json}\n');
  assertCliError(runCli(tempRoot, 'inputs/malformed.jsonl', outputRel),
    /malformed JSONL/i, 'malformed JSONL must fail closed');

  writeText(tempRoot, 'inputs/blank.jsonl',
    `${JSON.stringify(decisionCreated('game-9:1'))}\n\n`);
  assertCliError(runCli(tempRoot, 'inputs/blank.jsonl', outputRel),
    /blank line/i, 'blank JSONL line must fail closed');

  writeJsonl(tempRoot, 'inputs/unknown-event-type.jsonl', [
    decisionCreated('game-8:1'),
    {
      eventKey: 'game-8:1|invented',
      eventType: 'InventedEvent',
      modelId,
      modelPayloadSha256,
      payload: { DecisionId: 'game-8:1' },
    },
  ]);
  assertCliError(runCli(tempRoot, 'inputs/unknown-event-type.jsonl', outputRel),
    /unknown event type/i, 'unknown event type must fail closed');

  writeJsonl(tempRoot, 'inputs/duplicate-event.jsonl', [
    decisionCreated('game-7:1'),
    decisionCreated('game-7:1'),
  ]);
  assertCliError(runCli(tempRoot, 'inputs/duplicate-event.jsonl', outputRel),
    /duplicate event/i, 'duplicate event key must fail closed');

  writeJsonl(tempRoot, 'inputs/orphan-action.jsonl', [
    playerAction('game-6:1', 'Refresh|-1||'),
  ]);
  assertCliError(runCli(tempRoot, 'inputs/orphan-action.jsonl', outputRel),
    /unknown decision/i, 'orphan action must fail closed');

  writeJsonl(tempRoot, 'inputs/unknown-top-level.jsonl', [{
    ...decisionCreated('game-5:1'),
    _tampered: true,
  }]);
  assertCliError(runCli(tempRoot, 'inputs/unknown-top-level.jsonl', outputRel),
    /unknown field/i, 'unknown top-level field must fail closed');

  const created = decisionCreated('game-4:1');
  writeJsonl(tempRoot, 'inputs/unknown-payload.jsonl', [{
    ...created,
    payload: {
      ...created.payload,
      _tampered: true,
    },
  }]);
  assertCliError(runCli(tempRoot, 'inputs/unknown-payload.jsonl', outputRel),
    /unknown field/i, 'unknown payload field must fail closed');

  writeJsonl(tempRoot, 'inputs/unknown-state.jsonl', [{
    ...created,
    payload: {
      ...created.payload,
      State: {
        ...created.payload.State,
        _tampered: true,
      },
    },
  }]);
  assertCliError(runCli(tempRoot, 'inputs/unknown-state.jsonl', outputRel),
    /unknown field/i, 'unknown state field must fail closed');

  const first = runCli(tempRoot, inputRel, outputRel);
  assert.strictEqual(first.status, 0, first.stderr);
  const outputPath = path.join(tempRoot, outputRel);
  assert.ok(fs.existsSync(outputPath), 'aggregate output must be written');
  const firstBytes = fs.readFileSync(outputPath);
  const report = JSON.parse(firstBytes.toString('utf8'));

  assert.strictEqual(report.schemaVersion, 1);
  assert.strictEqual(report.inputPath, inputRel);
  assert.strictEqual(report.eventCount, 10);
  assert.strictEqual(report.decisionCount, 3);
  assert.deepStrictEqual(report.eventTypeCounts, {
    DecisionCreated: 3,
    PlayerActionObserved: 3,
    RecommendationIssued: 2,
    CombatOutcomeObserved: 2,
    ShadowComparison: 0,
  });
  assert.deepStrictEqual(report.identity, {
    gameBuild: { distinctCount: 1, values: ['36.2.248348'] },
    seasonId: { distinctCount: 1, values: ['season14'] },
    cardPoolIdentity: { distinctCount: 1, values: ['0bcc979ae21df891'] },
    heroCardId: { distinctCount: 1, values: ['TB2_100'] },
    anomalyId: { distinctCount: 1, values: [''] },
    currentCompGuideTier: { distinctCount: 1, values: ['T1'] },
  });
  assert.strictEqual(report.trinketOfferDecisionCount, 2);
  assert.strictEqual(report.pickTrinketActionCount, 1);
  assert.strictEqual(report.inputFileSha256,
    crypto.createHash('sha256').update(fs.readFileSync(inputPath)).digest('hex'));

  const reportText = JSON.stringify(report);
  assert.ok(!reportText.includes(path.resolve(tempRoot)), 'report must not contain machine paths');
  for (const forbidden of ['labels', 'scenarios', 'candidate model', 'training set',
    'evaluation samples']) {
    assert.ok(!reportText.toLowerCase().includes(forbidden), `report must not contain ${forbidden}`);
  }

  const secondOutputRel = 'reports/decision-events-aggregate-2.json';
  const second = runCli(tempRoot, inputRel, secondOutputRel);
  assert.strictEqual(second.status, 0, second.stderr);
  assert.deepStrictEqual(fs.readFileSync(path.join(tempRoot, secondOutputRel)), firstBytes,
    'rerunning ingest must produce byte-identical aggregate output');

  const overwrite = runCli(tempRoot, inputRel, outputRel);
  assertCliError(overwrite, /cannot publish/i,
    'existing output must fail closed without overwrite');
  assert.deepStrictEqual(fs.readFileSync(outputPath), firstBytes,
    'failed overwrite must preserve the original aggregate output');

  const parsed = readEventText(fs.readFileSync(inputPath, 'utf8'), 'input');
  const indexed = indexEvents(parsed, 'input');
  assert.strictEqual(indexed.size, 3);
} finally {
  removeTempRoot(tempRoot);
}

console.log('PASS deterministic decision-event ingest aggregator');

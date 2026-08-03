const assert = require('assert');
const fs = require('fs');
const path = require('path');
const repo = path.resolve(__dirname, '..');
const agents = fs.readFileSync(path.join(repo, 'AGENTS.md'), 'utf8');
for (const required of [
  'src/BobCoach/Core/Decision/',
  'src/BobCoach/Resources/DecisionModels/',
  'tests/fixtures/algorithm/',
  'tools/model/',
  'explicit external data root'
]) assert(agents.includes(required), `missing directory rule: ${required}`);

const { assertExternalDataRoot, resolveDataPath, actionKey, cardPoolIdentity, contractHeader, scenarioSchemaVersion } = require('../tools/model/contracts');
assert.throws(() => assertExternalDataRoot(repo, repo), /outside the repository/);
assert.throws(() => assertExternalDataRoot(path.join(process.env.SystemDrive, 'bobcoach-algorithm'), repo), /system drive/);
assert.throws(() => assertExternalDataRoot(path.dirname(repo), repo), /cannot contain repository/);

const pathContractRoot = path.join(path.dirname(repo),
  `HDT-BobCoach-path-contract-${process.pid}-${Date.now()}`);
const dataRoot = path.join(pathContractRoot, 'data');
const rootJunction = path.join(pathContractRoot, 'root-junction');
try {
  fs.mkdirSync(dataRoot, { recursive: true });
  fs.symlinkSync(repo, rootJunction, 'junction');
  assert.throws(() => assertExternalDataRoot(rootJunction, repo), /reparse point|outside the repository/i,
    'a DataRoot junction must not hide a repository target');

  const childJunction = path.join(dataRoot, 'child-junction');
  fs.symlinkSync(repo, childJunction, 'junction');
  assert.throws(() => resolveDataPath(dataRoot, 'child-junction\\AGENTS.md', 'input evidence'),
    /reparse point|escapes data root/i,
    'an input path must not traverse a child junction');
  assert.throws(() => resolveDataPath(dataRoot, 'child-junction\\new-output.json',
    'output evidence', { allowMissing: true }), /reparse point|escapes data root/i,
  'an output parent must not traverse a child junction');

  const inputSymlink = path.join(dataRoot, 'input-link.json');
  try {
    fs.symlinkSync(path.join(repo, 'AGENTS.md'), inputSymlink, 'file');
    assert.throws(() => resolveDataPath(dataRoot, 'input-link.json', 'input evidence'),
      /reparse point|escapes data root/i,
      'an input file symlink must not escape the data root');
  } catch (error) {
    if (!error || error.code !== 'EPERM') throw error;
    fs.writeFileSync(inputSymlink, '{}', 'utf8');
    const originalLstatSync = fs.lstatSync;
    fs.lstatSync = target => path.resolve(target) === path.resolve(inputSymlink)
      ? { isSymbolicLink: () => true }
      : originalLstatSync(target);
    try {
      assert.throws(() => resolveDataPath(dataRoot, 'input-link.json', 'input evidence'),
        /reparse point/i,
        'an input file reparse point must fail closed when symlink creation is unavailable');
    } finally {
      fs.lstatSync = originalLstatSync;
    }
  }
} finally {
  fs.rmSync(pathContractRoot, { recursive: true, force: true });
}
assert.strictEqual(actionKey({ type: 'Upgrade', targetIndex: -1, cardId: '', purchaseSource: '' }),
  'Upgrade|-1||');
assert.strictEqual(cardPoolIdentity(['Bacon_1', 'Bacon_2']), '64ee82650ea80b74',
  'card pool fingerprint must be a stable golden value');
assert.strictEqual(cardPoolIdentity(['Bacon_2', 'Bacon_1']), '64ee82650ea80b74',
  'fingerprint must be order independent');
assert.strictEqual(cardPoolIdentity(['Bacon_1', 'Bacon_1', 'Bacon_2']), '64ee82650ea80b74',
  'fingerprint must treat the pool as a set');
assert.notStrictEqual(cardPoolIdentity(['A', 'a']), cardPoolIdentity(['A']),
  'fingerprint must be case sensitive');
assert.strictEqual(cardPoolIdentity(null), 'unknown');
assert.strictEqual(cardPoolIdentity(undefined), 'unknown');
assert.throws(() => cardPoolIdentity([]), /must not be empty/,
  'an empty pool is a data error and must fail loudly');
assert.throws(() => cardPoolIdentity('Bacon_1'), /expects an array/);
assert.throws(() => cardPoolIdentity([42]), /non-empty strings/);
assert.throws(() => cardPoolIdentity(['']), /non-empty strings/);
const header = contractHeader({
  season: 'unknown',
  gameBuild: 'unknown',
  cardPoolIdentity: cardPoolIdentity(['Bacon_1', 'Bacon_2']),
  source: 'fixture-source',
  capturedAt: '2026-08-03T00:00:00Z',
  sha256: 'A'.repeat(64),
  provenance: { generator: 'fixture', parameters: { version: 1 } },
});
assert.deepStrictEqual(header, {
  schemaVersion: 1,
  season: 'unknown',
  gameBuild: 'unknown',
  cardPoolIdentity: '64ee82650ea80b74',
  source: 'fixture-source',
  capturedAt: '2026-08-03T00:00:00Z',
  sha256: 'a'.repeat(64),
  provenance: { generator: 'fixture', parameters: { version: 1 } },
});
assert.deepStrictEqual(Object.keys(header),
  ['schemaVersion', 'season', 'gameBuild', 'cardPoolIdentity', 'source', 'capturedAt', 'sha256', 'provenance'],
  'contract header must keep a fixed key order');
assert.throws(() => contractHeader({ season: 'x', gameBuild: 'x', cardPoolIdentity: 'x',
  capturedAt: '2026-08-03T00:00:00Z', sha256: 'a'.repeat(64) }), /source/);
assert.throws(() => contractHeader({ season: 'x', gameBuild: 'x', cardPoolIdentity: 'x',
  source: 's', capturedAt: '2026-08-03', sha256: 'a'.repeat(64) }), /ISO-8601/);
assert.throws(() => contractHeader({ season: 'x', gameBuild: 'x', cardPoolIdentity: 'x',
  source: 's', capturedAt: '2026-08-03T00:00:00Z', sha256: 'zz' }), /64-character/);
assert.throws(() => contractHeader({ season: 'x', gameBuild: 'x', cardPoolIdentity: 'x',
  source: 's', capturedAt: '2026-08-03T00:00:00Z', sha256: 'a'.repeat(64),
  provenance: ['not-an-object'] }), /provenance/);

const fixtures = path.join(repo, 'tests', 'fixtures', 'algorithm');
const schema = JSON.parse(fs.readFileSync(path.join(fixtures, 'scenario-schema-v1.json'), 'utf8'));
assert.strictEqual(scenarioSchemaVersion, 1);
assert.strictEqual(schema.scenarioSchemaVersion, 1);
assert.deepStrictEqual(schema.required,
  ['scenarioId', 'gameId', 'capturedAtUtc', 'category', 'state', 'candidates', 'actionLabels', 'acceptedActionKeys', 'severity']);
assert.deepStrictEqual(schema.categories, ['level', 'refresh', 'buy', 'sell', 'freeze', 'play', 'transition']);
assert.deepStrictEqual(schema.severities, ['normal', 'high-impact', 'severe']);
assert.deepStrictEqual(schema.actionGrades, ['preferred', 'acceptable', 'weak', 'severe-error', 'not-evaluable']);

const golden = JSON.parse(fs.readFileSync(path.join(fixtures, 'v1.1.0-golden.synthetic.json'), 'utf8'));
assert.strictEqual(golden.benchmarkId, 'v1.1.0-golden-synthetic-v1');
assert.strictEqual(golden.policyId, 'legacy-v1.1.0');
assert.strictEqual(golden.scenarioSchemaVersion, 1);
assert.strictEqual(golden.scenarios.length, 1);
assert.strictEqual(golden.scenarios[0].category, 'level');
assert.strictEqual(golden.scenarios[0].severity, 'high-impact');
assert.deepStrictEqual(golden.scenarios[0].state, {
  gameActive: true,
  turn: 2,
  gold: 5,
  tavernTier: 1,
  tavernUpgradeCost: 5
});
assert.strictEqual(golden.scenarios[0].candidates[0].type, 'Upgrade');
assert.strictEqual(golden.scenarios[0].candidates[0].targetIndex, -1);
assert.strictEqual(golden.scenarios[0].actionLabels[0].actionKey, 'Upgrade|-1||');
assert.strictEqual(golden.scenarios[0].actionLabels[0].grade, 'preferred');
assert.deepStrictEqual(golden.scenarios[0].acceptedActionKeys, ['Upgrade|-1||']);
assert.strictEqual(golden.scenarios[0].legacyActionKey, 'Upgrade|-1||');
console.log('PASS algorithm workspace and stable action contracts');

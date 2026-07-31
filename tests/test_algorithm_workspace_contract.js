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

const { assertExternalDataRoot, actionKey, scenarioSchemaVersion } = require('../tools/model/contracts');
assert.throws(() => assertExternalDataRoot(repo, repo), /outside the repository/);
assert.throws(() => assertExternalDataRoot(path.join(process.env.SystemDrive, 'bobcoach-algorithm'), repo), /system drive/);
assert.throws(() => assertExternalDataRoot(path.dirname(repo), repo), /cannot contain repository/);
assert.strictEqual(actionKey({ type: 'Upgrade', targetIndex: -1, cardId: '', purchaseSource: '' }),
  'Upgrade|-1||');

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
assert.strictEqual(golden.scenarios[0].candidates[0].type, 'Upgrade');
assert.strictEqual(golden.scenarios[0].candidates[0].targetIndex, -1);
assert.strictEqual(golden.scenarios[0].actionLabels[0].actionKey, 'Upgrade|-1||');
assert.strictEqual(golden.scenarios[0].actionLabels[0].grade, 'preferred');
assert.deepStrictEqual(golden.scenarios[0].acceptedActionKeys, ['Upgrade|-1||']);
assert.strictEqual(golden.scenarios[0].legacyActionKey, 'Upgrade|-1||');
console.log('PASS algorithm workspace and stable action contracts');

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

const { assertExternalDataRoot, actionKey } = require('../tools/model/contracts');
assert.throws(() => assertExternalDataRoot(repo, repo), /outside the repository/);
assert.throws(() => assertExternalDataRoot(path.join(process.env.SystemDrive, 'bobcoach-algorithm'), repo), /system drive/);
assert.strictEqual(actionKey({ type: 'Upgrade', targetIndex: -1, cardId: '', purchaseSource: '' }),
  'Upgrade|-1||');
console.log('PASS algorithm workspace and stable action contracts');

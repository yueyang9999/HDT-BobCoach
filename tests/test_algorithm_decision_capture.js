'use strict';

const assert = require('assert');
const childProcess = require('child_process');
const fs = require('fs');
const path = require('path');

const repositoryRoot = path.resolve(__dirname, '..');
const validator = path.join(repositoryRoot, 'tools', 'model',
  'validate_decision_event_capture.js');
const evidenceGenerated = 'E:\\HDT-BobCoach-evidence\\season14\\generated';
const tempPrefix = 't13-capture-validator-';

function run(args) {
  return childProcess.spawnSync(process.execPath, [validator, ...args], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  });
}

function makeTempRoot() {
  return fs.mkdtempSync(path.join(evidenceGenerated, tempPrefix));
}

function removeTempRoot(root) {
  const resolved = path.resolve(root);
  const expectedBase = path.resolve(evidenceGenerated) + path.sep;
  assert.ok(resolved.startsWith(expectedBase), `unsafe temporary cleanup path: ${resolved}`);
  assert.ok(path.basename(resolved).startsWith(tempPrefix),
    `unexpected temporary directory: ${resolved}`);
  fs.rmSync(resolved, { recursive: true, force: true });
}

function assertCliError(result, pattern, message) {
  assert.notStrictEqual(result.status, 0, message);
  assert.match(result.stderr, pattern, message);
}

const source = fs.readFileSync(validator, 'utf8');
assert.ok(!source.includes("require('fs')"), 'validator must not require fs directly');
assert.ok(!source.includes('fs.readFileSync'), 'validator must not read external files directly');
assert.ok(!source.includes('fs.writeFileSync'), 'validator must not write external files directly');

const tempRoot = makeTempRoot();
try {
  const missingArgs = run([]);
  assertCliError(missingArgs, /required option: --data-root <data root>/i,
    'missing --data-root must fail closed');

  const relativeRoot = run(['--data-root', 'relative\\data-root']);
  assertCliError(relativeRoot, /absolute path/i,
    'relative data root must fail closed');

  const systemRoot = path.join(process.env.SystemDrive || 'C:', 'Windows', 'Temp');
  assertCliError(run(['--data-root', systemRoot]),
    /system drive/i, 'system drive data root must fail closed');

  assertCliError(run(['--data-root', repositoryRoot]),
    /outside the repository/i, 'repository-contained data root must fail closed');

  const missingRoot = path.join(tempRoot, 'missing');
  assertCliError(run(['--data-root', missingRoot]),
    /does not exist/i, 'missing data root must fail closed');

  assertCliError(run(['--unknown', tempRoot]),
    /unknown option/i, 'unknown option must fail closed');

  const target = path.join(tempRoot, 'target');
  const link = path.join(tempRoot, 'link');
  fs.mkdirSync(target);
  fs.symlinkSync(target, link, 'junction');
  assertCliError(run(['--data-root', link]),
    /reparse point/i, 'reparse-point data root must fail closed');
  fs.rmdirSync(link);
  fs.rmdirSync(target);

  const valid = run(['--data-root', tempRoot]);
  assert.strictEqual(valid.status, 0, valid.stderr);
  assert.match(valid.stdout, /decision-events\.jsonl/,
    'validator must print the planned capture file path');
  assert.match(valid.stdout, /ingest_decision_events\.js/,
    'validator must print the next ingest command');
  assert.strictEqual(fs.readdirSync(tempRoot).length, 0,
    'validator must not create files in the data root');
} finally {
  removeTempRoot(tempRoot);
}

console.log('PASS decision-event capture root validation');

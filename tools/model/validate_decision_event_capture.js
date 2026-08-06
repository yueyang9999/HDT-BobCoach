'use strict';

const path = require('path');

const { assertExternalDataRoot, resolveDataPath } = require('./contracts');

function parseCliArgs(args) {
  const options = {};
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg !== '--data-root') {
      throw new Error(`unknown option: ${arg}`);
    }
    const value = args[index + 1];
    if (!value || value.startsWith('--')) {
      throw new Error('required option: --data-root <data root>');
    }
    options['data-root'] = value;
    index += 1;
  }
  return options;
}

function validateCaptureRoot(dataRoot) {
  if (!dataRoot) {
    throw new Error('required option: --data-root <data root>');
  }
  if (!path.isAbsolute(dataRoot)) {
    throw new Error('data root must be an absolute path');
  }

  const repositoryRoot = path.resolve(__dirname, '..', '..');
  const resolvedRoot = assertExternalDataRoot(dataRoot, repositoryRoot);
  return resolveDataPath(resolvedRoot, 'decision-events.jsonl', 'capture file',
    { allowMissing: true });
}

function main(argv) {
  const options = parseCliArgs(argv);
  const capturePath = validateCaptureRoot(options['data-root']);
  process.stdout.write(`decision-events capture root is ready: ${capturePath}\n`);
  process.stdout.write('next: node tools\\model\\ingest_decision_events.js '
    + '--data-root <data-root> --input decision-events.jsonl '
    + '--output reports\\decision-events-aggregate.json\n');
}

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = {
  validateCaptureRoot,
};

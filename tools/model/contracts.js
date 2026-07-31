'use strict';
const path = require('path');
function assertExternalDataRoot(root, repositoryRoot) {
  if (!root) throw new Error('--data-root is required');
  const resolved = path.resolve(root);
  const repo = path.resolve(repositoryRoot);
  const systemRoot = path.parse((process.env.SystemDrive || '') + path.sep).root;
  if (systemRoot && path.parse(resolved).root.toLowerCase() === systemRoot.toLowerCase())
    throw new Error('data root must not be on the system drive');
  const relative = path.relative(repo, resolved);
  if (relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..'))
    throw new Error('data root must be outside the repository');
  const repositoryRelative = path.relative(resolved, repo);
  if (repositoryRelative !== '' &&
      !repositoryRelative.startsWith('..' + path.sep) && repositoryRelative !== '..')
    throw new Error('data root cannot contain repository');
  return resolved;
}
function actionKey(action) {
  return [action.type, action.targetIndex, action.cardId || '', action.purchaseSource || ''].join('|');
}
module.exports = { assertExternalDataRoot, actionKey, scenarioSchemaVersion: 1 };

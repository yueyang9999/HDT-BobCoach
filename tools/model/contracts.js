'use strict';
const fs = require('fs');
const path = require('path');

function isWithin(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative === '' || (relative !== '..' && !relative.startsWith('..' + path.sep)
    && !path.isAbsolute(relative));
}

function assertNoReparsePoints(target, name) {
  const resolved = path.resolve(target);
  const parsed = path.parse(resolved);
  let current = parsed.root;
  for (const segment of path.relative(parsed.root, resolved).split(path.sep).filter(Boolean)) {
    current = path.join(current, segment);
    let stats;
    try {
      stats = fs.lstatSync(current);
    } catch (error) {
      if (error && error.code === 'ENOENT') break;
      throw error;
    }
    if (stats.isSymbolicLink()) throw new Error(`${name} contains a reparse point: ${current}`);
  }
}

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
  if (!fs.existsSync(resolved) || !fs.statSync(resolved).isDirectory())
    throw new Error(`data root does not exist: ${resolved}`);
  assertNoReparsePoints(resolved, 'data root');
  const physicalRoot = fs.realpathSync.native(resolved);
  const physicalRepo = fs.realpathSync.native(repo);
  if (isWithin(physicalRepo, physicalRoot)) throw new Error('data root must be outside the repository');
  if (isWithin(physicalRoot, physicalRepo)) throw new Error('data root cannot contain repository');
  return resolved;
}

function resolveDataPath(root, relativePath, name, options = {}) {
  if (!relativePath) throw new Error(`${name} is required`);
  if (path.isAbsolute(relativePath)) throw new Error(`${name} must be relative to data root`);
  const resolvedRoot = path.resolve(root);
  const resolved = path.resolve(resolvedRoot, relativePath);
  if (!isWithin(resolvedRoot, resolved) || resolved === resolvedRoot)
    throw new Error(`${name} escapes data root`);
  assertNoReparsePoints(resolvedRoot, 'data root');
  assertNoReparsePoints(resolved, name);

  let existing = resolved;
  while (!fs.existsSync(existing)) {
    const parent = path.dirname(existing);
    if (parent === existing || !isWithin(resolvedRoot, parent))
      throw new Error(`${name} escapes data root`);
    existing = parent;
  }
  if (!options.allowMissing && existing !== resolved)
    throw new Error(`${name} does not exist: ${resolved}`);
  const physicalRoot = fs.realpathSync.native(resolvedRoot);
  const physicalExisting = fs.realpathSync.native(existing);
  if (!isWithin(physicalRoot, physicalExisting)) throw new Error(`${name} escapes data root`);
  return resolved;
}
function actionKey(action) {
  return [action.type, action.targetIndex, action.cardId || '', action.purchaseSource || ''].join('|');
}
module.exports = { assertExternalDataRoot, resolveDataPath, actionKey, scenarioSchemaVersion: 1 };

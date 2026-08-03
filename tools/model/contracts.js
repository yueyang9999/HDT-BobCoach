'use strict';
const crypto = require('crypto');
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
function cardPoolIdentity(cardIds) {
  if (cardIds === null || cardIds === undefined) return 'unknown';
  if (!Array.isArray(cardIds)) throw new Error('cardPoolIdentity expects an array of card ids or null');
  const ids = cardIds.map((cardId) => {
    if (typeof cardId !== 'string' || cardId.length === 0)
      throw new Error('card ids must be non-empty strings');
    return cardId;
  });
  if (ids.length === 0) throw new Error('card pool must not be empty; pass null for unknown');
  const sorted = [...new Set(ids)].sort((a, b) =>
    Buffer.compare(Buffer.from(a, 'utf8'), Buffer.from(b, 'utf8')));
  const digest = crypto.createHash('sha256')
    .update(JSON.stringify(sorted), 'utf8')
    .digest('hex');
  return digest.slice(0, 16);
}
function contractHeader(options) {
  if (!options || typeof options !== 'object')
    throw new Error('contractHeader expects an options object');
  for (const key of ['season', 'gameBuild', 'cardPoolIdentity', 'source', 'capturedAt', 'sha256']) {
    const value = options[key];
    if (typeof value !== 'string' || value.trim().length === 0)
      throw new Error(`contract header requires a non-empty ${key}`);
  }
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/.test(options.capturedAt))
    throw new Error('capturedAt must be an ISO-8601 UTC timestamp');
  if (!/^[a-f0-9]{64}$/i.test(options.sha256))
    throw new Error('sha256 must be a 64-character hexadecimal digest');
  const header = {
    schemaVersion: 1,
    season: options.season,
    gameBuild: options.gameBuild,
    cardPoolIdentity: options.cardPoolIdentity,
    source: options.source,
    capturedAt: options.capturedAt,
    sha256: options.sha256.toLowerCase(),
  };
  if (options.provenance !== undefined) {
    if (!options.provenance || typeof options.provenance !== 'object' || Array.isArray(options.provenance))
      throw new Error('provenance must be an object');
    header.provenance = options.provenance;
  }
  return header;
}
module.exports = {
  assertExternalDataRoot, resolveDataPath, actionKey, cardPoolIdentity, contractHeader,
  scenarioSchemaVersion: 1
};

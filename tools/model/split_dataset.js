'use strict';

function splitRows(rows, holdoutGameCount) {
  if (!Array.isArray(rows) || rows.length === 0) throw new Error('pair rows are required');
  const deduplicated = new Map();
  const partitions = new Map();
  rows.forEach(row => {
    validateRow(row);
    const partition = row.partition || 'training';
    const priorPartition = partitions.get(row.gameId);
    if (priorPartition && priorPartition !== partition) throw new Error('whole-game split violation');
    partitions.set(row.gameId, partition);
    const key = row.stateFingerprint + '|' + row.preferred.candidateFingerprint;
    const prior = deduplicated.get(key);
    if (!prior || compareRows(prior, row) < 0) deduplicated.set(key, row);
  });
  const groups = new Map();
  Array.from(deduplicated.values()).forEach(row => {
    if (!groups.has(row.gameId)) groups.set(row.gameId, []);
    groups.get(row.gameId).push(row);
  });
  groups.forEach(gameRows => gameRows.sort(compareStableRowKey));
  const games = Array.from(groups.entries()).sort((left, right) => {
    const leftTime = newestTimestamp(left[1]);
    const rightTime = newestTimestamp(right[1]);
    return leftTime === rightTime ? compareOrdinalText(left[0], right[0]) : leftTime - rightTime;
  });
  const holdoutCount = holdoutGameCount === undefined ? Math.max(1, Math.floor(games.length / 5)) : holdoutGameCount;
  if (!Number.isInteger(holdoutCount) || holdoutCount < 1 || holdoutCount >= games.length)
    throw new Error('holdout must reserve whole games');
  const splitIndex = games.length - holdoutCount;
  return {
    training: games.slice(0, splitIndex).flatMap(([, gameRows]) => gameRows),
    holdout: games.slice(splitIndex).flatMap(([, gameRows]) => gameRows)
  };
}

function validateRow(row) {
  if (!row || typeof row !== 'object' || !text(row.gameId) || !text(row.capturedAtUtc)
    || !text(row.stateFingerprint) || !row.preferred || !row.other
    || !text(row.preferred.candidateFingerprint) || !text(row.other.candidateFingerprint))
    throw new Error('invalid pair row');
  if (Number.isNaN(Date.parse(row.capturedAtUtc))) throw new Error('invalid pair timestamp');
}

function newestTimestamp(rows) { return Math.max.apply(null, rows.map(row => Date.parse(row.capturedAtUtc))); }
function compareRows(left, right) {
  const timeDifference = Date.parse(left.capturedAtUtc) - Date.parse(right.capturedAtUtc);
  return timeDifference || compareStableRowKey(left, right);
}
function compareStableRowKey(left, right) {
  const leftKey = stableJson(left);
  const rightKey = stableJson(right);
  return compareOrdinalText(leftKey, rightKey);
}
function compareOrdinalText(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
function stableJson(value) {
  if (Array.isArray(value)) return '[' + value.map(stableJson).join(',') + ']';
  if (value && typeof value === 'object') {
    return '{' + Object.keys(value).sort()
      .map(key => JSON.stringify(key) + ':' + stableJson(value[key])).join(',') + '}';
  }
  return JSON.stringify(value);
}
function text(value) { return typeof value === 'string' && value.trim(); }
module.exports = { splitRows };

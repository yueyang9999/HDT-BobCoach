'use strict';

const fs = require('fs');
const path = require('path');

const EVENT_TYPES = new Set([
  'DecisionCreated',
  'PlayerActionObserved',
  'RecommendationIssued',
  'CombatOutcomeObserved',
  'ShadowComparison',
]);

const TOP_LEVEL_FIELDS = new Set([
  'eventKey',
  'eventType',
  'modelId',
  'modelPayloadSha256',
  'payload',
]);

const PAYLOAD_FIELDS = {
  DecisionCreated: new Set([
    'DecisionId',
    'GameId',
    'State',
    'CandidateKeys',
    'StateFingerprint',
    'CandidateFingerprint',
  ]),
  PlayerActionObserved: new Set(['decisionId', 'observedAtUtc', 'actionKey']),
  RecommendationIssued: new Set(['decisionId', 'observedAtUtc', 'result']),
  CombatOutcomeObserved: new Set(['decisionId', 'observedAtUtc', 'outcome']),
  ShadowComparison: new Set([
    'DecisionId',
    'ComparisonId',
    'StateFingerprint',
    'ComparedActionFingerprint',
    'LegacyPolicyId',
    'UnifiedPolicyId',
    'LegacySelectedActionKey',
    'UnifiedSelectedActionKey',
    'Agrees',
    'LegacyConfidence',
    'UnifiedConfidence',
    'LegacyPlannerStatus',
    'UnifiedPlannerStatus',
    'LegacyFallbackReason',
    'UnifiedFallbackReason',
    'ScoreMargin',
  ]),
};

const STATE_FIELDS = new Set([
  'GameActive',
  'IsDuos',
  'GameBuild',
  'SeasonId',
  'CardPoolIdentity',
  'HeroCardId',
  'AnomalyId',
  'Gold',
  'MaxGold',
  'TavernTier',
  'TavernUpgradeCost',
  'Health',
  'MaxHealth',
  'Armor',
  'Turn',
  'Phase',
  'FrozenShop',
  'FreeRefreshCount',
  'FirstPurchaseUsedThisTurn',
  'FirstMinionPurchaseUsedThisTurn',
  'CurrentCompGuideTier',
  'AvailableTribes',
  'BoardMinions',
  'HandMinions',
  'ShopMinions',
  'TrinketOffer',
  'ActiveTrinkets',
]);

const MINION_FIELDS = new Set([
  'CardId',
  'EntityId',
  'Attack',
  'Health',
  'Tier',
  'Position',
  'Golden',
  'Taunt',
  'DivineShield',
  'Windfury',
  'MegaWindfury',
  'Stealth',
  'Reborn',
  'Poisonous',
  'Venomous',
  'Cleave',
  'Overkill',
  'IsSpell',
  'Cost',
  'Tribe',
]);

const TRINKET_FIELDS = new Set([
  'CardId',
  'TrinketName',
  'EntityId',
  'IsLesser',
  'Tier',
  'Attack',
  'Health',
  'Tribes',
]);

const RESULT_FIELDS = new Set([
  'PolicyId',
  'SelectedAction',
  'RankedActions',
  'Confidence',
  'PlannerStatus',
  'FallbackReason',
  'Explanation',
]);

const RANKED_ACTION_FIELDS = new Set(['Action', 'ActionKey', 'Score', 'Factors']);
const FINGERPRINT_PATTERN = /^[a-f0-9]{64}$/;

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function assertNoUnknownFields(object, allowed, label) {
  for (const key of Object.keys(object)) {
    if (!allowed.has(key)) {
      throw new Error(`${label} contains unknown field "${key}"`);
    }
  }
}

function requireString(object, key, label) {
  if (typeof object[key] !== 'string') {
    throw new Error(`${label} requires string field "${key}"`);
  }
  return object[key];
}

function requireNonEmptyString(object, key, label) {
  const value = requireString(object, key, label);
  if (value.trim().length === 0) {
    throw new Error(`${label} requires non-empty string field "${key}"`);
  }
  return value;
}

function requireBoolean(object, key, label) {
  if (typeof object[key] !== 'boolean') {
    throw new Error(`${label} requires boolean field "${key}"`);
  }
  return object[key];
}

function requireFiniteNumber(object, key, label) {
  const value = object[key];
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error(`${label} requires finite number field "${key}"`);
  }
  return value;
}

function requireObject(object, key, label) {
  if (!isObject(object[key])) {
    throw new Error(`${label} requires object field "${key}"`);
  }
  return object[key];
}

function requireArray(object, key, label) {
  if (!Array.isArray(object[key])) {
    throw new Error(`${label} requires array field "${key}"`);
  }
  return object[key];
}

function requireStringArray(object, key, label) {
  const value = requireArray(object, key, label);
  if (!value.every(item => typeof item === 'string')) {
    throw new Error(`${label} requires string array field "${key}"`);
  }
  return value;
}

function requireFingerprint(object, key, label) {
  const value = requireString(object, key, label);
  if (!FINGERPRINT_PATTERN.test(value)) {
    throw new Error(`${label} requires lowercase SHA-256 fingerprint field "${key}"`);
  }
  return value;
}

function validateMinion(minion, label) {
  assertNoUnknownFields(minion, MINION_FIELDS, label);
  requireString(minion, 'CardId', label);
  requireFiniteNumber(minion, 'EntityId', label);
  requireFiniteNumber(minion, 'Attack', label);
  requireFiniteNumber(minion, 'Health', label);
  requireFiniteNumber(minion, 'Tier', label);
  requireFiniteNumber(minion, 'Position', label);
  requireBoolean(minion, 'Golden', label);
  requireBoolean(minion, 'Taunt', label);
  requireBoolean(minion, 'DivineShield', label);
  requireBoolean(minion, 'Windfury', label);
  requireBoolean(minion, 'MegaWindfury', label);
  requireBoolean(minion, 'Stealth', label);
  requireBoolean(minion, 'Reborn', label);
  requireBoolean(minion, 'Poisonous', label);
  requireBoolean(minion, 'Venomous', label);
  requireBoolean(minion, 'Cleave', label);
  requireBoolean(minion, 'Overkill', label);
  requireBoolean(minion, 'IsSpell', label);
  requireFiniteNumber(minion, 'Cost', label);
  requireString(minion, 'Tribe', label);
}

function validateTrinket(trinket, label) {
  assertNoUnknownFields(trinket, TRINKET_FIELDS, label);
  requireString(trinket, 'CardId', label);
  requireString(trinket, 'TrinketName', label);
  requireFiniteNumber(trinket, 'EntityId', label);
  requireBoolean(trinket, 'IsLesser', label);
  requireFiniteNumber(trinket, 'Tier', label);
  requireFiniteNumber(trinket, 'Attack', label);
  requireFiniteNumber(trinket, 'Health', label);
  requireStringArray(trinket, 'Tribes', label);
}

function validateState(state, label) {
  assertNoUnknownFields(state, STATE_FIELDS, label);
  requireBoolean(state, 'GameActive', label);
  requireBoolean(state, 'IsDuos', label);
  requireString(state, 'GameBuild', label);
  requireString(state, 'SeasonId', label);
  requireString(state, 'CardPoolIdentity', label);
  requireString(state, 'HeroCardId', label);
  requireString(state, 'AnomalyId', label);
  requireFiniteNumber(state, 'Gold', label);
  requireFiniteNumber(state, 'MaxGold', label);
  requireFiniteNumber(state, 'TavernTier', label);
  requireFiniteNumber(state, 'TavernUpgradeCost', label);
  requireFiniteNumber(state, 'Health', label);
  requireFiniteNumber(state, 'MaxHealth', label);
  requireFiniteNumber(state, 'Armor', label);
  requireFiniteNumber(state, 'Turn', label);
  requireString(state, 'Phase', label);
  requireBoolean(state, 'FrozenShop', label);
  requireFiniteNumber(state, 'FreeRefreshCount', label);
  requireBoolean(state, 'FirstPurchaseUsedThisTurn', label);
  requireBoolean(state, 'FirstMinionPurchaseUsedThisTurn', label);
  requireString(state, 'CurrentCompGuideTier', label);
  requireStringArray(state, 'AvailableTribes', label);
  for (const listName of ['BoardMinions', 'HandMinions', 'ShopMinions']) {
    for (const item of requireArray(state, listName, label)) {
      if (!isObject(item)) throw new Error(`${label} requires object list "${listName}"`);
      validateMinion(item, `${label}.${listName}`);
    }
  }
  for (const item of requireArray(state, 'TrinketOffer', label)) {
    if (!isObject(item)) throw new Error(`${label} requires object list "TrinketOffer"`);
    validateTrinket(item, `${label}.TrinketOffer`);
  }
  requireStringArray(state, 'ActiveTrinkets', label);
}

function validateResult(result, label) {
  assertNoUnknownFields(result, RESULT_FIELDS, label);
  requireString(result, 'PolicyId', label);
  if (result.SelectedAction !== null && !isObject(result.SelectedAction)) {
    throw new Error(`${label} requires nullable object field "SelectedAction"`);
  }
  const rankedActions = requireArray(result, 'RankedActions', label);
  for (const ranked of rankedActions) {
    if (!isObject(ranked)) throw new Error(`${label} requires object list "RankedActions"`);
    assertNoUnknownFields(ranked, RANKED_ACTION_FIELDS, `${label}.RankedActions`);
    requireString(ranked, 'ActionKey', `${label}.RankedActions`);
    requireFiniteNumber(ranked, 'Score', `${label}.RankedActions`);
    requireArray(ranked, 'Factors', `${label}.RankedActions`);
  }
  requireFiniteNumber(result, 'Confidence', label);
  requireString(result, 'PlannerStatus', label);
  requireString(result, 'FallbackReason', label);
  requireString(result, 'Explanation', label);
}

function validateDecisionCreated(payload, label) {
  assertNoUnknownFields(payload, PAYLOAD_FIELDS.DecisionCreated, label);
  requireNonEmptyString(payload, 'DecisionId', label);
  requireNonEmptyString(payload, 'GameId', label);
  validateState(requireObject(payload, 'State', label), `${label}.State`);
  requireStringArray(payload, 'CandidateKeys', label);
  requireFingerprint(payload, 'StateFingerprint', label);
  requireFingerprint(payload, 'CandidateFingerprint', label);
}

function validateObservedAction(payload, label) {
  assertNoUnknownFields(payload, PAYLOAD_FIELDS.PlayerActionObserved, label);
  requireNonEmptyString(payload, 'decisionId', label);
  requireNonEmptyString(payload, 'observedAtUtc', label);
  requireNonEmptyString(payload, 'actionKey', label);
}

function validateRecommendation(payload, label) {
  assertNoUnknownFields(payload, PAYLOAD_FIELDS.RecommendationIssued, label);
  requireNonEmptyString(payload, 'decisionId', label);
  requireNonEmptyString(payload, 'observedAtUtc', label);
  validateResult(requireObject(payload, 'result', label), `${label}.result`);
}

function validateCombatOutcome(payload, label) {
  assertNoUnknownFields(payload, PAYLOAD_FIELDS.CombatOutcomeObserved, label);
  requireNonEmptyString(payload, 'decisionId', label);
  requireNonEmptyString(payload, 'observedAtUtc', label);
  const outcome = requireObject(payload, 'outcome', label);
  if (Object.keys(outcome).length !== 1 || !Object.prototype.hasOwnProperty.call(outcome, 'ResultSummary')) {
    throw new Error(`${label}.outcome must contain exactly the ResultSummary field`);
  }
  requireString(outcome, 'ResultSummary', `${label}.outcome`);
}

function validateShadowComparison(payload, label) {
  assertNoUnknownFields(payload, PAYLOAD_FIELDS.ShadowComparison, label);
  requireNonEmptyString(payload, 'DecisionId', label);
  requireNonEmptyString(payload, 'ComparisonId', label);
  requireFingerprint(payload, 'StateFingerprint', label);
  requireFingerprint(payload, 'ComparedActionFingerprint', label);
  requireString(payload, 'LegacyPolicyId', label);
  requireString(payload, 'UnifiedPolicyId', label);
  requireString(payload, 'LegacySelectedActionKey', label);
  requireString(payload, 'UnifiedSelectedActionKey', label);
  requireBoolean(payload, 'Agrees', label);
  requireFiniteNumber(payload, 'LegacyConfidence', label);
  requireFiniteNumber(payload, 'UnifiedConfidence', label);
  requireString(payload, 'LegacyPlannerStatus', label);
  requireString(payload, 'UnifiedPlannerStatus', label);
  requireString(payload, 'LegacyFallbackReason', label);
  requireString(payload, 'UnifiedFallbackReason', label);
  requireFiniteNumber(payload, 'ScoreMargin', label);
}

function decisionIdOf(event) {
  const payload = event.payload;
  if (event.eventType === 'DecisionCreated' || event.eventType === 'ShadowComparison') {
    return payload.DecisionId;
  }
  return payload.decisionId;
}

function validateEvent(event, fileLabel, lineNumber) {
  const label = `${fileLabel} line ${lineNumber}`;
  if (!isObject(event)) throw new Error(`${label} is not a JSON object`);
  assertNoUnknownFields(event, TOP_LEVEL_FIELDS, label);
  requireNonEmptyString(event, 'eventKey', label);
  requireNonEmptyString(event, 'eventType', label);
  requireString(event, 'modelId', label);
  requireString(event, 'modelPayloadSha256', label);
  requireObject(event, 'payload', label);
  if (!EVENT_TYPES.has(event.eventType)) {
    throw new Error(`${label} has unknown event type "${event.eventType}"`);
  }
  const decisionId = decisionIdOf(event);
  requireNonEmptyString({ DecisionId: decisionId }, 'DecisionId', label);
  if (event.eventType === 'DecisionCreated') validateDecisionCreated(event.payload, label);
  else if (event.eventType === 'PlayerActionObserved') validateObservedAction(event.payload, label);
  else if (event.eventType === 'RecommendationIssued') validateRecommendation(event.payload, label);
  else if (event.eventType === 'CombatOutcomeObserved') validateCombatOutcome(event.payload, label);
  else validateShadowComparison(event.payload, label);
}

function parseActionOrdinal(event, label) {
  const decisionId = decisionIdOf(event);
  const prefix = `${decisionId}|action|`;
  if (!event.eventKey.startsWith(prefix)) {
    throw new Error(`${label} eventKey must start with decision action prefix`);
  }
  const remainder = event.eventKey.slice(prefix.length);
  const separator = remainder.indexOf('|');
  if (separator < 0) throw new Error(`${label} eventKey must contain an action ordinal`);
  const ordinalText = remainder.slice(0, separator);
  const ordinal = Number(ordinalText);
  if (!Number.isInteger(ordinal) || ordinal < 0) {
    throw new Error(`${label} eventKey has invalid action ordinal "${ordinalText}"`);
  }
  const keyFromEventKey = remainder.slice(separator + 1);
  if (keyFromEventKey !== event.payload.actionKey) {
    throw new Error(`${label} eventKey action does not match payload actionKey`);
  }
  return ordinal;
}

function readEventFile(filePath, fileLabel) {
  let text;
  try {
    text = fs.readFileSync(filePath, 'utf8');
  } catch (error) {
    throw new Error(`${fileLabel} decision event file is unreadable: ${filePath} (${error.message})`);
  }
  const rawLines = text.split(/\r?\n/);
  if (rawLines.length > 0 && rawLines[rawLines.length - 1] === '') rawLines.pop();
  if (rawLines.every(line => line.trim().length === 0)) {
    throw new Error(`${fileLabel} decision event file is empty: ${filePath}`);
  }
  const blankLine = rawLines.findIndex(line => line.trim().length === 0);
  if (blankLine >= 0) {
    throw new Error(`${fileLabel} decision event file has a blank line at line ${blankLine + 1}`);
  }

  const events = [];
  rawLines.forEach((line, index) => {
    const lineNumber = index + 1;
    let event;
    try {
      event = JSON.parse(line);
    } catch (error) {
      throw new Error(`${fileLabel} decision event file has malformed JSONL at line ${lineNumber}`);
    }
    validateEvent(event, fileLabel, lineNumber);
    events.push(event);
  });
  return events;
}

function indexEvents(events, fileLabel) {
  const byDecision = new Map();
  const seenEventKeys = new Set();

  for (const event of events) {
    if (seenEventKeys.has(event.eventKey)) {
      throw new Error(`${fileLabel} contains duplicate event key "${event.eventKey}"`);
    }
    seenEventKeys.add(event.eventKey);
  }

  const decisionIds = new Set();
  for (const event of events) {
    if (event.eventType === 'DecisionCreated') {
      const decisionId = event.payload.DecisionId;
      if (decisionIds.has(decisionId)) {
        throw new Error(`${fileLabel} contains duplicate DecisionCreated for "${decisionId}"`);
      }
      decisionIds.add(decisionId);
    }
  }

  for (const event of events) {
    const decisionId = decisionIdOf(event);
    if (!decisionIds.has(decisionId)) {
      throw new Error(`${fileLabel} event references unknown decision "${decisionId}"`);
    }
    if (!byDecision.has(decisionId)) byDecision.set(decisionId, {});
    const bucket = byDecision.get(decisionId);
    if (bucket[event.eventType]) throw new Error(`${fileLabel} has duplicate ${event.eventType} for "${decisionId}"`);
    bucket[event.eventType] = event;
  }

  return byDecision;
}

function sameJsonValue(left, right) {
  return JSON.stringify(left) === JSON.stringify(right);
}

function normalizeTrinketOffer(offer) {
  return offer.map(trinket => trinket.CardId);
}

function orderedActions(bucket, decisionId) {
  if (!bucket.PlayerActionObserved) return [];
  const event = bucket.PlayerActionObserved;
  const ordinal = parseActionOrdinal(event, `decision ${decisionId}`);
  return [{ ordinal, actionKey: event.payload.actionKey }];
}

function compareDecision(decisionId, baseline, candidate, differences) {
  const baselineState = baseline.DecisionCreated.payload.State;
  const candidateState = candidate.DecisionCreated.payload.State;
  const comparisons = [
    ['CurrentCompGuideTier', baselineState.CurrentCompGuideTier, candidateState.CurrentCompGuideTier],
    ['TrinketOffer', normalizeTrinketOffer(baselineState.TrinketOffer),
      normalizeTrinketOffer(candidateState.TrinketOffer)],
    ['ActiveTrinkets', baselineState.ActiveTrinkets, candidateState.ActiveTrinkets],
    ['CandidateKeys', baseline.DecisionCreated.payload.CandidateKeys,
      candidate.DecisionCreated.payload.CandidateKeys],
    ['StateFingerprint', baseline.DecisionCreated.payload.StateFingerprint,
      candidate.DecisionCreated.payload.StateFingerprint],
  ];
  for (const [kind, baselineValue, candidateValue] of comparisons) {
    if (!sameJsonValue(baselineValue, candidateValue)) {
      differences.push({ kind, decisionId, baseline: baselineValue, candidate: candidateValue });
    }
  }

  const baselineActions = orderedActions(baseline, decisionId);
  const candidateActions = orderedActions(candidate, decisionId);
  if (!sameJsonValue(baselineActions, candidateActions)) {
    const baselineKeys = baselineActions.map(item => item.actionKey);
    const candidateKeys = candidateActions.map(item => item.actionKey);
    const isPickTrinket = baselineKeys[0] && candidateKeys[0]
      && baselineKeys[0].startsWith('PickTrinket|') && candidateKeys[0].startsWith('PickTrinket|');
    differences.push({
      kind: isPickTrinket ? 'PickTrinket' : 'PlayerAction',
      decisionId,
      baseline: baselineKeys,
      candidate: candidateKeys,
    });
  }

  if (baseline.RecommendationIssued && candidate.RecommendationIssued) {
    const baselinePolicy = baseline.RecommendationIssued.payload.result.PolicyId;
    const candidatePolicy = candidate.RecommendationIssued.payload.result.PolicyId;
    if (baselinePolicy !== candidatePolicy) {
      differences.push({
        kind: 'RecommendationPolicy',
        decisionId,
        baseline: baselinePolicy,
        candidate: candidatePolicy,
      });
    }
  } else if (Boolean(baseline.RecommendationIssued) !== Boolean(candidate.RecommendationIssued)) {
    differences.push({
      kind: 'RecommendationMissing',
      decisionId,
      baseline: Boolean(baseline.RecommendationIssued),
      candidate: Boolean(candidate.RecommendationIssued),
    });
  }

  if (baseline.ShadowComparison && candidate.ShadowComparison) {
    const baselineSummary = summarizeShadowComparison(baseline.ShadowComparison.payload);
    const candidateSummary = summarizeShadowComparison(candidate.ShadowComparison.payload);
    if (!sameJsonValue(baselineSummary, candidateSummary)) {
      differences.push({ kind: 'ShadowComparison', decisionId, baseline: baselineSummary, candidate: candidateSummary });
    }
  } else if (Boolean(baseline.ShadowComparison) !== Boolean(candidate.ShadowComparison)) {
    differences.push({
      kind: 'ShadowComparisonMissing',
      decisionId,
      baseline: Boolean(baseline.ShadowComparison),
      candidate: Boolean(candidate.ShadowComparison),
    });
  }

  if (baseline.CombatOutcomeObserved && candidate.CombatOutcomeObserved) {
    const baselineOutcome = baseline.CombatOutcomeObserved.payload.outcome.ResultSummary;
    const candidateOutcome = candidate.CombatOutcomeObserved.payload.outcome.ResultSummary;
    if (baselineOutcome !== candidateOutcome) {
      differences.push({ kind: 'CombatOutcome', decisionId, baseline: baselineOutcome, candidate: candidateOutcome });
    }
  } else if (Boolean(baseline.CombatOutcomeObserved) !== Boolean(candidate.CombatOutcomeObserved)) {
    differences.push({
      kind: 'CombatOutcomeMissing',
      decisionId,
      baseline: Boolean(baseline.CombatOutcomeObserved),
      candidate: Boolean(candidate.CombatOutcomeObserved),
    });
  }
}

function summarizeShadowComparison(payload) {
  return {
    agrees: payload.Agrees,
    legacySelectedActionKey: payload.LegacySelectedActionKey,
    unifiedSelectedActionKey: payload.UnifiedSelectedActionKey,
    scoreMargin: payload.ScoreMargin,
  };
}

function summarizeFile(events) {
  const decisionIds = [...new Set(events.map(decisionIdOf))].sort();
  const modelIds = [...new Set(events.map(event => event.modelId))];
  const payloadHashes = [...new Set(events.map(event => event.modelPayloadSha256))];
  return {
    eventCount: events.length,
    decisionCount: decisionIds.length,
    decisionIds,
    modelId: modelIds.length === 1 ? modelIds[0] : 'mixed',
    modelPayloadSha256: payloadHashes.length === 1 ? payloadHashes[0] : 'mixed',
  };
}

function compareDecisionEventFiles(baselinePath, candidatePath) {
  const baselineEvents = readEventFile(baselinePath, 'baseline');
  const candidateEvents = readEventFile(candidatePath, 'candidate');
  const baselineByDecision = indexEvents(baselineEvents, 'baseline');
  const candidateByDecision = indexEvents(candidateEvents, 'candidate');

  const baselineDecisionIds = [...baselineByDecision.keys()].sort();
  const candidateDecisionIds = [...candidateByDecision.keys()].sort();
  const baselineOnly = baselineDecisionIds.filter(id => !candidateByDecision.has(id));
  const candidateOnly = candidateDecisionIds.filter(id => !baselineByDecision.has(id));
  const comparedDecisionIds = baselineDecisionIds.filter(id => candidateByDecision.has(id));
  const differences = [];

  if (baselineOnly.length > 0 || candidateOnly.length > 0) {
    differences.push({
      kind: 'DecisionSet',
      baselineOnly,
      candidateOnly,
    });
  }

  for (const decisionId of comparedDecisionIds) {
    compareDecision(decisionId, baselineByDecision.get(decisionId),
      candidateByDecision.get(decisionId), differences);
  }

  return {
    schemaVersion: 1,
    baseline: {
      path: path.resolve(baselinePath),
      ...summarizeFile(baselineEvents),
    },
    candidate: {
      path: path.resolve(candidatePath),
      ...summarizeFile(candidateEvents),
    },
    comparedDecisionCount: comparedDecisionIds.length,
    differences,
    equal: differences.length === 0,
  };
}

function parseCliArgs(args) {
  const options = {};
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg === '--baseline' || arg === '--candidate') {
      const value = args[index + 1];
      if (!value || value.startsWith('--')) {
        throw new Error(`required option: ${arg} <path>`);
      }
      options[arg.slice(2)] = value;
      index += 1;
    } else {
      throw new Error(`unknown option: ${arg}`);
    }
  }
  return options;
}

if (require.main === module) {
  try {
    const options = parseCliArgs(process.argv.slice(2));
    const missingOptions = [];
    if (!options.baseline) missingOptions.push('--baseline <path>');
    if (!options.candidate) missingOptions.push('--candidate <path>');
    if (missingOptions.length > 0) {
      throw new Error(`required option: ${missingOptions.join(', ')}`);
    }
    const report = compareDecisionEventFiles(options.baseline, options.candidate);
    process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = {
  compareDecisionEventFiles,
  readEventFile,
  indexEvents,
};

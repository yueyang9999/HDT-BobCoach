using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>把早到Power.log批次绑定到开局第二技能发现规则。</summary>
    public sealed class SecondHeroPowerDiscoverTracker
    {
        private sealed class RawBatch
        {
            public int ChoiceId;
            public List<string> CandidateCardIds;
            public string EvidenceSource;
        }

        private sealed class RawSelection
        {
            public int ChoiceId;
            public string SelectedCardId;
            public string EvidenceSource;
        }

        private readonly List<RawBatch> _rawBatches = new List<RawBatch>();
        private readonly object _sync = new object();
        private readonly List<RawSelection> _rawSelections = new List<RawSelection>();
        private readonly List<SecondHeroPowerChoiceExpectation> _expectations =
            new List<SecondHeroPowerChoiceExpectation>();
        private readonly List<SecondHeroPowerChoiceBatchObservation> _observedBatches =
            new List<SecondHeroPowerChoiceBatchObservation>();
        private readonly List<SecondHeroPowerChoiceSelection> _selections =
            new List<SecondHeroPowerChoiceSelection>();
        private readonly List<SecondHeroPowerEntityObservation> _entityObservations =
            new List<SecondHeroPowerEntityObservation>();

        public IList<SecondHeroPowerChoiceExpectation> Expectations
        {
            get { lock (_sync) return new List<SecondHeroPowerChoiceExpectation>(_expectations); }
        }
        public IList<SecondHeroPowerChoiceBatchObservation> ObservedBatches
        {
            get { lock (_sync) return new List<SecondHeroPowerChoiceBatchObservation>(_observedBatches); }
        }
        public IList<SecondHeroPowerChoiceSelection> Selections
        {
            get { lock (_sync) return new List<SecondHeroPowerChoiceSelection>(_selections); }
        }
        public IList<SecondHeroPowerEntityObservation> EntityObservations
        {
            get { lock (_sync) return new List<SecondHeroPowerEntityObservation>(_entityObservations); }
        }

        public bool ObserveBatch(
            int choiceId,
            int observedTurn,
            IEnumerable<string> candidateCardIds,
            string evidenceSource)
        {
            lock (_sync)
            {
                var rawCandidates = (candidateCardIds ?? Enumerable.Empty<string>()).ToList();
                var candidates = rawCandidates.Distinct().ToList();
                if (choiceId < 0 || observedTurn > 1 || observedTurn == 0
                    || evidenceSource != "power_log"
                    || rawCandidates.Count < 2 || rawCandidates.Count > 3
                    || rawCandidates.Any(string.IsNullOrEmpty)
                    || candidates.Count != rawCandidates.Count
                    || candidates.Any(cardId => !HeroPowerChoiceIdentity.IsEligible(cardId)))
                    return false;

                var existing = _rawBatches.FirstOrDefault(item => item.ChoiceId == choiceId);
                if (existing == null)
                {
                    _rawBatches.Add(new RawBatch
                    {
                        ChoiceId = choiceId,
                        CandidateCardIds = candidates,
                        EvidenceSource = evidenceSource,
                    });
                }
                else if (candidates.Count >= existing.CandidateCardIds.Count)
                {
                    existing.CandidateCardIds = candidates;
                    existing.EvidenceSource = evidenceSource;
                }
                return true;
            }
        }

        public bool ObserveSelection(
            int choiceId, string selectedCardId, string evidenceSource)
        {
            lock (_sync)
            {
                if (choiceId < 0 || string.IsNullOrEmpty(selectedCardId)
                    || evidenceSource != "power_log") return false;
                var batch = _rawBatches.FirstOrDefault(item => item.ChoiceId == choiceId);
                if (batch == null || !batch.CandidateCardIds.Contains(selectedCardId)) return false;
                var existing = _rawSelections.FirstOrDefault(item => item.ChoiceId == choiceId);
                if (existing == null)
                {
                    _rawSelections.Add(new RawSelection
                    {
                        ChoiceId = choiceId,
                        SelectedCardId = selectedCardId,
                        EvidenceSource = evidenceSource,
                    });
                }
                return true;
            }
        }

        public void Advance(
            SecondHeroPowerDiscoverRule rule,
            IEnumerable<HeroPowerState> observedHeroPowers)
        {
            lock (_sync)
            {
                if (rule == null) return;
                string occurrenceId = rule.SourceId
                    + ":discover_second_hero_power_at_game_start@game_start";
                if (_expectations.Count == 0)
                    _expectations.Add(new SecondHeroPowerChoiceExpectation(
                        occurrenceId, rule.Count, rule.SourceId));

                var raw = _rawBatches.FirstOrDefault();
                if (raw == null) return;
                _observedBatches.Clear();
                _observedBatches.Add(new SecondHeroPowerChoiceBatchObservation(
                    occurrenceId, raw.ChoiceId, raw.CandidateCardIds, raw.EvidenceSource));
                var rawSelection = _rawSelections.FirstOrDefault(
                    item => item.ChoiceId == raw.ChoiceId);
                _selections.Clear();
                if (rawSelection != null)
                {
                    _selections.Add(new SecondHeroPowerChoiceSelection(
                        occurrenceId, rawSelection.ChoiceId,
                        rawSelection.SelectedCardId, rawSelection.EvidenceSource));
                }
                if (_selections.Count > 0)
                {
                    string selectedCardId = _selections[0].SelectedCardId;
                    var matchingEntity = (observedHeroPowers
                        ?? Enumerable.Empty<HeroPowerState>())
                        .FirstOrDefault(power => power != null && power.IsSecondary
                            && power.CardId == selectedCardId && power.EntityId > 0);
                    if (matchingEntity != null && !_entityObservations.Any(
                        observation => observation.EntityId == matchingEntity.EntityId))
                    {
                        _entityObservations.Add(new SecondHeroPowerEntityObservation(
                            occurrenceId, matchingEntity.CardId, matchingEntity.EntityId,
                            "hero_power_entity"));
                    }
                }
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _rawBatches.Clear();
                _rawSelections.Clear();
                _expectations.Clear();
                _observedBatches.Clear();
                _selections.Clear();
                _entityObservations.Clear();
            }
        }
    }
}

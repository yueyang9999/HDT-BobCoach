using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>追踪共享选牌发生项；选择、发放与到账必须由后续显式证据推进。</summary>
    public sealed class SharedCardVoteTracker
    {
        private readonly Dictionary<string, SharedCardVoteOccurrence> _occurrences =
            new Dictionary<string, SharedCardVoteOccurrence>();
        private readonly Dictionary<string, SharedCardVoteOccurrence> _pendingSelections =
            new Dictionary<string, SharedCardVoteOccurrence>();
        private readonly Dictionary<string, SharedCardVoteSelection> _selections =
            new Dictionary<string, SharedCardVoteSelection>();
        private readonly Dictionary<string, SharedCardGrantExpectation> _grantExpectations =
            new Dictionary<string, SharedCardGrantExpectation>();
        private readonly Dictionary<string, SharedCardGrantObservation> _localGrantObservations =
            new Dictionary<string, SharedCardGrantObservation>();
        private readonly HashSet<int> _seenLocalHandEntityIds = new HashSet<int>();
        private readonly List<SharedCardVoteOccurrence> _observedOccurrencesThisFrame =
            new List<SharedCardVoteOccurrence>();
        private readonly List<SharedCardGrantExpectation> _observedGrantExpectationsThisFrame =
            new List<SharedCardGrantExpectation>();
        private readonly List<SharedCardGrantObservation> _observedLocalGrantsThisFrame =
            new List<SharedCardGrantObservation>();
        private int _currentTurn = -1;
        private bool _hasLocalHandBaseline;

        public IList<SharedCardVoteOccurrence> ObservedOccurrencesThisFrame
        {
            get { return _observedOccurrencesThisFrame.AsReadOnly(); }
        }

        public IList<SharedCardVoteOccurrence> PendingSelections
        {
            get { return _pendingSelections.Values.OrderBy(value => value.Turn).ToList(); }
        }

        public IList<SharedCardVoteSelection> Selections
        {
            get { return _selections.Values.OrderBy(value => value.OccurrenceId).ToList(); }
        }

        public IList<SharedCardGrantExpectation> ObservedGrantExpectationsThisFrame
        {
            get { return _observedGrantExpectationsThisFrame.AsReadOnly(); }
        }

        public IList<SharedCardGrantExpectation> GrantExpectations
        {
            get { return _grantExpectations.Values.OrderBy(value => value.Turn).ToList(); }
        }

        public IList<SharedCardGrantObservation> ObservedLocalGrantsThisFrame
        {
            get { return _observedLocalGrantsThisFrame.AsReadOnly(); }
        }

        public IList<SharedCardGrantObservation> LocalGrantObservations
        {
            get { return _localGrantObservations.Values.OrderBy(value => value.OccurrenceId).ToList(); }
        }

        public void Reset()
        {
            _occurrences.Clear();
            _pendingSelections.Clear();
            _selections.Clear();
            _grantExpectations.Clear();
            _localGrantObservations.Clear();
            _seenLocalHandEntityIds.Clear();
            _observedOccurrencesThisFrame.Clear();
            _observedGrantExpectationsThisFrame.Clear();
            _observedLocalGrantsThisFrame.Clear();
            _currentTurn = -1;
            _hasLocalHandBaseline = false;
        }

        public void Advance(
            int turn, SharedCardVoteRule rule, IList<MinionData> localHand)
        {
            Advance(turn, rule, localHand, false);
        }

        public void Advance(
            int turn, SharedCardVoteRule rule, IList<MinionData> localHand,
            bool turnEnded)
        {
            _observedOccurrencesThisFrame.Clear();
            _observedGrantExpectationsThisFrame.Clear();
            _observedLocalGrantsThisFrame.Clear();
            if (turn <= 0 || rule == null) return;
            var hand = localHand ?? new MinionData[0];
            var newlyObservedHandCards = _hasLocalHandBaseline
                ? hand.Where(card => card != null && card.EntityId > 0
                    && !_seenLocalHandEntityIds.Contains(card.EntityId)).ToList()
                : new List<MinionData>();
            foreach (var card in hand.Where(card => card != null && card.EntityId > 0))
                _seenLocalHandEntityIds.Add(card.EntityId);
            _hasLocalHandBaseline = true;
            CompleteEarlierTurns(turn, rule);
            _currentTurn = turn;

            string occurrenceId = rule.SourceId
                + ":shared_card_vote_each_turn@" + turn;
            if (!_occurrences.ContainsKey(occurrenceId))
            {
                var occurrence = new SharedCardVoteOccurrence(
                    occurrenceId, turn, rule.SourceId);
                _occurrences[occurrenceId] = occurrence;
                _pendingSelections[occurrenceId] = occurrence;
                _observedOccurrencesThisFrame.Add(occurrence);
            }
            if (turnEnded) CompleteTurn(turn, rule);
            ObserveLocalGrants(turn, newlyObservedHandCards);
        }

        private void ObserveLocalGrants(int turn, IList<MinionData> newlyObservedHandCards)
        {
            foreach (var card in newlyObservedHandCards)
            {
                var expectation = _grantExpectations.Values
                    .Where(value => (value.Turn == turn || value.Turn == turn - 1)
                        && !string.IsNullOrEmpty(value.CardId)
                        && value.CardId == card.CardId
                        && !_localGrantObservations.ContainsKey(value.OccurrenceId))
                    .OrderByDescending(value => value.Turn)
                    .FirstOrDefault();
                if (expectation == null) continue;
                var observation = new SharedCardGrantObservation(
                    expectation.OccurrenceId, card.CardId, card.EntityId,
                    "local_player", "hand_entity");
                _localGrantObservations[expectation.OccurrenceId] = observation;
                _observedLocalGrantsThisFrame.Add(observation);
            }
        }

        private void CompleteEarlierTurns(int turn, SharedCardVoteRule rule)
        {
            foreach (var occurrence in _occurrences.Values
                .Where(value => value.Turn < turn)
                .OrderBy(value => value.Turn))
            {
                CompleteOccurrence(occurrence, rule);
            }
        }

        private void CompleteTurn(int turn, SharedCardVoteRule rule)
        {
            var occurrence = _occurrences.Values
                .FirstOrDefault(value => value.Turn == turn);
            if (occurrence != null) CompleteOccurrence(occurrence, rule);
        }

        private void CompleteOccurrence(
            SharedCardVoteOccurrence occurrence, SharedCardVoteRule rule)
        {
            if (_grantExpectations.ContainsKey(occurrence.OccurrenceId)) return;
            SharedCardVoteSelection selection;
            string cardId = _selections.TryGetValue(
                occurrence.OccurrenceId, out selection)
                ? selection.SelectedCardId : "";
            var expectation = new SharedCardGrantExpectation(
                occurrence.OccurrenceId, occurrence.Turn, cardId,
                rule.SharedScope, occurrence.SourceId);
            _grantExpectations[occurrence.OccurrenceId] = expectation;
            _observedGrantExpectationsThisFrame.Add(expectation);
        }

        public bool ObserveSelection(
            string occurrenceId, string selectedCardId,
            string selectingPlayerId, string evidenceSource)
        {
            SharedCardVoteOccurrence occurrence;
            if (string.IsNullOrEmpty(occurrenceId)
                || string.IsNullOrEmpty(selectedCardId)
                || !_pendingSelections.TryGetValue(occurrenceId, out occurrence)
                || occurrence.Turn != _currentTurn
                || !IsExplicitEvidence(evidenceSource))
                return false;

            _pendingSelections.Remove(occurrenceId);
            _selections[occurrenceId] = new SharedCardVoteSelection(
                occurrenceId, selectedCardId, selectingPlayerId, evidenceSource);
            SharedCardGrantExpectation existingGrant;
            if (_grantExpectations.TryGetValue(occurrenceId, out existingGrant)
                && string.IsNullOrEmpty(existingGrant.CardId))
            {
                _grantExpectations[occurrenceId] = new SharedCardGrantExpectation(
                    occurrenceId, existingGrant.Turn, selectedCardId,
                    existingGrant.SharedScope, existingGrant.SourceId);
            }
            return true;
        }

        private static bool IsExplicitEvidence(string evidenceSource)
        {
            return evidenceSource == "power_log"
                || evidenceSource == "choice_list"
                || evidenceSource == "entity_transition";
        }
    }
}

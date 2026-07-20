using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>按真实回合边界建立卡牌预期，并只用首次出现的真实手牌实体claim。</summary>
    public sealed class TurnStartCardGrantTracker
    {
        private readonly Dictionary<string, TurnStartCardGrantExpectation> _pending =
            new Dictionary<string, TurnStartCardGrantExpectation>();
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private readonly HashSet<string> _emitted = new HashSet<string>();
        private readonly HashSet<int> _seenHandEntityIds = new HashSet<int>();
        private readonly List<TurnStartCardGrantExpectation> _observedThisFrame =
            new List<TurnStartCardGrantExpectation>();
        private bool _hasHandBaseline;

        public IList<TurnStartCardGrantExpectation> ObservedThisFrame
        {
            get { return _observedThisFrame.ToList(); }
        }

        public IList<TurnStartCardGrantExpectation> Pending
        {
            get { return _pending.Values.OrderBy(value => value.OccurrenceId).ToList(); }
        }

        public IList<string> ClaimedOccurrences
        {
            get { return _claimed.OrderBy(value => value).ToList(); }
        }

        public void Reset()
        {
            _pending.Clear();
            _claimed.Clear();
            _emitted.Clear();
            _seenHandEntityIds.Clear();
            _observedThisFrame.Clear();
            _hasHandBaseline = false;
        }

        public void Advance(
            int turn, PortalInBottleRule rule, IList<MinionData> currentHand)
        {
            _observedThisFrame.Clear();
            var hand = (currentHand ?? new MinionData[0])
                .Where(card => card != null && card.EntityId > 0).ToList();
            var newCards = _hasHandBaseline
                ? hand.Where(card => !_seenHandEntityIds.Contains(card.EntityId)).ToList()
                : (turn == 1 ? hand : new List<MinionData>());

            if (turn > 0 && rule != null)
            {
                string occurrenceId = rule.SourceId
                    + ":portal_in_bottle_at_turn_start@" + turn;
                if (_emitted.Add(occurrenceId))
                {
                    var expectation = new TurnStartCardGrantExpectation(
                        occurrenceId, turn, rule.CardId, rule.Count, rule.SourceId);
                    _pending[occurrenceId] = expectation;
                    _observedThisFrame.Add(expectation);
                }
                TurnStartCardGrantExpectation pending;
                if (_pending.TryGetValue(occurrenceId, out pending)
                    && newCards.Count(card => card.CardId == pending.CardId) >= pending.Count)
                {
                    _pending.Remove(occurrenceId);
                    _claimed.Add(occurrenceId);
                }
            }

            foreach (var card in hand) _seenHandEntityIds.Add(card.EntityId);
            _hasHandBaseline = true;
        }
    }
}

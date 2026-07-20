using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>跨帧跟踪真实连续升本与尚待Power.log确认的奖品发现。</summary>
    public sealed class UpgradePrizeTracker
    {
        private int _previousTier;
        private readonly List<TavernUpgradeOccurrence> _observedThisFrame =
            new List<TavernUpgradeOccurrence>();
        private readonly List<PrizeDiscoverExpectation> _pending =
            new List<PrizeDiscoverExpectation>();
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private readonly HashSet<string> _observedTransitions = new HashSet<string>();

        public IList<TavernUpgradeOccurrence> ObservedThisFrame
        {
            get { return new ReadOnlyCollection<TavernUpgradeOccurrence>(_observedThisFrame); }
        }

        public IList<PrizeDiscoverExpectation> Pending
        {
            get { return new ReadOnlyCollection<PrizeDiscoverExpectation>(_pending); }
        }

        public ISet<string> ClaimedOccurrences { get { return _claimed; } }

        public void Reset()
        {
            _previousTier = 0;
            _observedThisFrame.Clear();
            _pending.Clear();
            _claimed.Clear();
            _observedTransitions.Clear();
        }

        public void Advance(
            int turn, int currentTier, bool isRecruitPhase, UpgradePrizeRule rule)
        {
            _observedThisFrame.Clear();
            int previousTier = _previousTier;
            if (currentTier > 0) _previousTier = currentTier;
            if (!isRecruitPhase || rule == null) return;

            var occurrence = UpgradePrizeEvaluator.CreateOccurrence(
                rule, turn, previousTier, currentTier, "observed_transition");
            if (occurrence == null || _claimed.Contains(occurrence.OccurrenceId)
                || _pending.Any(item => item.OccurrenceId == occurrence.OccurrenceId)) return;
            string transitionId = previousTier + ">" + currentTier;
            if (_observedTransitions.Contains(transitionId)) return;
            var expectation = UpgradePrizeEvaluator.CreateExpectation(rule, occurrence);
            if (expectation == null) return;
            _observedTransitions.Add(transitionId);
            _observedThisFrame.Add(occurrence);
            _pending.Add(expectation);
        }

        public bool MarkOldestPendingClaimed()
        {
            if (_pending.Count == 0) return false;
            var expectation = _pending[0];
            _pending.RemoveAt(0);
            return _claimed.Add(expectation.OccurrenceId);
        }
    }
}

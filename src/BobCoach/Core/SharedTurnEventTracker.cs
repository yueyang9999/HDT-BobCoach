using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>跟踪共享回合事件；没有外部结果证据时只保留pending。</summary>
    public sealed class SharedTurnEventTracker
    {
        private readonly Dictionary<string, SharedTurnEventExpectation> _pending =
            new Dictionary<string, SharedTurnEventExpectation>();
        private readonly HashSet<string> _emitted = new HashSet<string>();
        private readonly Dictionary<string, SharedTurnEventOutcome> _outcomes =
            new Dictionary<string, SharedTurnEventOutcome>();
        private readonly List<SharedTurnEventExpectation> _observedThisFrame =
            new List<SharedTurnEventExpectation>();

        public IList<SharedTurnEventExpectation> ObservedThisFrame
        {
            get { return _observedThisFrame.ToList(); }
        }

        public IList<SharedTurnEventExpectation> Pending
        {
            get { return _pending.Values.OrderBy(value => value.OccurrenceId).ToList(); }
        }

        public IList<SharedTurnEventOutcome> Outcomes
        {
            get { return _outcomes.Values.OrderBy(value => value.OccurrenceId).ToList(); }
        }

        public void Reset()
        {
            _pending.Clear();
            _emitted.Clear();
            _outcomes.Clear();
            _observedThisFrame.Clear();
        }

        public void Advance(int turn, SharedYoggWheelRule rule)
        {
            _observedThisFrame.Clear();
            if (turn <= 0 || rule == null) return;
            string occurrenceId = rule.SourceId
                + ":shared_yogg_wheel_at_turn_start@" + turn;
            if (!_emitted.Add(occurrenceId)) return;
            var expectation = new SharedTurnEventExpectation(
                occurrenceId, turn, "yogg_wheel",
                rule.SharedScope, rule.SourceId);
            _pending[occurrenceId] = expectation;
            _observedThisFrame.Add(expectation);
        }

        public bool ObserveOutcome(
            string occurrenceId, string outcomeId, string evidenceSource)
        {
            if (string.IsNullOrEmpty(occurrenceId) || string.IsNullOrEmpty(outcomeId)
                || (evidenceSource != "power_log"
                    && evidenceSource != "entity_transition")) return false;
            SharedTurnEventExpectation expectation;
            if (!_pending.TryGetValue(occurrenceId, out expectation)) return false;
            _pending.Remove(occurrenceId);
            _outcomes[occurrenceId] = new SharedTurnEventOutcome(
                occurrenceId, outcomeId, evidenceSource);
            return true;
        }
    }
}

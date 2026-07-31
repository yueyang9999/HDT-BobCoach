using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class DecisionLifecycleTracker
    {
        private readonly string _gameId;
        private readonly DecisionEventRecorder _recorder;
        private int _nextOrdinal = 1;
        private DecisionEventRecord _current;
        private bool _currentClosed;

        public DecisionLifecycleTracker(string gameId, DecisionEventRecorder recorder)
        {
            if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("gameId");
            if (recorder == null) throw new ArgumentNullException("recorder");
            _gameId = gameId;
            _recorder = recorder;
        }

        public DecisionEventRecord BeginOrReuse(GameState state,
            IEnumerable<ActionDescriptor> candidates)
        {
            var prospective = DecisionEventRecord.Create(_gameId + ":" + _nextOrdinal,
                _gameId, state, candidates);
            if (_current != null && !_currentClosed
                && string.Equals(_current.StateFingerprint, prospective.StateFingerprint,
                    StringComparison.Ordinal)
                && string.Equals(_current.CandidateFingerprint, prospective.CandidateFingerprint,
                    StringComparison.Ordinal))
                return _current;

            _recorder.RecordCreated(prospective);
            _current = prospective;
            _currentClosed = false;
            _nextOrdinal++;
            return _current;
        }

        public void AttachObservedActions(IEnumerable<string> actionKeys, string observedAtUtc)
        {
            if (_current == null || _currentClosed) return;
            var actions = (actionKeys ?? Enumerable.Empty<string>()).ToList();
            if (actions.Count == 0) return;

            for (var index = 0; index < actions.Count; index++)
                _recorder.AttachObservedAction(_current.DecisionId, actions[index], observedAtUtc, index);
            _currentClosed = true;
        }

        public void Reset()
        {
            _nextOrdinal = 1;
            _current = null;
            _currentClosed = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public static class StartResourceExpectationEvaluator
    {
        public static void RecordObservedState(
            GameState state, IEnumerable<StartResourceExpectation> expectations)
        {
            if (state == null || expectations == null) return;
            foreach (var expectation in expectations.Where(value => value != null))
            {
                var status = Evaluate(state, expectation);
                if (status == StartResourceVerificationStatus.Observed)
                    state.ObservedStartResourceExpectations.Add(expectation.Id);
                else if (status == StartResourceVerificationStatus.Mismatched)
                    state.MismatchedStartResourceExpectations.Add(expectation.Id);
            }
        }

        public static StartResourceVerificationStatus Evaluate(
            GameState state, StartResourceExpectation expectation)
        {
            if (state == null || expectation == null)
                return StartResourceVerificationStatus.Pending;
            if (state.MismatchedStartResourceExpectations != null
                && state.MismatchedStartResourceExpectations.Contains(expectation.Id))
                return StartResourceVerificationStatus.Mismatched;
            if (state.ObservedStartResourceExpectations != null
                && state.ObservedStartResourceExpectations.Contains(expectation.Id))
                return StartResourceVerificationStatus.Observed;

            IEnumerable<MinionData> observed = expectation.Kind == "board_minion"
                ? state.BoardMinions : state.HandMinions;
            var sameCard = (observed ?? Enumerable.Empty<MinionData>())
                .Where(card => card != null
                    && string.Equals(card.CardId, expectation.CardId, StringComparison.Ordinal))
                .ToList();
            if (sameCard.Count(card => card.Golden == expectation.Golden) >= expectation.Count)
                return StartResourceVerificationStatus.Observed;
            if (sameCard.Count >= expectation.Count)
                return StartResourceVerificationStatus.Mismatched;
            return StartResourceVerificationStatus.Pending;
        }
    }
}

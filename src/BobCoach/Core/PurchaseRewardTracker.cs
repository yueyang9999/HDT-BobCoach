using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class PurchaseRewardTracker
    {
        private readonly Dictionary<string, PurchaseRewardExpectation> _pending =
            new Dictionary<string, PurchaseRewardExpectation>();
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private readonly HashSet<int> _consumedTurns = new HashSet<int>();

        public IList<PurchaseRewardExpectation> Pending
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
            _consumedTurns.Clear();
        }

        public void Advance(
            int turn,
            FirstPurchaseExtraCopyRule rule,
            IList<ObservedPurchase> observedPurchases,
            IList<MinionData> newlyObservedCards)
        {
            var newCards = newlyObservedCards ?? new MinionData[0];
            var successful = (observedPurchases ?? new ObservedPurchase[0])
                .Where(purchase => purchase != null && purchase.Succeeded
                    && purchase.Turn == turn)
                .ToList();
            var purchasedEntityIds = new HashSet<int>(successful
                .Where(purchase => purchase.EntityId > 0)
                .Select(purchase => purchase.EntityId));
            ClaimObservedCopies(newCards, purchasedEntityIds);

            if (rule == null || _consumedTurns.Contains(turn)) return;
            if (successful.Count == 0) return;

            _consumedTurns.Add(turn);
            if (successful.Count != 1 || successful[0].Source != "tavern_shop") return;

            var firstPurchase = successful[0];
            string occurrenceId = rule.SourceId + ":first_purchase_extra_copy@" + turn;
            _pending[occurrenceId] = new PurchaseRewardExpectation(
                occurrenceId, firstPurchase.CardId, rule.ExtraCopyCount,
                firstPurchase.IsSpell, firstPurchase.Golden, rule.SourceId);
            ClaimObservedCopies(newCards, purchasedEntityIds);
        }

        private void ClaimObservedCopies(
            IEnumerable<MinionData> newCards, HashSet<int> purchasedEntityIds)
        {
            foreach (var pending in _pending.Values.ToList())
            {
                int matchingCount = newCards.Count(card => card != null
                    && card.EntityId > 0
                    && (purchasedEntityIds == null
                        || !purchasedEntityIds.Contains(card.EntityId))
                    && card.CardId == pending.CardId
                    && card.IsSpell == pending.IsSpell
                    && card.Golden == pending.Golden);
                if (matchingCount < pending.Count) continue;
                _pending.Remove(pending.OccurrenceId);
                _claimed.Add(pending.OccurrenceId);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    internal interface ICardEffectBaselineProvider
    {
        bool TryGet(out IReadOnlyList<double> baseline);
    }

    internal sealed class CardEffectBaselineProvider : ICardEffectBaselineProvider
    {
        private static readonly IReadOnlyList<double> Empty
            = Array.AsReadOnly(new double[0]);

        private readonly ILocalCardPoolMembershipSource _members;
        private readonly ICardEffectFactSource _facts;
        private readonly object _gate = new object();
        private bool _attempted;
        private bool _success;
        private IReadOnlyList<double> _baseline = Empty;

        public CardEffectBaselineProvider(
            ILocalCardPoolMembershipSource members,
            ICardEffectFactSource facts)
        {
            _members = members ?? throw new ArgumentNullException(nameof(members));
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        }

        public bool TryGet(out IReadOnlyList<double> baseline)
        {
            lock (_gate)
            {
                if (!_attempted)
                {
                    _attempted = true;
                    try
                    {
                        double[] candidate;
                        _success = TryBuild(out candidate);
                        _baseline = _success
                            ? Array.AsReadOnly(candidate)
                            : Empty;
                    }
                    catch
                    {
                        _success = false;
                        _baseline = Empty;
                    }
                }
                baseline = _baseline;
                return _success;
            }
        }

        private bool TryBuild(out double[] baseline)
        {
            baseline = new double[0];
            string startBuild = _members.ReadSnapshotId();
            if (string.IsNullOrEmpty(startBuild)) return false;

            IReadOnlyCollection<LocalShopPoolMember> shop = _members.ReadShopMembers();
            IReadOnlyCollection<LocalBuddyPoolMember> buddies = _members.ReadBuddyMembers();
            IReadOnlyCollection<LocalTimewarpPoolMember> timewarps = _members.ReadTimewarpMembers();
            if (shop == null || buddies == null || timewarps == null) return false;

            var expectedTypes = new Dictionary<string, CardEffectCardType>(StringComparer.Ordinal);
            if (!AddShopMembers(shop, expectedTypes)
                || !AddMinionMembers(buddies.Select(item => item.CardId), expectedTypes)
                || !AddMinionMembers(timewarps.Select(item => item.CardId), expectedTypes)
                || expectedTypes.Count == 0)
                return false;

            var byTier = Enumerable.Range(0, 8)
                .Select(_ => new List<int>()).ToArray();
            foreach (var pair in expectedTypes.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                CardEffectFact fact;
                if (!_facts.TryGet(pair.Key, out fact)
                    || fact == null
                    || !string.Equals(fact.CardId, pair.Key, StringComparison.Ordinal)
                    || fact.CardType != pair.Value
                    || fact.Tier < 1 || fact.Tier > 7)
                    return false;
                if (fact.CardType == CardEffectCardType.Minion)
                {
                    if (fact.Attack < 0 || fact.Health < 0) return false;
                    byTier[fact.Tier].Add(fact.Attack + fact.Health);
                }
            }

            var candidate = new double[8];
            double previous = 0;
            for (int tier = 1; tier <= 7; tier++)
            {
                if (byTier[tier].Count == 0) return false;
                byTier[tier].Sort();
                int middle = byTier[tier].Count / 2;
                double raw = byTier[tier].Count % 2 == 1
                    ? byTier[tier][middle]
                    : (byTier[tier][middle - 1] + byTier[tier][middle]) / 2.0;
                candidate[tier] = Math.Max(raw, previous);
                previous = candidate[tier];
            }

            string endBuild = _members.ReadSnapshotId();
            if (!string.Equals(startBuild, endBuild, StringComparison.Ordinal)) return false;
            baseline = candidate;
            return true;
        }

        private static bool AddShopMembers(
            IEnumerable<LocalShopPoolMember> members,
            IDictionary<string, CardEffectCardType> expectedTypes)
        {
            var local = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (string.IsNullOrEmpty(member.CardId)
                    || !local.Add(member.CardId)
                    || expectedTypes.ContainsKey(member.CardId))
                    return false;
                expectedTypes[member.CardId] = member.IsSpell
                    ? CardEffectCardType.TavernSpell
                    : CardEffectCardType.Minion;
            }
            return true;
        }

        private static bool AddMinionMembers(
            IEnumerable<string> cardIds,
            IDictionary<string, CardEffectCardType> expectedTypes)
        {
            var local = new HashSet<string>(StringComparer.Ordinal);
            foreach (string cardId in cardIds)
            {
                if (string.IsNullOrEmpty(cardId)
                    || !local.Add(cardId)
                    || expectedTypes.ContainsKey(cardId))
                    return false;
                expectedTypes[cardId] = CardEffectCardType.Minion;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardPoolMembershipSource : ILocalCardPoolMembershipSource
    {
        public string ReadSnapshotId()
        {
            return HearthDb.Cards.Build ?? "";
        }

        public IReadOnlyCollection<LocalShopPoolMember> ReadShopMembers()
        {
            var result = new List<LocalShopPoolMember>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in HearthDb.Cards.BaconPoolMinions)
            {
                var card = pair.Value;
                if (card == null
                    || string.IsNullOrEmpty(pair.Key)
                    || !string.Equals(card.Id, pair.Key, StringComparison.Ordinal)
                    || card.Type != HearthDb.Enums.CardType.MINION
                    || HearthDb.Cards.TripleToNormalCardIds.ContainsKey(pair.Key)
                    || !seen.Add(pair.Key))
                    throw new InvalidOperationException("invalid local shop minion membership");
                result.Add(new LocalShopPoolMember(pair.Key, false));
            }

            foreach (var pair in HearthDb.Cards.All)
            {
                var card = pair.Value;
                if (card == null
                    || card.Type != HearthDb.Enums.CardType.BATTLEGROUND_SPELL
                    || card.Entity.GetTag(HearthDb.Enums.GameTag.IS_BACON_POOL_SPELL) <= 0)
                    continue;
                if (string.IsNullOrEmpty(pair.Key)
                    || !string.Equals(card.Id, pair.Key, StringComparison.Ordinal)
                    || !seen.Add(pair.Key))
                    throw new InvalidOperationException("invalid local shop spell membership");
                result.Add(new LocalShopPoolMember(pair.Key, true));
            }
            return result.AsReadOnly();
        }

        public IReadOnlyCollection<LocalBuddyPoolMember> ReadBuddyMembers()
        {
            var result = new List<LocalBuddyPoolMember>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var seenGolden = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in HearthDb.Cards.All)
            {
                var card = pair.Value;
                if (card == null
                    || card.Type != HearthDb.Enums.CardType.MINION
                    || card.Entity.GetTag(HearthDb.Enums.GameTag.BACON_BUDDY) <= 0)
                    continue;
                if (HearthDb.Cards.TripleToNormalCardIds.ContainsKey(pair.Key)) continue;

                string goldenCardId;
                HearthDb.Card goldenCard;
                int copies = InitialPoolCopiesForTier(card.TechLevel);
                if (string.IsNullOrEmpty(pair.Key)
                    || !string.Equals(card.Id, pair.Key, StringComparison.Ordinal)
                    || !HearthDb.Cards.NormalToTripleCardIds.TryGetValue(pair.Key, out goldenCardId)
                    || string.IsNullOrEmpty(goldenCardId)
                    || !HearthDb.Cards.All.TryGetValue(goldenCardId, out goldenCard)
                    || goldenCard == null
                    || !string.Equals(goldenCard.Id, goldenCardId, StringComparison.Ordinal)
                    || goldenCard.Type != HearthDb.Enums.CardType.MINION
                    || card.TechLevel < 1 || card.TechLevel > 6
                    || copies <= 0
                    || !seen.Add(pair.Key)
                    || !seenGolden.Add(goldenCardId))
                    throw new InvalidOperationException("invalid local buddy membership");

                result.Add(new LocalBuddyPoolMember(
                    pair.Key, goldenCardId, card.TechLevel, copies));
            }
            return result.AsReadOnly();
        }

        public IReadOnlyCollection<LocalTimewarpPoolMember> ReadTimewarpMembers()
        {
            var result = new List<LocalTimewarpPoolMember>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in HearthDb.Cards.All)
            {
                var card = pair.Value;
                if (card == null
                    || card.Type != HearthDb.Enums.CardType.MINION
                    || card.Entity.GetTag(HearthDb.Enums.GameTag.BACON_TIMEWARPED) <= 0)
                    continue;
                if (HearthDb.Cards.TripleToNormalCardIds.ContainsKey(pair.Key)) continue;

                string goldenCardId;
                HearthDb.Card goldenCard;
                string kind = card.TechLevel == 3 ? "lesser"
                    : card.TechLevel == 5 ? "greater" : "";
                if (string.IsNullOrEmpty(pair.Key)
                    || !string.Equals(card.Id, pair.Key, StringComparison.Ordinal)
                    || string.IsNullOrEmpty(kind)
                    || !HearthDb.Cards.NormalToTripleCardIds.TryGetValue(pair.Key, out goldenCardId)
                    || string.IsNullOrEmpty(goldenCardId)
                    || !HearthDb.Cards.All.TryGetValue(goldenCardId, out goldenCard)
                    || goldenCard == null
                    || !string.Equals(goldenCard.Id, goldenCardId, StringComparison.Ordinal)
                    || goldenCard.Type != HearthDb.Enums.CardType.MINION
                    || !seen.Add(pair.Key))
                    throw new InvalidOperationException("invalid local timewarp membership");

                result.Add(new LocalTimewarpPoolMember(pair.Key, kind, card.TechLevel));
            }
            return result.AsReadOnly();
        }

        private static int InitialPoolCopiesForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 16;
                case 2: return 15;
                case 3: return 13;
                case 4: return 11;
                case 5: return 9;
                case 6: return 7;
                default: return 0;
            }
        }
    }
}

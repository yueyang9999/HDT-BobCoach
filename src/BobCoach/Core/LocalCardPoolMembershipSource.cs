using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal interface ILocalCardPoolMembershipSource
    {
        string ReadSnapshotId();
        IReadOnlyCollection<LocalShopPoolMember> ReadShopMembers();
        IReadOnlyCollection<LocalBuddyPoolMember> ReadBuddyMembers();
        IReadOnlyCollection<LocalTimewarpPoolMember> ReadTimewarpMembers();
    }

    internal struct LocalShopPoolMember
    {
        public string CardId { get; private set; }
        public bool IsSpell { get; private set; }

        public LocalShopPoolMember(string cardId, bool isSpell)
        {
            CardId = cardId ?? "";
            IsSpell = isSpell;
        }
    }

    internal struct LocalBuddyPoolMember
    {
        public string CardId { get; private set; }
        public string GoldenCardId { get; private set; }
        public int Tier { get; private set; }
        public int InitialPoolCopies { get; private set; }

        public LocalBuddyPoolMember(
            string cardId, string goldenCardId, int tier, int initialPoolCopies)
        {
            CardId = cardId ?? "";
            GoldenCardId = goldenCardId ?? "";
            Tier = tier;
            InitialPoolCopies = initialPoolCopies;
        }
    }

    internal struct LocalTimewarpPoolMember
    {
        public string CardId { get; private set; }
        public string Kind { get; private set; }
        public int Tier { get; private set; }

        public LocalTimewarpPoolMember(string cardId, string kind, int tier)
        {
            CardId = cardId ?? "";
            Kind = kind ?? "";
            Tier = tier;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class LocalCardPoolMembershipSnapshot
    {
        public string SnapshotId { get; private set; }
        public IReadOnlyCollection<LocalShopPoolMember> ShopMembers { get; private set; }
        public IReadOnlyCollection<LocalBuddyPoolMember> BuddyMembers { get; private set; }
        public IReadOnlyCollection<LocalTimewarpPoolMember> TimewarpMembers { get; private set; }

        private LocalCardPoolMembershipSnapshot(
            string snapshotId,
            LocalShopPoolMember[] shopMembers,
            LocalBuddyPoolMember[] buddyMembers,
            LocalTimewarpPoolMember[] timewarpMembers)
        {
            SnapshotId = snapshotId;
            ShopMembers = Array.AsReadOnly(shopMembers);
            BuddyMembers = Array.AsReadOnly(buddyMembers);
            TimewarpMembers = Array.AsReadOnly(timewarpMembers);
        }

        public static bool TryCreate(
            ILocalCardPoolMembershipSource source,
            out LocalCardPoolMembershipSnapshot snapshot)
        {
            snapshot = null;
            if (source == null) return false;

            try
            {
                string snapshotId = source.ReadSnapshotId();
                var shop = source.ReadShopMembers();
                var buddies = source.ReadBuddyMembers();
                var timewarps = source.ReadTimewarpMembers();
                if (string.IsNullOrWhiteSpace(snapshotId)
                    || shop == null || shop.Count == 0
                    || buddies == null || buddies.Count == 0
                    || timewarps == null || timewarps.Count == 0)
                    return false;

                var memberIds = new HashSet<string>(StringComparer.Ordinal);
                var goldenIds = new HashSet<string>(StringComparer.Ordinal);
                var shopCopy = new List<LocalShopPoolMember>();
                foreach (var member in shop)
                {
                    if (string.IsNullOrEmpty(member.CardId)
                        || !memberIds.Add(member.CardId))
                        return false;
                    shopCopy.Add(member);
                }

                var buddyCopy = new List<LocalBuddyPoolMember>();
                foreach (var member in buddies)
                {
                    if (string.IsNullOrEmpty(member.CardId)
                        || string.IsNullOrEmpty(member.GoldenCardId)
                        || string.Equals(member.CardId, member.GoldenCardId, StringComparison.Ordinal)
                        || member.Tier < 1 || member.Tier > 7
                        || member.InitialPoolCopies <= 0
                        || !memberIds.Add(member.CardId)
                        || !goldenIds.Add(member.GoldenCardId))
                        return false;
                    buddyCopy.Add(member);
                }

                var timewarpCopy = new List<LocalTimewarpPoolMember>();
                foreach (var member in timewarps)
                {
                    if (string.IsNullOrEmpty(member.CardId)
                        || (member.Kind != "lesser" && member.Kind != "greater")
                        || member.Tier < 1 || member.Tier > 7
                        || !memberIds.Add(member.CardId))
                        return false;
                    timewarpCopy.Add(member);
                }

                if (goldenIds.Overlaps(memberIds)) return false;
                snapshot = new LocalCardPoolMembershipSnapshot(
                    snapshotId, shopCopy.ToArray(), buddyCopy.ToArray(), timewarpCopy.ToArray());
                return true;
            }
            catch
            {
                snapshot = null;
                return false;
            }
        }
    }
}

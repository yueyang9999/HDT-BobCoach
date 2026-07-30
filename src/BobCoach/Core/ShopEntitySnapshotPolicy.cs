using HearthDb.Enums;

namespace BobCoach.Engine
{
    internal static class ShopEntitySnapshotPolicy
    {
        public static bool IsChangeDetectionCandidate(CardType cardType)
        {
            return cardType == CardType.MINION
                || cardType == CardType.SPELL
                || cardType == CardType.BATTLEGROUND_SPELL;
        }

        public static bool IsStateHashCandidate(CardType cardType, int health)
        {
            if (cardType == CardType.SPELL || cardType == CardType.BATTLEGROUND_SPELL)
                return true;

            return cardType == CardType.MINION && health > 0;
        }
    }
}

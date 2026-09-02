using System;

namespace BobCoach.Engine
{
    /// <summary>战棋英雄CardId的保守身份门禁；实体类型仍必须由HDT IsHero证明。</summary>
    public static class BattlegroundsHeroIdentity
    {
        public static bool IsEligibleHeroCardId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            if (cardId.IndexOf("_PH", StringComparison.Ordinal) >= 0)
                return false;
            if (cardId.EndsWith("p", StringComparison.Ordinal)) return false;
            if (cardId.StartsWith("TB_BaconShop_HERO", StringComparison.Ordinal))
                return true;
            return cardId.StartsWith("BG", StringComparison.Ordinal)
                && cardId.IndexOf("_HERO_", StringComparison.Ordinal) >= 0;
        }

        public static string NormalizeHeroCardId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return cardId ?? "";
            int skinIndex = cardId.IndexOf("_SKIN_", StringComparison.Ordinal);
            if (skinIndex > 0)
                return cardId.Substring(0, skinIndex);
            return cardId;
        }
    }
}

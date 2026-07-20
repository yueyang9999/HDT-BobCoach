using System;

namespace BobCoach.Engine
{
    /// <summary>Power.log候选CardId的保守英雄技能身份门禁。</summary>
    public static class HeroPowerChoiceIdentity
    {
        public static bool IsEligible(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            if (cardId.IndexOf("_SKIN", StringComparison.Ordinal) >= 0
                || cardId.EndsWith("_PH", StringComparison.Ordinal)
                || cardId.IndexOf("_PH_", StringComparison.Ordinal) >= 0)
                return false;
            if (cardId.StartsWith("TB_BaconShop_HP_", StringComparison.Ordinal)
                || cardId.IndexOf("BACON_HERO_POWER", StringComparison.Ordinal) >= 0
                || cardId.IndexOf("_HERO_POWER", StringComparison.Ordinal) >= 0
                || cardId.IndexOf("_HEROPOWER_", StringComparison.Ordinal) >= 0)
                return true;
            return cardId.StartsWith("BG", StringComparison.Ordinal)
                && cardId.IndexOf("_HERO_", StringComparison.Ordinal) >= 0
                && cardId.EndsWith("p", StringComparison.Ordinal);
        }
    }
}

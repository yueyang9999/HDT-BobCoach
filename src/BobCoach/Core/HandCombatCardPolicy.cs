using System;

namespace BobCoach.Engine
{
    /// <summary>
    /// 手牌开战效果卡：战斗开始时从手牌触发，通常应保留在手牌而不是上场。
    /// 规则来源为当前版本卡牌文本和战斗模拟器中的手牌 START_OF_COMBAT 分支。
    /// </summary>
    public static class HandCombatCardPolicy
    {
        private static readonly System.Collections.Generic.HashSet<string> HandCombatCardIds
            = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "BG32_330",
            };

        public static bool ShouldKeepInHand(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            if (HandCombatCardIds.Contains(cardId)) return true;

            // 兼容金卡、临时复制和附魔派生 ID，避免“由效果抓到手牌”时绕过规则。
            string candidate = cardId;
            int goldenIndex = candidate.IndexOf("_G", StringComparison.Ordinal);
            if (goldenIndex > 0)
                candidate = candidate.Substring(0, goldenIndex);
            if (candidate.EndsWith("t", StringComparison.Ordinal))
                candidate = candidate.Substring(0, candidate.Length - 1);
            return HandCombatCardIds.Contains(candidate);
        }
    }
}

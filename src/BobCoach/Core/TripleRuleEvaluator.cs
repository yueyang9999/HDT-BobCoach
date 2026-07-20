using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>合金阈值与奖励资格的唯一规则入口。</summary>
    public static class TripleRuleEvaluator
    {
        public static int CountOwnedCopies(GameState state, string cardId)
        {
            if (state == null || string.IsNullOrEmpty(cardId)) return 0;
            return CountCopies(state.BoardMinions, cardId)
                + CountCopies(state.HandMinions, cardId);
        }

        public static bool CompletesGolden(
            GameState state, string shopCardId, EffectiveGameRules rules)
        {
            if (string.IsNullOrEmpty(shopCardId)) return false;
            var effectiveRules = rules ?? EffectiveGameRules.Default;
            int requirement = Math.Max(2, effectiveRules.GoldenCopyRequirement);
            return CountOwnedCopies(state, shopCardId) >= requirement - 1;
        }

        public static bool GrantsStandardDiscover(EffectiveGameRules rules)
        {
            var effectiveRules = rules ?? EffectiveGameRules.Default;
            return string.Equals(effectiveRules.GoldenRewardOverride,
                "standard_discover", StringComparison.Ordinal);
        }

        private static int CountCopies(IEnumerable<MinionData> cards, string cardId)
        {
            if (cards == null) return 0;
            int count = 0;
            foreach (var card in cards)
            {
                if (card != null && !card.Golden && !card.IsSpell
                    && string.Equals(card.CardId, cardId, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }
    }
}

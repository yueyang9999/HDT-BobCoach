using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class FreeRefreshObservation
    {
        public string CardId { get; set; }
        public int ControllerId { get; set; }
        public int TaggedCount { get; set; }
    }

    /// <summary>有效规则的费用与合法性公共入口。</summary>
    public static class GameRuleEvaluator
    {
        public static int ResolveFreeRefreshCount(
            IEnumerable<FreeRefreshObservation> observations, int localControllerId)
        {
            if (observations == null || localControllerId <= 0) return 0;

            int count = 0;
            foreach (var observation in observations)
            {
                if (observation == null
                    || observation.ControllerId != localControllerId)
                    continue;
                if (observation.CardId != "Bacon_Free_Refresh_Player_Ench"
                    && observation.CardId != "TB_BaconShop_8p_Reroll_Button")
                    continue;
                count = Math.Max(count, observation.TaggedCount);
            }
            return Math.Min(Math.Max(0, count), 10);
        }

        public static int GetPurchaseCost(
            GameState state, MinionData card, string heroCardId, EffectiveGameRules rules)
        {
            if (state == null || card == null) return int.MaxValue;
            if (card.IsSpell)
            {
                int baseCost = card.Cost >= 0 ? card.Cost : int.MaxValue;
                var trinkets = state.ActiveTrinketContext
                    ?? (rules != null ? rules.ActiveTrinkets : ActiveTrinketContext.Empty);
                return trinkets.AdjustPurchaseCost(card, baseCost);
            }
            if (card.Tier <= 0) return int.MaxValue;

            rules = rules ?? EffectiveGameRules.Default;
            if (!state.FirstMinionPurchaseUsedThisTurn && rules.FirstMinionPurchaseCost.HasValue)
                return Math.Max(0, rules.FirstMinionPurchaseCost.Value);
            if (rules.MinionPurchaseCostOverride.HasValue)
                return Math.Max(0, rules.MinionPurchaseCostOverride.Value);
            return IsMillhouse(heroCardId) ? 2 : 3;
        }

        public static int? GetUpgradeCost(GameState state, EffectiveGameRules rules)
        {
            if (state == null) return null;
            rules = rules ?? EffectiveGameRules.Default;
            if (state.TavernTier >= rules.MaxTavernTier) return null;
            if (state.TavernUpgradeCost >= 0) return state.TavernUpgradeCost;
            if (state.TavernTier >= 6) return null;
            int fallbackCost = ActionEnumerator.GetUpgradeCost(
                state.TavernTier, state.Turn, state.LastUpgradeTurn);
            ActiveTrinketContext trinkets = state.ActiveTrinketContext;
            if (trinkets == null
                || (trinkets.ResolvedCardIds.Count == 0 && trinkets.UnknownCardIds.Count == 0))
                trinkets = rules.ActiveTrinkets;
            int delta = trinkets != null ? trinkets.UpgradeCostDelta : rules.UpgradeCostDelta;
            return Math.Max(0, fallbackCost + delta);
        }

        public static int GetRefreshCost(
            GameState state, string heroCardId, EffectiveGameRules rules)
        {
            if (state != null && state.FreeRefreshCount > 0) return 0;
            rules = rules ?? EffectiveGameRules.Default;
            if (rules.RefreshCostOverride.HasValue)
                return Math.Max(0, rules.RefreshCostOverride.Value);
            return IsMillhouse(heroCardId) ? 2 : 1;
        }

        private static bool IsMillhouse(string heroCardId)
        {
            return !string.IsNullOrEmpty(heroCardId) && heroCardId.Contains("HERO_49");
        }
    }
}

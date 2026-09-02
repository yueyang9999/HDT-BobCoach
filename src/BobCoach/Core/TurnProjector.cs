using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class CrossTurnPlan
    {
        public GameAction FirstAction;
        public GameAction NextTurnAction;
        public double CombinedScore;
        public GameState StateAfterFirst;
        public GameState NextTurnState;
    }

    /// <summary>
    /// 跨回合状态投影器。用于策略规划中估算“本回合动作执行后，下一回合金币/手牌/场面/刷新机会”。
    /// 它只做有限规则投影，不模拟战斗伤害。
    /// </summary>
    public static class TurnProjector
    {
        public static GameState Advance(GameState state)
        {
            if (state == null || !state.GameActive) return null;

            var next = new Simulator().Simulate(state, new GameAction
            {
                Type = ActionType.FreezeShop,
            });
            if (next == null) return null;

            next.Turn++;
            int baseMaxGold = Math.Min(10, 2 + next.Turn);
            int carriedGoldCapBonus = Math.Max(
                0, state.MaxGold - Math.Min(10, 2 + state.Turn));
            next.MaxGold = Math.Min(20, baseMaxGold + carriedGoldCapBonus);
            next.Gold = next.MaxGold;
            next.LiveGold = next.Gold;
            next.FreeRefreshCount = state.FreeRefreshCount;
            next.FirstPurchaseUsedThisTurn = false;
            next.FirstMinionPurchaseUsedThisTurn = false;
            next.HeroPowerExhausted = false;
            next.ActivatedThisTurn = new HashSet<int>();
            next.RecentlyBoughtEntityIds = new HashSet<int>();
            next.DiscoverOptions = new List<TrinketOption>();
            next.TrinketOffer = new List<TrinketOption>();
            next.HeroOptions = new List<HeroOption>();
            next.Phase = "shop";
            next.NormalRecruitAvailable = true;

            if (next.HeroPowers != null)
            {
                foreach (var power in next.HeroPowers)
                    if (power != null) power.Exhausted = false;
            }

            int nextTurnGoldBonus = GetNextTurnGoldBonus(next);
            if (nextTurnGoldBonus > 0)
            {
                next.MaxGold = Math.Min(20, next.MaxGold + nextTurnGoldBonus);
                next.Gold = next.MaxGold;
                next.LiveGold = next.Gold;
            }

            if (!next.FrozenShop)
                next.ShopMinions = new List<MinionData>();

            next.TavernUpgradeCost = ActionEnumerator.GetUpgradeCost(
                next.TavernTier, next.Turn, next.LastUpgradeTurn);
            return next;
        }

        private static int GetNextTurnGoldBonus(GameState state)
        {
            if (state == null) return 0;
            int bonus = 0;
            foreach (var minion in state.BoardMinions)
            {
                if (minion == null) continue;
                bonus += IsNextTurnGoldCard(minion.CardId) ? 1 : 0;
            }
            foreach (var card in state.HandMinions)
            {
                if (card == null) continue;
                bonus += IsNextTurnGoldCard(card.CardId) ? 1 : 0;
            }
            return Math.Min(3, bonus);
        }

        private static bool IsNextTurnGoldCard(string cardId)
        {
            return cardId == "BG26_135"
                || cardId == "BG26_135_G"
                || cardId == "BG34_Giant_001"
                || cardId == "BG34_Giant_001_G";
        }
    }
}

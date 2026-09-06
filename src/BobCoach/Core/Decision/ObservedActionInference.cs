using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    internal static class ObservedActionInference
    {
        public static IList<string> Infer(GameState previous, GameState current,
            IEnumerable<ActionDescriptor> candidates)
        {
            if (previous == null || current == null || candidates == null)
                return new List<string>();

            var currentBoardAndHand = new HashSet<int>(Items(current.BoardMinions)
                .Concat(Items(current.HandMinions)).Select(item => item.EntityId));
            var currentBoard = new HashSet<int>(Items(current.BoardMinions)
                .Select(item => item.EntityId));
            var matches = candidates
                .Where(candidate => Matches(candidate, previous, current,
                    currentBoardAndHand, currentBoard))
                .Select(candidate => candidate.ActionKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (matches.Count == 1) return matches;
            if (matches.Count > 1)
                return new List<string> { "observation|unresolved|unknown" };
            return new List<string>();
        }

        private static bool Matches(ActionDescriptor candidate, GameState previous,
            GameState current, ISet<int> currentBoardAndHand, ISet<int> currentBoard)
        {
            if (candidate == null || candidate.Action == null) return false;
            switch (candidate.Action.Type)
            {
                case ActionType.BuyMinion:
                    return MatchesPurchasedEntity(candidate, current, currentBoardAndHand);
                case ActionType.BuySpell:
                    return MatchesPurchasedEntity(candidate, current, currentBoardAndHand)
                        || MatchesImmediatelyConsumedSpell(candidate, previous, current,
                            currentBoardAndHand);
                case ActionType.SellMinion:
                    return candidate.EntityId > 0
                        && !currentBoard.Contains(candidate.EntityId)
                        && !Items(current.HandMinions)
                            .Any(item => item.EntityId == candidate.EntityId);
                case ActionType.PlayMinion:
                    return MatchesPlayedFromHand(candidate, previous, current, currentBoard);
                case ActionType.Refresh:
                    return MatchesRefresh(candidate, previous, current);
                case ActionType.Upgrade:
                    return current.TavernTier == previous.TavernTier + 1;
                case ActionType.FreezeShop:
                    return !previous.FrozenShop && current.FrozenShop;
                case ActionType.UseHeroPower:
                    return !previous.HeroPowerExhausted && current.HeroPowerExhausted;
                default:
                    return false;
            }
        }

        private static bool MatchesPlayedFromHand(ActionDescriptor candidate,
            GameState previous, GameState current, ISet<int> currentBoard)
        {
            if (candidate.EntityId <= 0 || previous.Turn != current.Turn)
                return false;
            bool wasInHand = Items(previous.HandMinions)
                .Any(item => item.EntityId == candidate.EntityId);
            bool wasOnBoard = Items(previous.BoardMinions)
                .Any(item => item.EntityId == candidate.EntityId);
            return wasInHand && !wasOnBoard && currentBoard.Contains(candidate.EntityId);
        }

        private static bool MatchesPurchasedEntity(ActionDescriptor candidate, GameState current,
            ISet<int> currentBoardAndHand)
        {
            return candidate.EntityId > 0
                && currentBoardAndHand.Contains(candidate.EntityId)
                && !Items(current.ShopMinions)
                    .Any(item => item.EntityId == candidate.EntityId);
        }

        private static bool MatchesImmediatelyConsumedSpell(ActionDescriptor candidate,
            GameState previous, GameState current, ISet<int> currentBoardAndHand)
        {
            return candidate.EntityId > 0
                && candidate.GoldCost >= 0
                && previous.Turn == current.Turn
                && Items(previous.ShopMinions)
                    .Any(item => item.EntityId == candidate.EntityId)
                && !Items(current.ShopMinions)
                    .Any(item => item.EntityId == candidate.EntityId)
                && !currentBoardAndHand.Contains(candidate.EntityId)
                && previous.Gold - current.Gold == candidate.GoldCost;
        }

        private static bool MatchesRefresh(ActionDescriptor candidate, GameState previous,
            GameState current)
        {
            if (previous.Turn != current.Turn)
                return false;

            var previousShopIds = PositiveEntityIds(previous.ShopMinions).ToList();
            var currentShopIds = PositiveEntityIds(current.ShopMinions).ToList();
            if (previousShopIds.Count == 0 || currentShopIds.Count == 0
                || previousShopIds.Intersect(currentShopIds).Any())
                return false;

            var goldSpent = previous.Gold - current.Gold;
            if (candidate.GoldCost == 0)
                return previous.FreeRefreshCount > current.FreeRefreshCount && goldSpent == 0;

            return candidate.GoldCost > 0
                && previous.FreeRefreshCount == current.FreeRefreshCount
                && goldSpent == candidate.GoldCost;
        }

        private static IEnumerable<MinionData> Items(IEnumerable<MinionData> items)
        {
            return items ?? Enumerable.Empty<MinionData>();
        }

        private static IEnumerable<int> PositiveEntityIds(IEnumerable<MinionData> items)
        {
            return Items(items).Where(item => item.EntityId > 0).Select(item => item.EntityId);
        }
    }
}

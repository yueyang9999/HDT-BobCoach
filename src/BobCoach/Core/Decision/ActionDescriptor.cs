using System;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public enum SimulatorSupport { None, Partial, Deterministic, ExpectedValue }

    public sealed class ActionDescriptor
    {
        public GameAction Action { get; private set; }
        public string ActionKey { get; private set; }
        public string SourceZone { get; private set; }
        public string TargetZone { get; private set; }
        public int EntityId { get; private set; }
        public int GoldCost { get; private set; }
        public SimulatorSupport SimulatorSupport { get; private set; }

        public static string CreateKey(GameAction action)
        {
            return string.Join("|", action.Type, action.TargetIndex,
                action.CardId ?? "", action.PurchaseSource ?? "");
        }

        public static ActionDescriptor From(GameState state, GameAction action)
        {
            return new ActionDescriptor
            {
                Action = action,
                ActionKey = CreateKey(action),
                SourceZone = SourceZoneFor(action.Type),
                TargetZone = TargetZoneFor(action.Type),
                EntityId = ResolveEntityId(state, action),
                GoldCost = ResolveGoldCost(state, action),
                SimulatorSupport = SupportFor(action.Type)
            };
        }

        private static string SourceZoneFor(ActionType type)
        {
            if (type == ActionType.BuyMinion || type == ActionType.BuySpell) return "shop";
            if (type == ActionType.SellMinion) return "board";
            if (type == ActionType.Activate) return "board";
            if (type == ActionType.PlayMinion || type == ActionType.UseSpell) return "hand";
            if (type == ActionType.PickTrinket || type == ActionType.PickDiscover) return "choice";
            return "tavern";
        }

        private static string TargetZoneFor(ActionType type)
        {
            if (type == ActionType.BuyMinion || type == ActionType.BuySpell
                || type == ActionType.PickDiscover) return "hand";
            if (type == ActionType.SellMinion) return "none";
            if (type == ActionType.Activate) return "board";
            if (type == ActionType.PlayMinion) return "board";
            if (type == ActionType.UseSpell) return "none";
            return "tavern";
        }

        private static int ResolveEntityId(GameState state, GameAction action)
        {
            if ((action.Type == ActionType.BuyMinion || action.Type == ActionType.BuySpell)
                && action.TargetIndex >= 0 && action.TargetIndex < state.ShopMinions.Count)
                return state.ShopMinions[action.TargetIndex].EntityId;
            if (action.Type == ActionType.SellMinion
                && action.TargetIndex >= 0 && action.TargetIndex < state.BoardMinions.Count)
                return state.BoardMinions[action.TargetIndex].EntityId;
            if (action.Type == ActionType.Activate
                && action.TargetIndex >= 0 && action.TargetIndex < state.BoardMinions.Count)
                return state.BoardMinions[action.TargetIndex].EntityId;
            if ((action.Type == ActionType.PlayMinion || action.Type == ActionType.UseSpell)
                && action.TargetIndex >= 0 && action.TargetIndex < state.HandMinions.Count)
                return state.HandMinions[action.TargetIndex].EntityId;
            if (action.Type == ActionType.PickTrinket
                && action.TargetIndex >= 0 && action.TargetIndex < state.TrinketOffer.Count)
                return state.TrinketOffer[action.TargetIndex].EntityId;
            if (action.Type == ActionType.PickDiscover
                && action.TargetIndex >= 0 && action.TargetIndex < state.DiscoverOptions.Count)
                return state.DiscoverOptions[action.TargetIndex].EntityId;
            return 0;
        }

        private static int ResolveGoldCost(GameState state, GameAction action)
        {
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            if ((action.Type == ActionType.BuyMinion || action.Type == ActionType.BuySpell)
                && action.TargetIndex >= 0 && action.TargetIndex < state.ShopMinions.Count)
                return GameRuleEvaluator.GetPurchaseCost(state, state.ShopMinions[action.TargetIndex],
                    state.HeroCardId, rules);
            if (action.Type == ActionType.Refresh)
                return GameRuleEvaluator.GetRefreshCost(state, state.HeroCardId, rules);
            if (action.Type == ActionType.Upgrade)
                return GameRuleEvaluator.GetUpgradeCost(state, rules) ?? int.MaxValue;
            if (action.Type == ActionType.UseHeroPower)
            {
                var power = state.HeroPowers == null
                    ? null
                    : state.HeroPowers.FirstOrDefault(x => x.CardId == action.CardId);
                return power == null ? state.HeroPowerCost : power.Cost;
            }
            if (action.Type == ActionType.SendGoldToTeammate)
            {
                var transferRule = rules.TeammateGoldTransfer;
                return transferRule != null && action.CardId == transferRule.ActionCardId
                    ? transferRule.GoldPerUse
                    : int.MaxValue;
            }
            if (action.Type == ActionType.Activate) return action.Cost;
            if (action.Type == ActionType.PlayMinion) return 0;
            if (action.Type == ActionType.UseSpell) return 0;
            return 0;
        }

        private static SimulatorSupport SupportFor(ActionType type)
        {
            if (type == ActionType.Refresh) return SimulatorSupport.ExpectedValue;
            if (type == ActionType.BuyMinion || type == ActionType.SellMinion
                || type == ActionType.Upgrade) return SimulatorSupport.Deterministic;
            if (type == ActionType.PlayMinion) return SimulatorSupport.Deterministic;
            if (type == ActionType.BuySpell || type == ActionType.FreezeShop
                || type == ActionType.UseHeroPower || type == ActionType.Activate
                || type == ActionType.UseSpell)
                return SimulatorSupport.Partial;
            if (type == ActionType.SendGoldToTeammate) return SimulatorSupport.Partial;
            return SimulatorSupport.None;
        }
    }
}

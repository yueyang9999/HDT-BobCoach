using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class ConstraintDecision
    {
        public ActionDescriptor Candidate { get; private set; }
        public bool Accepted { get; private set; }
        public string ReasonCode { get; private set; }

        public ConstraintDecision(ActionDescriptor candidate, bool accepted, string reasonCode)
        {
            Candidate = candidate;
            Accepted = accepted;
            ReasonCode = reasonCode;
        }
    }

    public sealed class HardConstraintFilter
    {
        public IList<ActionDescriptor> Apply(GameState state,
            IEnumerable<ActionDescriptor> candidates)
        {
            if (candidates == null) return new List<ActionDescriptor>();

            return candidates.Where(candidate => Evaluate(state, candidate).Accepted).ToList();
        }

        public ConstraintDecision Evaluate(GameState state, ActionDescriptor candidate)
        {
            if (state == null || candidate == null || candidate.Action == null)
                return Reject(candidate, "known-invalid-irreversible");

            if (HasInvalidSourceIndex(state, candidate.Action))
                return Reject(candidate, "invalid-source-index");

            if (candidate.GoldCost > state.Gold)
                return Reject(candidate, "insufficient-gold");

            if (HasInvalidTarget(state, candidate.Action))
                return Reject(candidate, "invalid-target");

            return new ConstraintDecision(candidate, true, "legal");
        }

        private static ConstraintDecision Reject(ActionDescriptor candidate, string reasonCode)
        {
            return new ConstraintDecision(candidate, false, reasonCode);
        }

        private static bool HasInvalidSourceIndex(GameState state, GameAction action)
        {
            if (action.Type == ActionType.BuyMinion || action.Type == ActionType.BuySpell)
                return action.TargetIndex < 0 || action.TargetIndex >= state.ShopMinions.Count;
            if (action.Type == ActionType.SellMinion)
                return action.TargetIndex < 0 || action.TargetIndex >= state.BoardMinions.Count;
            if (action.Type == ActionType.PickTrinket)
                return action.TargetIndex < 0 || action.TargetIndex >= state.TrinketOffer.Count;
            if (action.Type == ActionType.PickDiscover)
                return action.TargetIndex < 0 || action.TargetIndex >= state.DiscoverOptions.Count;
            return false;
        }

        private static bool HasInvalidTarget(GameState state, GameAction action)
        {
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            if (action.Type == ActionType.BuyMinion)
                return state.BoardMinions.Count >= state.MaxBoardSlots;
            if (action.Type == ActionType.BuySpell)
                return state.HandMinions.Count >= 10;
            if (action.Type == ActionType.Refresh)
                return !rules.ManualRefreshAllowed;
            if (action.Type == ActionType.Upgrade)
                return !GameRuleEvaluator.GetUpgradeCost(state, rules).HasValue;
            if (action.Type == ActionType.FreezeShop)
                return state.FrozenShop || state.ShopMinions.Count == 0;
            return false;
        }
    }
}

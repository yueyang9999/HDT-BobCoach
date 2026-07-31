using System;
using System.Collections.Generic;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class LegacyDecisionPolicy : IDecisionPolicy
    {
        private readonly IDecisionProvider _provider;

        public string PolicyId { get { return "legacy-v1.1.0"; } }

        public LegacyDecisionPolicy(IDecisionProvider provider)
        {
            _provider = provider;
        }

        public DecisionResult Decide(GameState state)
        {
            var action = _provider.GetBestAction(state);
            var ranked = new List<RankedAction>();
            if (action != null)
                ranked.Add(new RankedAction(action, CreateLegacyActionKey(action), 0d, null));

            return new DecisionResult(PolicyId, action, ranked, 1d,
                PlannerStatus.NotRequested, FallbackReason.None,
                action == null ? "" : action.Description);
        }

        private static string CreateLegacyActionKey(GameAction action)
        {
            return string.Join("|", action.Type, action.TargetIndex,
                action.CardId ?? "", action.PurchaseSource ?? "");
        }
    }

    public sealed class UnavailableDecisionPolicy : IDecisionPolicy
    {
        private readonly FallbackReason _reason;

        public string PolicyId { get { return "unavailable"; } }

        private UnavailableDecisionPolicy(FallbackReason reason)
        {
            if (reason == FallbackReason.None)
                throw new ArgumentException("reason");
            _reason = reason;
        }

        public static UnavailableDecisionPolicy For(FallbackReason reason)
        {
            return new UnavailableDecisionPolicy(reason);
        }

        public DecisionResult Decide(GameState state)
        {
            return new DecisionResult(PolicyId, null, new List<RankedAction>(), 0d,
                PlannerStatus.NotRequested, _reason, "");
        }
    }
}

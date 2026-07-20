using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>根据当前可观察的真实英雄身份核验全体英雄覆盖规则。</summary>
    public static class HeroRosterOverrideEvaluator
    {
        public static IList<HeroIdentityExpectation> Evaluate(
            AllHeroesOverrideRule rule,
            int localControllerId,
            string localHeroCardId,
            IEnumerable<OpponentData> opponents)
        {
            if (rule == null) return new List<HeroIdentityExpectation>();

            var observedByController = new Dictionary<int, string>();
            if (localControllerId > 0)
                observedByController[localControllerId] = localHeroCardId ?? "";
            if (opponents != null)
            {
                foreach (var opponent in opponents)
                {
                    if (opponent == null || opponent.ControllerId <= 0) continue;
                    if (opponent.ControllerId == localControllerId) continue;
                    observedByController[opponent.ControllerId] = opponent.HeroCardId ?? "";
                }
            }

            return observedByController
                .OrderBy(pair => pair.Key)
                .Select(pair => CreateExpectation(rule, pair.Key, pair.Value))
                .ToList();
        }

        private static HeroIdentityExpectation CreateExpectation(
            AllHeroesOverrideRule rule, int controllerId, string observedHeroCardId)
        {
            string status = string.IsNullOrEmpty(observedHeroCardId)
                ? "pending"
                : observedHeroCardId == rule.TargetHeroCardId
                    ? "observed" : "mismatched";
            return new HeroIdentityExpectation(
                controllerId, rule.TargetHeroCardId, observedHeroCardId,
                status, rule.SourceId);
        }
    }
}

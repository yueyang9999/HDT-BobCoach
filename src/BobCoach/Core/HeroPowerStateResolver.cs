using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>把技能实体事实解析为角色稳定、可独立消费的技能状态。</summary>
    public static class HeroPowerStateResolver
    {
        public static IList<HeroPowerState> Resolve(
            IEnumerable<HeroPowerObservation> observations,
            string primaryHeroPowerCardId,
            EffectiveGameRules rules,
            int turn,
            int tavernTier)
        {
            var observed = (observations ?? Enumerable.Empty<HeroPowerObservation>())
                .Where(item => item != null && !string.IsNullOrEmpty(item.CardId))
                .ToList();
            var result = new List<HeroPowerState>();
            var fixedSecondaryRules = (rules ?? EffectiveGameRules.Default)
                .SecondaryHeroPowers.ToDictionary(rule => rule.CardId, rule => rule);
            bool primaryAssigned = false;
            foreach (var item in observed)
            {
                bool isPrimary = !primaryAssigned
                    && item.CardId == (primaryHeroPowerCardId ?? "");
                if (isPrimary) primaryAssigned = true;
                SecondaryHeroPowerRule fixedRule;
                bool matchesFixedSecondary = fixedSecondaryRules.TryGetValue(
                    item.CardId, out fixedRule);
                int unlockTurn = matchesFixedSecondary
                    ? fixedRule.UnlockTurn
                    : (item.UnlockTurn > 0 ? item.UnlockTurn : 1);
                int unlockTier = item.UnlockTier > 0 ? item.UnlockTier : 1;
                result.Add(new HeroPowerState
                {
                    CardId = item.CardId,
                    EntityId = item.EntityId,
                    Cost = item.Cost,
                    Exhausted = item.Exhausted,
                    IsPrimary = isPrimary,
                    IsSecondary = !isPrimary,
                    IsActive = item.IsActive,
                    IsUnlocked = turn >= unlockTurn && tavernTier >= unlockTier,
                    HasDiscover = item.HasDiscover,
                    UnlockTurn = unlockTurn,
                    UnlockTier = unlockTier,
                    SpecialRule = item.SpecialRule ?? "",
                });
            }
            return result;
        }
    }
}

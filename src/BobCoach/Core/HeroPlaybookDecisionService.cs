using System;

namespace BobCoach.Engine
{
    public sealed class HeroPlaybookDecision
    {
        public bool ShouldUse;
        public int Cost;
        public double ActionValueBias;
        public string PhasePriority = "";
        public string Reason = "";
    }

    public static class HeroPlaybookDecisionService
    {
        public static HeroPlaybookDecision Decide(
            HeroPlaybookProfile profile,
            string powerType,
            int cost,
            int liveGold,
            int turn,
            int tavernTier,
            int health,
            int boardSize,
            int shopSize,
            bool exhausted,
            bool hasValidTarget)
        {
            if (profile == null)
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "missing-profile",
                };
            }

            if (powerType == "Passive" || powerType == "Aura")
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "passive",
                };
            }

            if (exhausted)
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "used-this-turn",
                };
            }

            if (liveGold < cost)
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "insufficient-gold",
                };
            }

            if (turn < profile.UnlockTurn
                || tavernTier < profile.UnlockTier)
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "locked",
                };
            }

            HeroPlaybookPhase phase = FindMatchingPhase(
                profile, turn, health, liveGold, cost);
            if (phase == null)
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "no-phase-match",
                };
            }

            bool requiresTarget = profile.RequiresTarget
                || phase.TargetRequired;
            if (requiresTarget && !hasValidTarget)
            {
                return new HeroPlaybookDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "invalid-target",
                };
            }

            double bias = PriorityBias(phase.Priority);
            if (profile.SpecialRule == "CENARIUS")
            {
                double futureTurnCount = Math.Max(0, 10 - turn);
                bias += Math.Min(1.20, futureTurnCount * 0.18);
            }
            // R253: cost-decay hero (Togwaggle -1/turn) - cheaper = more worth using,
            // real players seize the discount window; boost when cost below static PowerCost
            if (profile.SpecialRule == "COST_DECAY"
                && profile.PowerCost > 0 && cost < profile.PowerCost)
            {
                double saved = (double)(profile.PowerCost - cost) / profile.PowerCost;
                bias += Math.Min(0.35, saved * 0.5);
            }

            return new HeroPlaybookDecision
            {
                ShouldUse = true,
                Cost = cost,
                ActionValueBias = bias,
                PhasePriority = phase.Priority,
                Reason = string.IsNullOrWhiteSpace(phase.Reason)
                    ? profile.UsePurpose
                    : phase.Reason,
            };
        }

        private static HeroPlaybookPhase FindMatchingPhase(
            HeroPlaybookProfile profile,
            int turn,
            int health,
            int liveGold,
            int costAtLeast)
        {
            HeroPlaybookPhase selected = null;
            int selectedRank = int.MaxValue;
            foreach (var phase in profile.Phases)
            {
                if (phase == null
                    || turn < phase.MinTurn
                    || turn > phase.MaxTurn
                    || health < phase.HealthAtLeast
                    // R248: 费用递减英雄(托瓦格尔: 每回合技能-1)的 PowerCost 是静态
                    // 最高值(11), 用静态 GoldAtLeast 会在前期金币上限不足时永不匹配;
                    // 改用运行时费用作为下限。
                    || liveGold < Math.Max(costAtLeast, 0))
                    continue;
                int rank = PriorityRank(phase.Priority);
                if (rank < selectedRank)
                {
                    selected = phase;
                    selectedRank = rank;
                }
            }
            return selected;
        }

        private static int PriorityRank(string priority)
        {
            if (priority == "Critical") return 0;
            if (priority == "High") return 1;
            if (priority == "Normal") return 2;
            if (priority == "Low") return 3;
            return int.MaxValue;
        }

        private static double PriorityBias(string priority)
        {
            if (priority == "Critical") return 1.20;
            if (priority == "High") return 0.80;
            if (priority == "Normal") return 0.35;
            return 0.0;
        }
    }
}

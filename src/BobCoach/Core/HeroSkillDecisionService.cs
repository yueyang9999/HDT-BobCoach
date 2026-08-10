namespace BobCoach.Engine
{
    public sealed class HeroSkillDecision
    {
        public bool ShouldUse;
        public double Score;
        public int Cost;
        public string Reason = "";
    }

    /// <summary>
    /// 主动英雄技能使用决策。被动技能不提示；
    /// 主动技能必须通过费用、解锁、目标、性价比和机会成本门控。
    /// </summary>
    public static class HeroSkillDecisionService
    {
        public static HeroSkillDecision Decide(
            string powerType,
            int cost,
            int unlockTurn,
            int unlockTier,
            int liveGold,
            int turn,
            int tavernTier,
            int boardSize,
            int shopSize,
            string usePurpose,
            bool hasValidTarget)
        {
            if (powerType == "Passive" || powerType == "Aura")
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "passive",
                };
            }

            if (liveGold < cost)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "insufficient-gold",
                };
            }

            if (turn < unlockTurn || tavernTier < unlockTier)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "locked",
                };
            }

            if (usePurpose == "Buff" && boardSize <= 0)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "no-board-target",
                };
            }

            if (usePurpose == "Resource" && boardSize >= 7)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "board-full",
                };
            }

            if ((usePurpose == "Combat" || usePurpose == "Generic")
                && boardSize <= 0)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "no-board-target",
                };
            }

            if (usePurpose == "Economy")
            {
                return new HeroSkillDecision
                {
                    ShouldUse = liveGold - cost >= 0,
                    Score = 0.7,
                    Cost = cost,
                    Reason = "economy",
                };
            }

            bool targetOk = hasValidTarget || usePurpose == "Economy";
            if (!targetOk)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "invalid-target",
                };
            }

            double score = 0.55 + (liveGold - cost) * 0.05 + boardSize * 0.02;
            if (shopSize > 0 && usePurpose == "Resource") score += 0.15;
            return new HeroSkillDecision
            {
                ShouldUse = score > 0.5,
                Score = score,
                Cost = cost,
                Reason = "active",
            };
        }
    }
}
